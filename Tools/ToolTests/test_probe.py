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


if __name__ == '__main__':
    unittest.main()
