"""Regression tests for log loss, false success, and observation/protocol separation."""
import importlib.util
import json
import os
import signal
import time
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

TOOLS = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('probe', TOOLS / 'probe.py')
probe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)
PREFIX = '11-08 00:00:01.000 123 124 I '
HOST = [
    PREFIX + 'VRWebViewHost: [VRWebViewHost] HC2 setup bootKeyboard=0',
    PREFIX + 'VRWebViewHost: [VRWebViewHost] HC2 bootCheck expected=0 observed=0 valid=true',
    PREFIX + 'Unity: [VRWebViewHost] HC2 start startupCase=native-webinfo',
    PREFIX + 'VRWebViewHost: HC2 nativeConnection target=native-webinfo nonNull=true type=0xc0a1 imeOptions=0x12000005 profileMatch=true',
    PREFIX + 'Unity: [VRWebViewHost] HC2 tick appFocus=True paused=False nativeTrial=True pointerTracked=False pointerBlocked=True',
    PREFIX + 'Unity: renderer message unrelated to the probe prefix',
]


class AnalysisTests(unittest.TestCase):
    def test_unity_keyboard_timeouts_are_reported_even_after_browser_ready(self):
        report = probe.analyze([PREFIX + 'Unity: Timeout while waiting for keyboard to become visible'])
        self.assertEqual(report['unity_keyboard_timeouts'], 1)
        self.assertIn('unity_keyboard_visibility_timeout', report['issues'])
        self.assertEqual(report['keyboard_visibility'], 'requires_user_observation')

    def test_pointer_visual_failure_is_not_a_successful_interaction(self):
        report = probe.analyze([PREFIX + 'Unity: [VRWebViewSample] BS1 event=pointer_visual_failed reason=shader_missing'])
        self.assertIn('browser_pointer_visual_failed', report['issues'])
        self.assertEqual(report['pointer_visual_failures'], 1)

    def test_release_target_is_not_misreported_as_a_click(self):
        report = probe.analyze([PREFIX + 'Unity: [VRWebViewSample] BS1 event=activation press=address release=page click=False drag=False'])
        self.assertEqual(report['pointer_activations'][0]['click'], 'False')
        self.assertEqual(report['pointer_activations'][0]['press'], 'address')
        self.assertEqual(report['keyboard_visibility'], 'requires_user_observation')

    def test_address_handoff_abort_is_observed_without_claiming_keyboard_visibility(self):
        report = probe.analyze([PREFIX + 'Unity: [VRWebViewSample] BS1 event=address_edit phase=prepare request=1 selected=True',
                                PREFIX + 'Unity: [VRWebViewSample] BS1 event=address_edit phase=aborted reason=deselected'])
        self.assertEqual(report['address_edit_prepares'], 1)
        self.assertEqual(report['address_edit_aborts'], 1)
        self.assertEqual(report['address_keyboard_requests'], 0)
        self.assertEqual(report['keyboard_visibility'], 'requires_user_observation')

    def test_address_keyboard_failed_event_is_not_reported_as_success(self):
        report = probe.analyze([PREFIX + 'Unity: [VRWebViewSample] BS1 event=address_keyboard_failed reason=visibility_timeout'])
        self.assertIn('browser_address_keyboard_failed', report['issues'])
        self.assertEqual(report['address_keyboard_failures'], 1)

    def test_new_browser_ui_start_resets_previous_host_readiness(self):
        report = probe.analyze(['VRWebViewSample BS1 event=ui_ready', 'VRWebViewRuntime event=host_ready',
            'VRWebViewSample BS1 event=ui_ready'], 'browser-ui')
        self.assertIn('expected_browser_ui_android_host_missing', report['issues'])


    def test_previous_rt1_host_cannot_satisfy_a_later_browser_ui_trial(self):
        report = probe.analyze(['VRWebViewRuntime event=host_ready', 'VRWebViewSample BS1 event=ui_ready'], 'browser-ui')
        self.assertIn('expected_browser_ui_android_host_missing', report['issues'])

    def test_another_process_cannot_satisfy_the_browser_ui_host(self):
        report = probe.analyze(['11-09 12:00:00.000 123 124 I Unity: VRWebViewSample BS1 event=ui_ready',
            '11-09 12:00:00.100 456 457 I VRWebViewRuntime: event=host_ready'], 'browser-ui')
        self.assertIn('expected_browser_ui_android_host_missing', report['issues'])


    def test_retirement_metadata_does_not_claim_full_rendering_or_keyboard_success(self):
        report = probe.analyze(['VRWebViewRuntime event=target_retired host=2 nativeAlive=0 unityDestroyRequested=true'])
        self.assertEqual(report['runtime_targets_retired'], 1)
        self.assertEqual(report['keyboard_visibility'], 'requires_user_observation')


    def test_values_require_the_new_observer_and_a_closed_snapshot(self):
        lines = ['Unity: [VRWebViewUnityInput] U1 event=ready stock=True webview=False valueProbe=1',
            'Unity: [VRWebViewUnityInput] U2 event=ready stock=True maxExtraShows=1',
            'Unity: [VRWebViewUnityInput] U2 event=armed attempt=1 ',
            'Unity: [VRWebViewUnityInput] U2 event=show_result issued=True accepted=True reason=show_returned',
            'Unity: [VRWebViewUnityInput] U1 event=values phase=closed keyboardSource=end_snapshot keyboardStatus=Done stable=True fieldLength=5 keyboardLength=5 fieldEqualsKeyboard=True labelIsFieldSlice=True compositionLength=0 missingBmpGlyphs=0']
        report = probe.analyze(lines, 'unity-input-values')
        self.assertEqual(report['issues'], [])
        self.assertEqual(report['keyboard_visibility'], 'requires_user_observation')
        report = probe.analyze(lines[:-1], 'unity-input-values')
        self.assertIn('expected_unity_final_value_sample_missing', report['issues'])
        report = probe.analyze([row.replace('valueProbe=1', '') for row in lines], 'unity-input-values')
        self.assertIn('expected_unity_value_probe_missing', report['issues'])

    def test_final_values_disagreement_is_distinct_from_composition_and_glyph_coverage(self):
        lines = ['Unity: [VRWebViewUnityInput] U1 event=values phase=editing keyboardSource=live stable=True fieldEqualsKeyboard=False labelIsFieldSlice=False compositionLength=2 missingBmpGlyphs=0',
            'Unity: [VRWebViewUnityInput] U1 event=values phase=closed keyboardSource=end_snapshot keyboardStatus=Done stable=True fieldEqualsKeyboard=False labelIsFieldSlice=False compositionLength=0 missingBmpGlyphs=3']
        report = probe.analyze(lines, 'unity-input-values')
        self.assertEqual(report['unity_value_stable_mismatches'], 2)
        self.assertEqual(report['unity_value_final_mismatches'], 1)
        self.assertEqual(report['unity_value_label_mismatches'], 1)
        self.assertEqual(report['unity_value_font_missing_samples'], 1)
        self.assertIn('unity_final_field_keyboard_mismatch', report['issues'])

    def test_cancel_restore_is_not_reported_as_a_done_value_mismatch(self):
        report = probe.analyze(['Unity: [VRWebViewUnityInput] U1 event=values phase=closed keyboardSource=end_snapshot keyboardStatus=Canceled stable=True fieldEqualsKeyboard=False'], 'unity-input-values')
        self.assertEqual(report['unity_value_stable_mismatches'], 1)
        self.assertEqual(report['unity_value_final_mismatches'], 0)
        self.assertNotIn('unity_final_field_keyboard_mismatch', report['issues'])

    def test_unstable_or_missing_keyboard_snapshots_are_not_final_value_failures(self):
        report = probe.analyze([
            'Unity: [VRWebViewUnityInput] U1 event=values phase=closed keyboardSource=end_snapshot keyboardStatus=Done stable=False fieldEqualsKeyboard=False',
            'Unity: [VRWebViewUnityInput] U1 event=values phase=closed keyboardSource=missing stable=True fieldEqualsKeyboard=False'], 'unity-input-values')
        self.assertEqual(report['unity_value_final_mismatches'], 0)
        self.assertNotIn('unity_final_field_keyboard_mismatch', report['issues'])

    def test_u2_snapshot_keeps_history_and_does_not_clear_or_relaunch(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'input-method.txt'
            with mock.patch.object(probe.subprocess, 'run', return_value=subprocess.CompletedProcess([], 0, b'SoftInputShowHideHistory\n', b'')) as run:
                status = probe.input_method_snapshot(['adb', '-s', 'device'], path)
            self.assertTrue(status['valid'])
            self.assertIn('SoftInputShowHideHistory', path.read_text())
            self.assertEqual(run.call_args.args[0], ['adb', '-s', 'device', 'shell', 'dumpsys', 'input_method'])
            self.assertEqual(run.call_args.kwargs['timeout'], 15)

    def test_u2_snapshot_failure_keeps_a_nonempty_diagnostic(self):
        with tempfile.TemporaryDirectory() as folder:
            for name, reply in [('empty', subprocess.CompletedProcess([], 0, b'', b'')),
                                ('denied', subprocess.CompletedProcess([], 1, b'', b'permission denied'))]:
                path = Path(folder) / (name + '.txt')
                with mock.patch.object(probe.subprocess, 'run', return_value=reply):
                    status = probe.input_method_snapshot(['adb'], path)
                self.assertFalse(status['valid'])
                self.assertGreater(path.stat().st_size, 0)
            path = Path(folder) / 'timeout.txt'
            with mock.patch.object(probe.subprocess, 'run', side_effect=subprocess.TimeoutExpired(['adb'], 15)):
                status = probe.input_method_snapshot(['adb'], path)
            self.assertFalse(status['valid'])
            self.assertIn('TimeoutExpired', path.read_text())

    def test_unity_order_requires_the_stock_field_and_a_real_extra_show(self):
        lines = ['Unity: [VRWebViewUnityInput] U1 event=ready stock=True webview=False',
            'Unity: [VRWebViewUnityInput] U2 event=ready attempt=0 stock=True webview=False maxExtraShows=1',
            'Unity: [VRWebViewUnityInput] U2 event=armed attempt=1 ',
            'Unity: [VRWebViewUnityInput] U2 event=observation attempt=1 localInputType=0x80001 localImeOptions=0x2000006 ready=True',
            'Unity: [VRWebViewUnityInput] U2 event=show_dispatch attempt=1 ',
            'Unity: [VRWebViewUnityInput] U2 event=show_result attempt=1 issued=True accepted=True reason=show_returned']
        report = probe.analyze(lines, 'unity-input-order')
        self.assertEqual(report['issues'], [])
        baseline = probe.analyze(['Unity: Timeout while waiting for keyboard to become visible'] + lines, 'unity-input-order')
        self.assertNotIn('unity_keyboard_visibility_timeout', baseline['issues'])
        self.assertEqual(baseline['unity_keyboard_timeouts'], 1)
        self.assertTrue(baseline['unity_order_notes'])
        self.assertEqual(report['unity_order_dispatches'], 1)
        self.assertEqual(report['unity_order_observations'][0]['localInputType'], '0x80001')
        self.assertEqual(report['keyboard_visibility'], 'requires_user_observation')
        report = probe.analyze(lines[:-1], 'unity-input-order')
        self.assertIn('expected_unity_order_show_missing', report['issues'])
        report = probe.analyze(lines[:-1] + ['Unity: [VRWebViewUnityInput] U2 event=show_result attempt=1 issued=False accepted=False reason=cancelled'], 'unity-input-order')
        self.assertIn('expected_unity_order_show_missing', report['issues'])
        report = probe.analyze(lines[:1], 'unity-input-order')
        self.assertIn('expected_unity_order_probe_missing', report['issues'])

    def test_unity_order_records_readiness_timeout_and_rejection_without_asserting_visibility(self):
        lines = ['Unity: [VRWebViewUnityInput] U1 event=ready stock=True webview=False',
            'Unity: [VRWebViewUnityInput] U2 event=ready attempt=0 stock=True maxExtraShows=1',
            'Unity: [VRWebViewUnityInput] U2 event=armed attempt=1 ',
            'Unity: [VRWebViewUnityInput] U2 event=show_result attempt=1 issued=True accepted=False reason=show_returned',
            'Unity: [VRWebViewUnityInput] U2 event=ready_timeout attempt=1 ',
            'Unity: [VRWebViewUnityInput] U2 event=cancelled attempt=1 reason=focus_lost']
        report = probe.analyze(lines, 'unity-input-order')
        self.assertIn('unity_order_show_rejected', report['issues'])
        self.assertIn('unity_order_readiness_timeout', report['issues'])
        self.assertEqual(report['unity_order_cancellations'][0]['reason'], 'focus_lost')
        self.assertEqual(report['keyboard_visibility'], 'requires_user_observation')

    def test_stock_unity_input_control_has_no_browser_host_requirement(self):
        report = probe.analyze(['Unity: [VRWebViewUnityInput] U1 event=ready stock=True webview=False',
            'Unity: [VRWebViewUnityInput] U1 event=state selected=True fieldFocus=True keyboard=True visible=False appFocus=True active=False status=LostFocus frames=10',
            'Unity: [VRWebViewUnityInput] U1 event=text_changed'], 'unity-input')
        self.assertEqual(report['issues'], [])
        self.assertTrue(report['unity_input_ready'])
        self.assertEqual(report['unity_input_text_changes'], 1)
        self.assertEqual(report['unity_input_states'][0]['status'], 'LostFocus')
        self.assertEqual(report['keyboard_visibility'], 'requires_user_observation')
        report = probe.analyze(['VRWebViewRuntime event=host_ready'], 'unity-input')
        self.assertIn('expected_stock_unity_input_missing', report['issues'])

    def test_browser_ui_requires_its_ui_and_a_real_android_host(self):
        report = probe.analyze(['Unity: [VRWebViewSample] BS1 event=ui_ready',
            'Unity: [VRWebViewSample] BS1 event=snapshot configured=True tabs=2 active=2 state=Ready busy=False addressFocus=False frames=10 appFocus=True',
            'VRWebViewRuntime host=1 event=host_ready'], 'browser-ui')
        self.assertEqual(report['issues'], [])
        self.assertEqual(report['browser_ui_snapshots'][0]['tabs'], '2')
        self.assertEqual(report['keyboard_visibility'], 'requires_user_observation')

    def test_rt1_and_a_widget_only_scene_cannot_establish_browser_ui_success(self):
        report = probe.analyze(['VRWebViewRuntime RT1 state=Ready'], 'browser-ui')
        self.assertIn('expected_browser_ui_ready_missing', report['issues'])
        report = probe.analyze(['VRWebViewSample BS1 event=ui_ready'], 'browser-ui')
        self.assertIn('expected_browser_ui_android_host_missing', report['issues'])


    def test_native_hover_counts_do_not_assert_dom_or_keyboard_success(self):
        report = probe.analyze(['VRWebViewRuntime event=host_ready',
            'VRWebViewRuntime event=hover action=9', 'VRWebViewRuntime event=hover action=10'], 'runtime')
        self.assertEqual(report['runtime_hover_enter'], 1)
        self.assertEqual(report['runtime_hover_exit'], 1)
        self.assertEqual(report['issues'], [])
        self.assertNotIn('pointerover_success', report)
        self.assertEqual(report['keyboard_visibility'], 'requires_user_observation')


    def test_runtime_ready_is_not_a_keyboard_visibility_assertion(self):
        report = probe.analyze(['VRWebViewRuntime host=5 event=host_ready',
            'VRWebViewRuntime host=5 event=connection connected=true inputType=1',
            'VRWebViewRuntime host=5 event=window actual=false action=HOLD_LOSS viewFocus=true'], 'runtime')
        self.assertEqual(report['issues'], [])
        self.assertEqual(report['runtime_connections'], 1)
        self.assertEqual(report['runtime_window_events'][0]['action'], 'HOLD_LOSS')
        self.assertEqual(report['keyboard_visibility'], 'requires_user_observation')

    def test_runtime_failure_after_ready_is_not_hidden(self):
        report = probe.analyze(['VRWebViewRuntime event=host_ready', 'VRWebViewRuntime event=failed type=InvalidOperationException'], 'runtime')
        self.assertIn('runtime_browser_failed', report['issues'])

    def test_runtime_missing_or_unrelated_scene_is_rejected(self):
        report = probe.analyze(['Unity other scene is running', 'VRWebViewFocus probe=WF1 event=installed'], 'runtime')
        self.assertIn('expected_runtime_browser_ready_missing', report['issues'])


    def test_pointer_policy_keeps_target_classification_separate_from_ime_visibility(self):
        report = probe.analyze([
            'VRWebViewFocus probe=WF1 event=dom route=direct input=3 editable=false pointerPolicy=1 pointerTarget=media pointerDown=4 editorPointerDown=2 nonEditorPointerDown=2 blurredEditors=1 endedEditors=2'])
        self.assertTrue(report['pointer_focus_policy_seen'])
        self.assertEqual(report['window_ime_events'][0]['pointerTarget'], 'media')
        self.assertEqual(report['window_ime_events'][0]['blurredEditors'], '1')
        self.assertEqual(report['window_ime_events'][0]['endedEditors'], '2')
        self.assertEqual(report['keyboard_visibility'], 'requires_user_observation')

    def test_window_ime_observations_do_not_claim_keyboard_or_editing_success(self):
        report = probe.analyze([
            'VRWebViewFocus probe=WF1 event=installed supported=true enabled=true route=direct',
            'VRWebViewFocus connection nonNull=true count=1/1 type=0xc0a1',
            'VRWebViewFocus probe=WF1 event=armed',
            'VRWebViewFocus probe=WF1 event=window actual=false action=HOLD_LOSS viewFocus=true editor=true',
            'VRWebViewFocus probe=WF1 event=dom route=direct input=2 compositionStart=1 compositionEnd=1 editable=true'], 'window-ime')
        self.assertEqual(report['issues'], [])
        self.assertEqual(report['window_ime_events'][-1]['input'], '2')
        self.assertEqual(report['window_ime_events'][-1]['route'], 'direct')
        self.assertEqual(report['keyboard_visibility'], 'requires_user_observation')
        self.assertNotIn('ime_fixed', report)

    def test_window_ime_accepts_nonreproduction_with_real_editor_attempt(self):
        report = probe.analyze([
            'VRWebViewFocus probe=WF1 event=installed supported=true enabled=true route=direct',
            'VRWebViewFocus connection nonNull=true count=1/1',
            'VRWebViewFocus probe=WF1 event=armed'], 'window-ime')
        self.assertEqual(report['issues'], [])

    def test_window_ime_reports_unsupported_provider(self):
        report = probe.analyze([
            'VRWebViewFocus probe=WF1 event=installed supported=false enabled=true route=direct',
            'VRWebViewFocus connection nonNull=true count=1/1'], 'window-ime')
        self.assertIn('window_ime_provider_unsupported', report['issues'])
        self.assertIn('expected_window_ime_request_missing', report['issues'])

    def test_previous_focus_trace_cannot_establish_new_window_ime_workaround(self):
        report = probe.analyze([
            'VRWebViewFocus probe=FO1 event=focus_trace_installed',
            'VRWebViewFocus proxy=IP1 event=mode enabled=false'], 'window-ime')
        self.assertIn('expected_window_ime_guard_missing', report['issues'])
        self.assertIn('expected_editor_connection_missing', report['issues'])

    def test_system_focus_rows_are_optional_observations(self):
        report = probe.analyze([PREFIX + 'input_focus: [Focus entering Example,reason=synthetic]'])
        self.assertEqual(report['system_focus_rows'], 1)
        self.assertNotIn('focus_destination', report)

    def test_focus_trace_requires_direct_mode_and_observed_stack_without_inferring_cause(self):
        report = probe.analyze([
            'VRWebViewFocus probe=FO1 event=focus_trace_installed',
            'VRWebViewFocus proxy=IP1 event=mode enabled=false variant=isolated',
            'VRWebViewFocus probe=FO1 event=focus_call seq=1 kind=view_change current=Surface',
            'VRWebViewFocus probe=FO1 event=focus_frame seq=1 index=0 class=android.view.View method=requestFocus line=1'], 'focus')
        self.assertEqual(report['issues'], [])
        self.assertEqual(report['focus_calls'], 1)
        self.assertEqual(report['focus_frames'], 1)
        self.assertEqual(report['keyboard_visibility'], 'requires_user_observation')
        self.assertNotIn('focus_cause', report)

    def test_old_direct_logs_do_not_establish_new_focus_caller_evidence(self):
        report = probe.analyze([
            'VRWebViewFocus proxy=IP1 event=mode enabled=false',
            'VRWebViewFocus globalFocus WebView>Surface'], 'focus')
        self.assertIn('expected_focus_trace_missing', report['issues'])
        self.assertIn('expected_focus_call_stack_missing', report['issues'])

    def test_focus_stacks_without_direct_route_cannot_establish_direct_webview_trial(self):
        report = probe.analyze([
            'VRWebViewFocus probe=FO1 event=focus_trace_installed',
            'VRWebViewFocus probe=FO1 event=focus_call seq=1 kind=view_change',
            'VRWebViewFocus probe=FO1 event=focus_frame seq=1 index=0 class=Example method=run'], 'focus')
        self.assertIn('expected_direct_mode_missing', report['issues'])

    def test_combined_capture_requires_new_ime_trial_and_media_evidence_without_claiming_success(self):
        report = probe.analyze([
            'VRWebViewFocus proxy=IP1 event=mode enabled=true variant=isolated sourceProxy=false',
            'VRWebViewFocus proxy=IP1 event=armed generation=4',
            'VRWebViewFocus proxy=IP1 event=ime_request variant=isolated editor=borrowed accepted=true',
            'VRWebViewFocus video=bundled request=1 event=response status=206 start=0',
            'VRWebViewFocus host=1 video=bundled event=sample time=6.1 paused=false'], 'ime-video')
        self.assertEqual(report['issues'], [])
        self.assertTrue(report['isolated_ime_variant'])
        self.assertEqual(report['keyboard_visibility'], 'requires_user_observation')
        self.assertEqual(report['video_playback'], 'requires_runtime_analysis_and_user_observation')

    def test_old_ime_and_video_logs_cannot_establish_new_isolated_trial(self):
        report = probe.analyze([
            'VRWebViewFocus proxy=IP1 event=armed generation=4',
            'VRWebViewFocus proxy=IP1 event=ime_request accepted=true',
            'VRWebViewFocus video=bundled request=1 event=response status=206 start=0',
            'VRWebViewFocus video=bundled event=sample time=6.1 paused=false'], 'ime-video')
        self.assertIn('expected_isolated_ime_variant_missing', report['issues'])
        self.assertIn('expected_borrowed_ime_request_missing', report['issues'])

    def test_native_control_alone_cannot_establish_borrowed_input_viability(self):
        report = probe.analyze([
            'VRWebViewFocus proxy=IP1 event=mode enabled=true variant=isolated',
            'VRWebViewFocus proxy=IP1 event=armed generation=4',
            'VRWebViewFocus proxy=IP1 event=ime_request variant=isolated editor=native accepted=true'], 'ime-video')
        self.assertIn('expected_borrowed_ime_request_missing', report['issues'])
        self.assertIn('expected_video_response_missing', report['issues'])
        self.assertIn('expected_video_sample_missing', report['issues'])

    def test_video_only_capture_does_not_require_an_ime_attempt_or_prove_playback(self):
        report = probe.analyze([
            'VRWebViewFocus video=bundled request=1 event=response status=206 start=0',
            'VRWebViewFocus host=1 video=bundled event=sample time=6.1 paused=false'], 'video')
        self.assertEqual(report['issues'], [])
        self.assertEqual(report['video_responses'], 1)
        self.assertEqual(report['video_samples'], 1)
        self.assertEqual(report['proxy_events'], [])
        self.assertEqual(report['video_playback'], 'requires_runtime_analysis_and_user_observation')

    def test_old_ip1_logs_cannot_establish_video_diagnostics(self):
        report = probe.analyze(['VRWebViewFocus proxy=IP1 event=unity_tick frames=33'], 'video')
        self.assertIn('expected_video_response_missing', report['issues'])
        self.assertIn('expected_video_sample_missing', report['issues'])

    def test_request_without_page_sample_is_an_incomplete_video_capture(self):
        report = probe.analyze(['VRWebViewFocus video=bundled request=1 event=response status=206'], 'video')
        self.assertNotIn('expected_video_response_missing', report['issues'])
        self.assertIn('expected_video_sample_missing', report['issues'])

    def test_proxy_start_on_a_failed_host_reports_the_initialization_problem(self):
        report = probe.analyze([
            'VRWebViewFocus host=1 proxy=IP1 event=host_start_failed stage=proxy_construct error=NullPointerException',
            'VRWebViewFocus proxy=IP1 event=start_requested host=1',
            'VRWebViewFocus host=1 proxy=IP1 event=ime_request_rejected reason=host_unavailable'], 'proxy')
        self.assertIn('proxy_host_initialization_failed', report['issues'])
        self.assertIn('proxy_start_rejected', report['issues'])
        self.assertNotIn('proxy_attempt_missing', report['issues'])
        self.assertEqual(report['proxy_events'][0]['stage'], 'proxy_construct')
        self.assertEqual(report['proxy_events'][0]['error'], 'NullPointerException')

    def test_proxy_capture_requires_a_real_attempt_not_only_unity_heartbeat(self):
        report = probe.analyze(['VRWebViewFocus proxy=IP1 event=unity_tick frames=33'], 'proxy')
        self.assertIn('proxy_attempt_missing', report['issues'])

    def test_proxy_evidence_keeps_api_result_and_dom_metadata_separate_from_visibility(self):
        report = probe.analyze([
            'VRWebViewFocus proxy=IP1 event=armed generation=4',
            'VRWebViewFocus proxy=IP1 event=ime_request generation=4 accepted=true',
            'VRWebViewFocus proxy=IP1 event=operation generation=4 op=commitText outcome=false',
            'VRWebViewFocus proxy=IP1 event=dom input=0 compositionStart=0 editable=true'], 'proxy')
        self.assertEqual(report['issues'], [])
        self.assertEqual(report['proxy_events'][2]['outcome'], 'false')
        self.assertEqual(report['proxy_events'][3]['input'], '0')
        self.assertEqual(report['keyboard_visibility'], 'requires_user_observation')
        self.assertEqual(report['ray_visibility'], 'requires_user_observation')

    def test_old_focus_logs_cannot_establish_new_proxy_protocol(self):
        report = probe.analyze(['VRWebViewFocus host=3 imeRequest accepted=true'], 'proxy')
        self.assertIn('expected_proxy_protocol_missing', report['issues'])

    def test_empty_is_not_success(self):
        self.assertIn('empty_log', probe.analyze([], 'host')['issues'])

    def test_xr_focus_is_not_keyboard_or_ray_visibility(self):
        report = probe.analyze([PREFIX + 'Unity: [MetaXRFeature] OnSessionStateChange: 4 -> 5'])
        self.assertEqual(report['xr_transitions'][0]['after'], 5)
        self.assertEqual(report['keyboard_visibility'], 'requires_user_observation')
        self.assertEqual(report['ray_visibility'], 'requires_user_observation')
        self.assertEqual(report['trials'], [])
        self.assertIn('no_probe_rows', report['issues'])

    def test_profile_and_trial_gate_are_distinct(self):
        report = probe.analyze(HOST, 'host')
        self.assertEqual(report['issues'], [])
        self.assertEqual(report['editor_flags'][0]['profile_match'], 'true')
        self.assertEqual(report['editor_flags'][0]['input_type'], '0xc0a1')
        self.assertTrue(report['pointer_states'][0]['nativeTrial'])
        self.assertTrue(report['pointer_states'][0]['pointerBlocked'])
        self.assertEqual(report['trials'][0]['case'], 'native-webinfo')
        self.assertEqual(report['trials'][0]['pid'], 123)

    def test_trial_return_keeps_policy_permission_distinct_from_visibility(self):
        line = HOST[4] + ' pointerObservation=Ready pointerBlockReasons=NativeTrial rayPolicyPermitted=False raySuppressionReason=InputSuppressed'
        state = probe.analyze([line])['pointer_states'][0]
        self.assertEqual(state['block_reasons'], ['NativeTrial'])
        self.assertFalse(state['ray_policy_permitted'])
        self.assertEqual(probe.analyze([line])['ray_visibility'], 'requires_user_observation')

    def test_tracking_absence_is_not_input_suppression(self):
        line = HOST[4].replace('nativeTrial=True', 'nativeTrial=False').replace('pointerBlocked=True', 'pointerBlocked=False')
        line += ' pointerObservation=Ready pointerBlockReasons=None rayPolicyPermitted=False raySuppressionReason=NoInputSource'
        state = probe.analyze([line])['pointer_states'][0]
        self.assertEqual(state['block_reasons'], [])
        self.assertFalse(state['ray_policy_permitted'])
        self.assertEqual(state['ray_suppression_reason'], 'NoInputSource')

    def test_old_logs_do_not_get_invented_suppression_causes(self):
        state = probe.analyze(HOST)['pointer_states'][0]
        self.assertIsNone(state['block_reasons'])
        self.assertIsNone(state['observation_available'])

    def test_actual_boot_mismatch_is_reported(self):
        lines = [x.replace('observed=0 valid=true', 'observed=1 valid=false') for x in HOST]
        self.assertIn('boot_expectation_mismatch', probe.analyze(lines)['issues'])

    def test_profile_mismatch_is_reported(self):
        lines = [x.replace('profileMatch=true', 'profileMatch=false') for x in HOST]
        self.assertIn('editor_profile_mismatch', probe.analyze(lines)['issues'])

    def test_legacy_probe_tags_remain_collectable(self):
        self.assertIn('VRWebViewProbe:I', probe.TAGS)
        self.assertIn('VRWebViewGpuProbe:I', probe.TAGS)
        report = probe.analyze([PREFIX + 'VRWebViewGpuProbe: synthetic initialization error'])
        self.assertEqual(report['legacy_rows'], 1)
        self.assertNotIn('no_probe_rows', report['issues'])

    def test_wrong_probe_family_is_not_silently_accepted(self):
        report = probe.analyze(HOST, 'activity')
        self.assertGreater(report['host_rows'], 0)
        self.assertIn('expected_activity_rows_missing', report['issues'])


@unittest.skipIf(os.name == 'nt', 'Executable fake ADB fixture uses a POSIX shebang')
class CaptureTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='vrwebview-tool-test-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.adb = self.root / 'adb'
        self.calls = self.root / 'calls.jsonl'
        self.output = self.root / 'logs'
        self.adb.write_text('''#!/usr/bin/env python3
import json,os,sys
from pathlib import Path
with Path(os.environ['VRWEBVIEW_TEST_CALLS']).open('a') as f:f.write(json.dumps(sys.argv[1:])+'\\n')
mode=os.environ.get('VRWEBVIEW_TEST_MODE','normal')
if sys.argv[-1]=='get-state':
 if mode=='offline': print('device offline',file=sys.stderr);sys.exit(1)
 print('device');sys.exit(0)
if mode=='adb-error': print('synthetic ADB failure',file=sys.stderr);sys.exit(1)
if mode!='empty': print(os.environ['VRWEBVIEW_TEST_ROWS'],flush=True)
if mode=='hold':
 import time
 while True: time.sleep(0.1)
''')
        self.adb.chmod(0o755)
        self.env = dict(os.environ, VRWEBVIEW_ADB_EXE=str(self.adb),
                        VRWEBVIEW_LOG_DIR=str(self.output), VRWEBVIEW_TEST_CALLS=str(self.calls),
                        VRWEBVIEW_TEST_ROWS='\n'.join(HOST))

    def run_tool(self, *args, mode='normal', rows=None, wrapper=None):
        env = dict(self.env, VRWEBVIEW_TEST_MODE=mode)
        if rows is not None:
            env['VRWEBVIEW_TEST_ROWS'] = '\n'.join(rows)
        command = ['bash', str(TOOLS / wrapper)] if wrapper else [sys.executable, str(TOOLS / 'probe.py'), *args]
        return subprocess.run(command, env=env, capture_output=True, text=True, cwd=self.root)

    def report(self):
        files = list(self.output.glob('*.summary.json'))
        self.assertEqual(len(files), 1)
        return json.loads(files[0].read_text())

    def test_live_capture_keeps_rows_without_probe_prefix(self):
        result = self.run_tool('capture', '--expect', 'host')
        self.assertEqual(result.returncode, 0, result.stderr)
        report = self.report()
        log = self.output / report['log']
        self.assertEqual(log.read_text().splitlines(), HOST)
        self.assertEqual(report['rows'], len(HOST))
        calls = [json.loads(x) for x in self.calls.read_text().splitlines()]
        self.assertEqual(calls[0], ['get-state'])
        self.assertNotIn('-c', calls[1])  # Never clear the buffer.
        self.assertIn('VRWebViewHost:I', calls[1])
        self.assertIn('VRWebViewActivityGPU:I', calls[1])

    def test_window_ime_capture_adds_system_and_events_buffers(self):
        rows = [
            PREFIX + 'VRWebViewFocus: probe=WF1 event=installed supported=true enabled=true route=direct',
            PREFIX + 'VRWebViewFocus: connection nonNull=true count=1/1',
            PREFIX + 'VRWebViewFocus: probe=WF1 event=armed',
            PREFIX + 'input_focus: [Focus entering Example,reason=synthetic]']
        result = self.run_tool('capture', '--expect', 'window-ime', rows=rows)
        self.assertEqual(result.returncode, 0, result.stderr)
        calls = [json.loads(x) for x in self.calls.read_text().splitlines()]
        self.assertEqual(calls[1][:7], ['logcat', '-b', 'main', '-b', 'system', '-b', 'events'])
        for tag in probe.SYSTEM_FOCUS_TAGS:
            self.assertIn(tag, calls[1])
        self.assertEqual(self.report()['system_focus_rows'], 1)

    def test_offline_check_precedes_log_creation(self):
        result = self.run_tool('capture', mode='offline')
        self.assertEqual(result.returncode, 2)
        self.assertFalse(self.output.exists())

    def test_zero_bytes_are_explicit_failure(self):
        result = self.run_tool('capture', mode='empty')
        self.assertEqual(result.returncode, 2)
        self.assertIn('empty_log', self.report()['issues'])

    def test_capture_without_host_metadata_keeps_evidence_and_fails(self):
        rows = [PREFIX + 'Unity: [MetaXRFeature] OnSessionStateChange: 4 -> 5']
        result = self.run_tool('capture', '--expect', 'host', rows=rows)
        self.assertEqual(result.returncode, 2)
        report = self.report()
        self.assertIn('expected_host_rows_missing', report['issues'])
        self.assertEqual((self.output / report['log']).read_text().splitlines(), rows)

    def test_adb_failure_retains_stderr_and_partial_capture(self):
        result = self.run_tool('capture', mode='adb-error')
        self.assertEqual(result.returncode, 2)
        report = self.report()
        self.assertIn('adb_capture_failed', report['issues'])
        self.assertIn('synthetic ADB failure', (self.output / report['adb_stderr']).read_text())

    def test_capture_label_does_not_replace_actual_boot_value(self):
        rows = [x.replace('bootKeyboard=0', 'bootKeyboard=1') for x in HOST]
        result = self.run_tool('capture', '--label', 'off', rows=rows)
        self.assertEqual(result.returncode, 0, result.stderr)
        report = self.report()
        self.assertEqual(report['setup_boot_values'][0]['value'], '1')
        self.assertTrue(report['label_is_not_a_verified_boot_setting'])

    def test_legacy_host_entry_delegates_without_filtering(self):
        result = self.run_tool(wrapper='capture_host_probe.sh')
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.report()['host_rows'], 5)

    def test_legacy_activity_entry_preserves_host_rows(self):
        result = self.run_tool(wrapper='capture_activity_gpu_probe.sh')
        self.assertEqual(result.returncode, 2)
        report = self.report()
        self.assertEqual(report['host_rows'], 5)
        self.assertIn('expected_activity_rows_missing', report['issues'])

    def test_ctrl_c_finishes_live_capture_and_saves_summary(self):
        env = dict(self.env, VRWEBVIEW_TEST_MODE='hold')
        process = subprocess.Popen([sys.executable, str(TOOLS / 'probe.py'), 'capture', '--expect', 'host'],
                                   env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE, start_new_session=True)
        try:
            deadline = time.monotonic() + 5
            while time.monotonic() < deadline:
                logs = list(self.output.glob('*.log')) if self.output.exists() else []
                if logs and 'renderer message' in logs[0].read_text():
                    break
                time.sleep(0.01)
            else:
                self.fail('Live fixture did not write its raw log')
            process.send_signal(signal.SIGINT)
            stdout, stderr = process.communicate(timeout=10)
            self.assertEqual(process.returncode, 0, stderr.decode())
            report = self.report()
            self.assertTrue(report['interrupted'])
            self.assertEqual(report['issues'], [])
            self.assertEqual((self.output / report['log']).read_text().splitlines(), HOST)
        finally:
            if process.poll() is None:
                process.kill()
                process.communicate(timeout=5)

    def test_doctor_has_no_launch_or_force_stop_side_effect(self):
        result = self.run_tool('doctor')
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual([json.loads(x) for x in self.calls.read_text().splitlines()], [['get-state']])



class GameActivityCompatTests(unittest.TestCase):
    def values(self):
        return ['11-10 11:00:00.000 123 124 I Unity: [VRWebViewUnityInput] U1 event=ready stock=True webview=False valueProbe=1',
                '11-10 11:00:00.000 123 124 I Unity: [VRWebViewUnityInput] U2 event=ready stock=True maxExtraShows=1',
                '11-10 11:00:00.000 123 124 I Unity: [VRWebViewUnityInput] U2 event=armed attempt=1 ',
                '11-10 11:00:00.000 123 124 I Unity: [VRWebViewUnityInput] U2 event=show_result issued=True accepted=True',
                '11-10 11:00:00.000 123 124 I Unity: [VRWebViewUnityInput] U1 event=values phase=closed keyboardSource=end_snapshot keyboardStatus=Done stable=True fieldEqualsKeyboard=True']

    def test_compat_marker_and_notifications_do_not_prove_keyboard_mirror_visibility(self):
        rows = ['11-10 10:59:59.000 123 124 I VRWebViewGameIme: event=connection_patch revision=1 sameConnection=true google=4.4.0',
                *self.values(),
                '11-10 11:00:00.000 123 124 I VRWebViewGameIme: event=extract_request monitor=true length=3',
                '11-10 11:00:00.000 123 124 I VRWebViewGameIme: event=extract_update length=4']
        r=probe.analyze(rows, 'unity-input-compat')
        self.assertEqual(r['issues'], [])
        self.assertEqual(r['unity_compat_extract_requests'], 1)
        self.assertEqual(r['unity_compat_extract_updates'], 1)
        self.assertEqual(r['system_keyboard_text'], 'requires_user_observation')

    def test_other_process_cannot_establish_the_patch_was_loaded(self):
        r=probe.analyze(self.values()+['11-10 11:00:00.000 456 457 I VRWebViewGameIme: event=connection_patch revision=1 sameConnection=true'], 'unity-input-compat')
        self.assertIn('expected_gameactivity_compat_patch_missing',r['issues'])

    def test_absent_extract_request_is_a_diagnostic_result_not_assumed_success_or_failure(self):
        r=probe.analyze(self.values()+['11-10 11:00:00.000 123 124 I VRWebViewGameIme: event=connection_patch revision=1 sameConnection=true'], 'unity-input-compat')
        self.assertEqual(r['issues'], [])
        self.assertIn('ime_did_not_request_extracted_text_during_capture',r['unity_compat_notes'])


class BrowserInputCompatTests(unittest.TestCase):
    def rows(self):
        return [
            '11-10 11:00:00.000 123 124 I VRWebViewGameIme: event=connection_patch revision=2 google=4.4.0 sameConnection=true showOrder=connection_then_show',
            '11-10 11:00:00.000 123 124 I Unity: [VRWebViewSample] BS1 event=ui_ready version=44',
            '11-10 11:00:00.000 123 124 I VRWebViewRuntime: event=host_ready',
            '11-10 11:00:00.000 123 124 I Unity: [VRWebViewSample] BS1 event=address_keyboard_request request=1',
            '11-10 11:00:00.000 123 124 I VRWebViewGameIme: event=show_requested localInputType=524289',
            '11-10 11:00:00.000 123 124 I VRWebViewGameIme: event=show_result issued=true accepted=true inputType=524289',
            '11-10 11:00:00.000 123 124 I VRWebViewGameIme: event=extract_request monitor=true length=0',
            '11-10 11:00:00.000 123 124 I VRWebViewGameIme: event=extract_update length=3']

    def test_browser_uses_revision_two_without_requiring_the_u2_extra_show(self):
        r=probe.analyze(self.rows(),'browser-input-compat')
        self.assertEqual(r['issues'],[])
        self.assertTrue(r['unity_compat_ready'])
        self.assertEqual(r['unity_compat_show_requests'],1)
        self.assertEqual(r['system_keyboard_text'],'requires_user_observation')

    def test_wrong_revision_or_process_cannot_establish_the_browser_patch(self):
        for rows in ([x.replace('revision=2','revision=1') for x in self.rows()],
                     [x.replace('123 124','456 457') if 'VRWebViewGameIme' in x else x for x in self.rows()]):
            r=probe.analyze(rows,'browser-input-compat')
            self.assertIn('expected_gameactivity_order_patch_missing',r['issues'])

    def test_stock_timeout_and_accidental_u2_retries_are_failures_for_browser_input(self):
        rows=self.rows()+['Unity: Timeout while waiting for keyboard to become visible',
                         'Unity: [VRWebViewUnityInput] U2 event=show_dispatch attempt=1']
        r=probe.analyze(rows,'browser-input-compat')
        self.assertIn('unity_keyboard_visibility_timeout',r['issues'])
        self.assertIn('unexpected_u2_extra_show_probe',r['issues'])

    def test_missing_address_attempt_or_rejected_expired_show_remains_observable(self):
        r=probe.analyze([x for x in self.rows() if 'address_keyboard_request' not in x],'browser-input-compat')
        self.assertIn('expected_address_keyboard_request_missing',r['issues'])
        rows=[x.replace('accepted=true','accepted=false') for x in self.rows()]+['11-10 11:00:00.000 123 124 I VRWebViewGameIme: event=show_timeout']
        r=probe.analyze(rows,'browser-input-compat')
        self.assertIn('gameactivity_show_rejected',r['issues'])
        self.assertIn('gameactivity_show_timeout',r['issues'])


if __name__ == '__main__':
    unittest.main()
