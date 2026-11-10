"""Common diagnostics for the frozen VRWebView experiments (Python standard library only)."""
import argparse
from datetime import datetime
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
TAGS = ('VRWebViewRuntime:I', 'VRWebViewHost:I', 'VRWebViewActivityGPU:I', 'VRWebViewFocus:I', 'VRWebViewProbe:I', 'VRWebViewGpuProbe:I', 'Unity:I',
        'AndroidRuntime:E', 'KeyboardInputMethodService:V', 'ImeTracker:V',
        'InputMethodManager:V', 'VRWebViewUnityInput:I', 'VRWebViewGameIme:I', '*:S')
SYSTEM_FOCUS_TAGS = ('WindowManager:I', 'InputDispatcher:I', 'input_focus:I')


class ProbeError(Exception):
    pass


def analyze(lines, expected='any'):
    """Summarize recorded metadata; do not infer HMD visibility or trial names from order."""
    result = dict(unity_input_pid=None, unity_compat_events=[], unity_compat_ready=False, unity_compat_extract_requests=0, unity_compat_extract_updates=0, unity_compat_show_requests=0, unity_compat_show_results=[], unity_compat_show_timeouts=0, unity_compat_state_notifications=0, unity_compat_state_restarts=0, unity_compat_native_notifications=0, unity_compat_key_notifications=0, unity_compat_notes=[], system_keyboard_text='requires_user_observation', unity_value_probe_ready=False, unity_value_samples=[], unity_value_stable_mismatches=0, unity_value_final_mismatches=0, unity_value_label_mismatches=0, unity_value_font_missing_samples=0, unity_order_notes=[], unity_order_ready=False, unity_order_arms=0, unity_order_dispatches=0, unity_order_shows=[], unity_order_observations=[], unity_order_timeouts=0, unity_order_cancellations=[], unity_input_rows=0, unity_input_ready=False, unity_input_text_changes=0, unity_input_states=[], pointer_visual_failures=0, pointer_activations=[], address_edit_prepares=0, address_keyboard_requests=0, address_edit_aborts=0, address_keyboard_failures=0, unity_keyboard_timeouts=0, keyboard_configurations=[], rows=0, browser_ui_rows=0, browser_ui_ready=False, browser_ui_pid=None, browser_ui_android_ready=False, browser_ui_snapshots=[], runtime_rows=0, runtime_ready=False, runtime_connections=0, runtime_targets_retired=0, runtime_hover_enter=0, runtime_hover_exit=0, runtime_failures=0, runtime_window_events=[], host_rows=0, activity_rows=0, focus_rows=0, legacy_rows=0, trials=[],
                  setup_boot_values=[], boot_checks=[], editor_flags=[], xr_transitions=[],
                  pointer_states=[], proxy_rows=0, proxy_events=[], issues=[], keyboard_visibility='requires_user_observation',
                  ray_visibility='requires_user_observation', video_responses=0, video_samples=0,
                  isolated_ime_variant=False,
                  focus_trace_installed=False, focus_calls=0, focus_frames=0, direct_mode_seen=False,
                  window_ime_installed=False, window_ime_supported=None, window_ime_events=[],
                  editor_connections=0, system_focus_rows=0,
                  pointer_focus_policy_seen=False,
                  video_playback='requires_runtime_analysis_and_user_observation')
    for number, line in enumerate(lines, 1):
        if not line.strip():
            continue
        result['rows'] += 1
        if 'VRWebViewGameIme' in line:
            fields = dict(re.findall(r'\b(event|revision|google|sameConnection|monitor|length|showOrder|issued|accepted|inputType|imeOptions|localInputType|connectedInputType|viewFocus|windowFocus|reason|stateUpdates|restart|monitored|source)=(\S+)', line))
            pid = re.match(r'^\S+\s+\S+\s+(\d+)\s+\d+\s+\S\s', line)
            result['unity_compat_events'].append(dict(line=number, pid=pid[1] if pid else None, **fields))
        if 'VRWebViewUnityInput' in line and 'U1 ' in line:
            if 'event=ready ' in line:
                pid = re.match(r'^\S+\s+\S+\s+(\d+)\s+\d+\s+\S\s', line)
                result['unity_input_pid'] = pid[1] if pid else None
            result['unity_input_rows'] += 1
            result['unity_input_ready'] |= 'event=ready ' in line and 'stock=True' in line and 'webview=False' in line
            result['unity_input_text_changes'] += int('event=text_changed' in line)
            result['unity_value_probe_ready'] |= 'event=ready ' in line and 'valueProbe=1' in line
            if 'event=values ' in line:
                fields = dict(re.findall(r'\b(phase|frames|keyboardSource|keyboardStatus|stable|fieldLength|keyboardLength|fieldEqualsKeyboard|labelLength|labelIsFieldSlice|emptyLabel|compositionLength|missingBmpGlyphs|surrogateUnits|fieldFocus|appFocus)=(\S+)', line))
                result['unity_value_samples'].append(dict(line=number, **fields))
                comparable = fields.get('stable') == 'True' and fields.get('keyboardSource') in ('live', 'end_snapshot')
                mismatch = comparable and fields.get('fieldEqualsKeyboard') == 'False'
                result['unity_value_stable_mismatches'] += int(mismatch)
                result['unity_value_final_mismatches'] += int(mismatch and fields.get('phase') == 'closed' and fields.get('keyboardStatus') == 'Done')
                result['unity_value_label_mismatches'] += int(fields.get('phase') in ('editing', 'closed') and fields.get('compositionLength') == '0' and fields.get('labelIsFieldSlice') == 'False')
                result['unity_value_font_missing_samples'] += int(re.fullmatch(r'0*[1-9][0-9]*', fields.get('missingBmpGlyphs', '0')) is not None)

            if 'event=state ' in line:
                result['unity_input_states'].append(dict(re.findall(r'\b(selected|fieldFocus|keyboard|visible|appFocus|active|status|frames)=(\w+)', line)))
        if 'VRWebViewUnityInput' in line and 'U2 ' in line:
            result['unity_input_rows'] += 1
            result['unity_order_ready'] |= 'event=ready ' in line and 'stock=True' in line and 'maxExtraShows=1' in line
            result['unity_order_arms'] += int('event=armed ' in line)
            result['unity_order_dispatches'] += int('event=show_dispatch ' in line)
            result['unity_order_timeouts'] += int('event=ready_timeout ' in line)
            fields = dict(re.findall(r'\b(attempt|localInputType|localImeOptions|viewFocus|windowFocus|immActive|ready|reason|stage|issued|accepted)=(\S+)', line))
            if 'event=show_result ' in line: result['unity_order_shows'].append(dict(line=number, **fields))
            if 'event=observation ' in line: result['unity_order_observations'].append(dict(line=number, **fields))
            if 'event=cancelled ' in line: result['unity_order_cancellations'].append(dict(line=number, **fields))
        if 'event=pointer_visual_failed' in line:
            result['pointer_visual_failures'] += 1
        if 'event=activation' in line and 'VRWebViewSample' in line:
            result['pointer_activations'].append(dict(re.findall(r'(press|release|click|drag)=(\S+)', line)))
        if 'event=address_edit phase=prepare' in line:
            result['address_edit_prepares'] += 1
        if 'event=address_keyboard_request' in line:
            result['address_keyboard_requests'] += 1
        if 'event=address_edit phase=aborted' in line:
            result['address_edit_aborts'] += 1
        if 'event=address_keyboard_failed' in line:
            result['address_keyboard_failures'] += 1
        if 'Timeout while waiting for keyboard to become visible' in line:
            result['unity_keyboard_timeouts'] += 1
        if 'event=keyboard_configuration' in line:
            result['keyboard_configurations'].append(dict(re.findall(r'(bootKeyboard|overlayDeclared)=(\S+)', line)))
        actor = re.match(r'\d\d-\d\d\s+[\d:.]+\s+(\d+)\s+\d+\s', line)
        actor_pid = int(actor[1]) if actor else None
        if 'VRWebViewSample' in line and 'BS1 ' in line:
            result['browser_ui_rows'] += 1
            if 'event=ui_ready' in line:
                result['browser_ui_ready'] = True; result['browser_ui_pid'] = actor_pid
                result['browser_ui_android_ready'] = False
            if 'event=snapshot' in line:
                fields = dict(re.findall(r'\b(configured|tabs|active|state|busy|addressFocus|frames|appFocus)=([A-Za-z0-9_-]+)', line))
                result['browser_ui_snapshots'].append(dict(line=number, **fields))
        if 'VRWebViewRuntime' in line:
            result['runtime_rows'] += 1
            if result['browser_ui_ready'] and actor_pid == result['browser_ui_pid'] and 'event=host_ready' in line:
                result['browser_ui_android_ready'] = True
            result['runtime_targets_retired'] += int('event=target_retired' in line)
            result['runtime_failures'] += int('event=failed' in line)
            result['runtime_hover_enter'] += int('event=hover action=9' in line)
            result['runtime_hover_exit'] += int('event=hover action=10' in line)
            result['runtime_ready'] |= 'event=host_ready' in line or 'RT1 state=Ready' in line
            result['runtime_connections'] += int('event=connection connected=true' in line)
            window = re.search(r'event=window actual=(true|false) action=(FORWARD|HOLD_LOSS|KEEP_GAIN)', line)
            if window: result['runtime_window_events'].append(dict(line=number, actual=window[1], action=window[2]))
        host = 'VRWebViewHost' in line
        result['host_rows'] += int(host)
        result['activity_rows'] += int('VRWebViewActivityGPU' in line)
        result['focus_rows'] += int('VRWebViewFocus' in line)
        result['legacy_rows'] += int('VRWebViewProbe' in line or 'VRWebViewGpuProbe' in line)
        pid = re.match(r'\d\d-\d\d\s+[\d:.]+\s+(\d+)\s+\d+\s', line)
        location = dict(line=number, pid=int(pid[1]) if pid else None)
        result['system_focus_rows'] += int(any(tag + ':' in line for tag in ('WindowManager', 'InputDispatcher', 'input_focus')))
        if 'probe=WF1 ' in line and 'VRWebViewFocus' in line:
            event = re.search(r'\bevent=([a-z_]+)', line)
            if event:
                fields = dict(re.findall(r'\b(enabled|supported|route|actual|action|viewFocus|editor|reason|resumed|actualWindow|input|compositionStart|compositionUpdate|compositionEnd|selectionChanges|editable|frames|appFocus|paused|tracked|pressed|source|pointerPolicy|pointerTarget|pointerDown|editorPointerDown|nonEditorPointerDown|blurredEditors|endedEditors|cleared|documentFocused)=([A-Za-z0-9_]+)', line))
                result['window_ime_events'].append(dict(location, event=event[1], **fields))
                result['pointer_focus_policy_seen'] |= event[1] == 'dom' and fields.get('pointerPolicy') == '1'
                if event[1] == 'installed':
                    result['window_ime_installed'] = True
                    result['window_ime_supported'] = fields.get('supported') == 'true'
                    result['direct_mode_seen'] |= fields.get('route') == 'direct'
        if 'VRWebViewFocus' in line and 'connection nonNull=true' in line:
            result['editor_connections'] += 1
        if 'probe=FO1 ' in line:
            result['focus_trace_installed'] |= 'event=focus_trace_installed' in line
            result['focus_calls'] += int('event=focus_call ' in line)
            result['focus_frames'] += int('event=focus_frame ' in line)
        if 'VRWebViewFocus' in line and 'video=bundled ' in line:
            result['video_responses'] += int('event=response ' in line)
            result['video_samples'] += int('event=sample ' in line)
        if 'proxy=IP1' in line:
            result['proxy_rows'] += 1
            event = re.search(r'\bevent=([a-z_]+)', line)
            if event:
                fields = dict(re.findall(r'\b(generation|op|outcome|enabled|connected|sourceFocus|sourceWindow|proxyFocus|windowFocus|accepted|frames|appFocus|paused|tracked|pressed|source|input|compositionStart|compositionUpdate|compositionEnd|selectionChanges|editable|stage|error|reason|host|variant|editor|query|target|focused|changes|composing|route)=([A-Za-z0-9_]+)', line))
                result['proxy_events'].append(dict(location, event=event[1], **fields))
                if event[1] == 'mode' and fields.get('variant') == 'isolated':
                    result['isolated_ime_variant'] = True
                if event[1] == 'mode' and fields.get('enabled') == 'false':
                    result['direct_mode_seen'] = True
        if host:
            trial = re.search(r'(?:startupCase=|run case=)([a-z-]+)', line)
            if trial:
                result['trials'].append(dict(location, case=trial[1]))
            boot = re.search(r'bootKeyboard=(\w+)', line)
            if boot:
                result['setup_boot_values'].append(dict(location, value=boot[1]))
            check = re.search(r'bootCheck expected=(\w+) observed=(\w+) valid=(true|false)', line)
            if check:
                result['boot_checks'].append(dict(location, expected=check[1], observed=check[2], valid=check[3] == 'true'))
            flags = re.search(r'\btype=(0x[0-9a-f]+).*?imeOptions=(0x[0-9a-f]+)', line)
            if flags:
                target = re.search(r'target=([a-z-]+)', line)
                match = re.search(r'profileMatch=(true|false|not-requested)', line)
                result['editor_flags'].append(dict(location, input_type=flags[1], ime_options=flags[2],
                    target=target[1] if target else 'webview', profile_match=match[1] if match else None))
            if 'tick ' in line:
                fields = dict(re.findall(r'\b(appFocus|paused|nativeTrial|pointerTracked|pointerBlocked)=(True|False|true|false)', line))
                if fields:
                    state = dict(location, **{k: v.lower() == 'true' for k, v in fields.items()})
                    observation = re.search(r'pointerObservation=(Ready|Unavailable)', line)
                    reasons = re.search(r'pointerBlockReasons=([A-Za-z|]+)', line)
                    permitted = re.search(r'rayPolicyPermitted=(True|False|true|false)', line)
                    suppression = re.search(r'raySuppressionReason=([A-Za-z]+)', line)
                    state['observation_available'] = observation[1] == 'Ready' if observation else None
                    state['block_reasons'] = ([] if reasons[1] == 'None' else reasons[1].split('|')) if reasons else None
                    state['ray_policy_permitted'] = permitted[1].lower() == 'true' if permitted else None
                    state['ray_suppression_reason'] = suppression[1] if suppression else None
                    result['pointer_states'].append(state)
        transition = re.search(r'(?:OnSessionStateChange:\s*|sessionState=)(\d+)\s*(?:->|>)\s*(\d+)', line)
        if transition:
            result['xr_transitions'].append(dict(location, before=int(transition[1]), after=int(transition[2])))
    owner_pid = (str(result['browser_ui_pid']) if result['browser_ui_pid'] is not None else None) if expected == 'browser-input-compat' else result['unity_input_pid']
    compat = [row for row in result['unity_compat_events'] if row.get('pid') == owner_pid]
    result['unity_compat_ready'] = any(row.get('event') == 'connection_patch' and row.get('revision') in ('1', '2', '3')
        and row.get('sameConnection') == 'true' for row in compat)
    result['unity_compat_extract_requests'] = sum(row.get('event') == 'extract_request' for row in compat)
    result['unity_compat_extract_updates'] = sum(row.get('event') == 'extract_update' for row in compat)
    result['unity_compat_show_requests'] = sum(row.get('event') == 'show_requested' for row in compat)
    result['unity_compat_show_results'] = [row for row in compat if row.get('event') == 'show_result']
    result['unity_compat_show_timeouts'] = sum(row.get('event') == 'show_timeout' for row in compat)
    result['unity_compat_state_notifications'] = sum(row.get('event') == 'state_applied' and row.get('restart') == 'false' for row in compat)
    result['unity_compat_state_restarts'] = sum(row.get('event') == 'state_applied' and row.get('restart') == 'true' for row in compat)
    result['unity_compat_native_notifications'] = sum(row.get('event') == 'state_applied' and row.get('restart') == 'false' and row.get('source') == 'native' for row in compat)
    result['unity_compat_key_notifications'] = sum(row.get('event') == 'state_applied' and row.get('restart') == 'false' and row.get('source') == 'key' for row in compat)
    if expected == 'browser-input-compat':
        if not any(row.get('event') == 'connection_patch' and row.get('revision') in ('2', '3')
                   and row.get('showOrder') == 'connection_then_show' for row in compat):
            result['issues'].append('expected_gameactivity_order_patch_missing')
        if not any(row.get('event') == 'connection_patch' and row.get('revision') == '3'
                   and row.get('stateUpdates') == 'notify' for row in compat):
            result['issues'].append('expected_gameactivity_state_notify_patch_missing')
        if not result['address_keyboard_requests']: result['issues'].append('expected_address_keyboard_request_missing')
        if not result['unity_compat_show_requests']: result['issues'].append('expected_gameactivity_show_request_missing')
        if not any(row.get('issued') == 'true' and row.get('accepted') == 'true' for row in result['unity_compat_show_results']):
            result['issues'].append('expected_gameactivity_show_missing')
        if any(row.get('accepted') == 'false' for row in result['unity_compat_show_results']):
            result['issues'].append('gameactivity_show_rejected')
        if result['unity_compat_show_timeouts']: result['issues'].append('gameactivity_show_timeout')
        if result['unity_order_dispatches']: result['issues'].append('unexpected_u2_extra_show_probe')

    if expected in ('unity-input-compat', 'browser-input-compat'):
        if not result['unity_compat_ready']: result['issues'].append('expected_gameactivity_compat_patch_missing')
        if not result['unity_compat_extract_requests']:
            result['unity_compat_notes'].append('ime_did_not_request_extracted_text_during_capture')
    if not result['rows']:
        result['issues'].append('empty_log')
    if not (result['unity_input_rows'] or result['browser_ui_rows'] or result['runtime_rows'] or result['host_rows'] or result['activity_rows'] or result['focus_rows'] or result['legacy_rows']):
        result['issues'].append('no_probe_rows')
    if result['pointer_visual_failures']:
        result['issues'].append('browser_pointer_visual_failed')
    if result['address_keyboard_failures']:
        result['issues'].append('browser_address_keyboard_failed')
    if result['unity_keyboard_timeouts']:
        if expected in ('unity-input-order', 'unity-input-values', 'unity-input-compat'):
            result['unity_order_notes'].append('stock_open_timed_out_evaluate_extra_show_separately')
        else:
            result['issues'].append('unity_keyboard_visibility_timeout')
    if result['runtime_failures']:
        result['issues'].append('runtime_browser_failed')
    if expected == 'unity-input' and not result['unity_input_ready']:
        result['issues'].append('expected_stock_unity_input_missing')
    if expected in ('unity-input-order', 'unity-input-values', 'unity-input-compat'):
        if not result['unity_input_ready']: result['issues'].append('expected_stock_unity_input_missing')
        if not result['unity_order_ready']: result['issues'].append('expected_unity_order_probe_missing')
        if not result['unity_order_arms']: result['issues'].append('expected_unity_order_attempt_missing')
        if not any(row.get('issued') == 'True' for row in result['unity_order_shows']):
            result['issues'].append('expected_unity_order_show_missing')
        if any(row.get('issued') == 'True' and row.get('accepted') == 'False' for row in result['unity_order_shows']):
            result['issues'].append('unity_order_show_rejected')
        if result['unity_order_timeouts']: result['issues'].append('unity_order_readiness_timeout')
    if expected in ('unity-input-values', 'unity-input-compat'):
        if not result['unity_value_probe_ready']: result['issues'].append('expected_unity_value_probe_missing')
        if not result['unity_value_samples']: result['issues'].append('expected_unity_value_samples_missing')
        if not any(row.get('phase') == 'closed' for row in result['unity_value_samples']):
            result['issues'].append('expected_unity_final_value_sample_missing')
        if not any(row.get('phase') == 'closed' and row.get('keyboardStatus') == 'Done'
                   and row.get('stable') == 'True' and row.get('keyboardSource') == 'end_snapshot'
                   for row in result['unity_value_samples']):
            result['issues'].append('expected_stable_done_value_snapshot_missing')
        if result['unity_value_final_mismatches']: result['issues'].append('unity_final_field_keyboard_mismatch')
    if expected in ('browser-ui', 'browser-input-compat'):
        if not result['browser_ui_ready']: result['issues'].append('expected_browser_ui_ready_missing')
        if not result['browser_ui_android_ready']: result['issues'].append('expected_browser_ui_android_host_missing')
    if expected == 'runtime' and not result['runtime_ready']:
        result['issues'].append('expected_runtime_browser_ready_missing')
    if expected == 'host' and not result['host_rows']:
        result['issues'].append('expected_host_rows_missing')
    if expected == 'activity' and not result['activity_rows']:
        result['issues'].append('expected_activity_rows_missing')
    if expected in ('video', 'ime-video'):
        if not result['video_responses']:
            result['issues'].append('expected_video_response_missing')
        if not result['video_samples']:
            result['issues'].append('expected_video_sample_missing')
    if expected in ('proxy', 'ime-video'):
        if not result['proxy_rows']:
            result['issues'].append('expected_proxy_protocol_missing')
        if any(event['event'] == 'host_start_failed' for event in result['proxy_events']):
            result['issues'].append('proxy_host_initialization_failed')
        if any(event['event'] == 'ime_request_rejected' for event in result['proxy_events']):
            result['issues'].append('proxy_start_rejected')
        if not any(event['event'] in ('armed', 'start_requested') for event in result['proxy_events']):
            result['issues'].append('proxy_attempt_missing')
    if expected == 'ime-video':
        if not result['isolated_ime_variant']:
            result['issues'].append('expected_isolated_ime_variant_missing')
        if not any(event['event'] == 'ime_request' and event.get('editor') == 'borrowed'
                   and event.get('variant') == 'isolated' for event in result['proxy_events']):
            result['issues'].append('expected_borrowed_ime_request_missing')
    if expected == 'focus':
        if not result['focus_trace_installed']:
            result['issues'].append('expected_focus_trace_missing')
        if not result['direct_mode_seen']:
            result['issues'].append('expected_direct_mode_missing')
        if not result['focus_calls'] or not result['focus_frames']:
            result['issues'].append('expected_focus_call_stack_missing')
    if expected == 'window-ime':
        if not result['window_ime_installed']:
            result['issues'].append('expected_window_ime_guard_missing')
        elif not result['window_ime_supported']:
            result['issues'].append('window_ime_provider_unsupported')
        if not result['direct_mode_seen']:
            result['issues'].append('expected_direct_mode_missing')
        if not result['editor_connections']:
            result['issues'].append('expected_editor_connection_missing')
        if not any(row['event'] == 'armed' for row in result['window_ime_events']):
            result['issues'].append('expected_window_ime_request_missing')
    if any(not row['valid'] for row in result['boot_checks']):
        result['issues'].append('boot_expectation_mismatch')
    if any(row['profile_match'] == 'false' for row in result['editor_flags']):
        result['issues'].append('editor_profile_mismatch')
    return result


def editor_version():
    path = ROOT / 'ProjectSettings/ProjectVersion.txt'
    if path.is_file():
        match = re.search(r'^m_EditorVersion: (\S+)', path.read_text(), re.M)
        if match:
            return match[1]
    return '6000.4.5f1'


def adb_command(args):
    executable = args.adb or os.environ.get('VRWEBVIEW_ADB_EXE')
    if not executable:
        version = editor_version()
        roots = (Path('/mnt/c/Program Files/Unity/Hub/Editor'), Path('C:/Program Files/Unity/Hub/Editor'))
        candidates = [root / version / 'Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe' for root in roots]
        executable = next((str(p) for p in candidates if p.is_file()), None) or shutil.which('adb')
    if not executable or not (Path(executable).is_file() or shutil.which(executable)):
        raise ProbeError('ADB not found; set VRWEBVIEW_ADB_EXE or use --adb PATH')
    command = [executable]
    serial = args.serial or os.environ.get('ANDROID_SERIAL')
    if serial:
        command += ['-s', serial]
    return command


def check_device(command):
    try:
        result = subprocess.run(command + ['get-state'], capture_output=True, text=True, timeout=15)
    except (OSError, subprocess.TimeoutExpired) as error:
        raise ProbeError('ADB connection check failed: ' + str(error)) from error
    if result.returncode or result.stdout.strip() != 'device':
        raise ProbeError('ADB device not ready: ' + (result.stderr.strip() or result.stdout.strip()))


def default_log_dir():
    preferred = Path('/mnt/c/Users/tmiyoshi/Downloads')
    return preferred if preferred.is_dir() else Path.home() / 'Downloads'


def input_method_snapshot(command, path):
    """Retain IMMS history after U2 without another build, replay, or buffer clear."""
    try:
        done = subprocess.run(command + ['shell', 'dumpsys', 'input_method'], capture_output=True, timeout=15)
        data = done.stdout + (b'\n' + done.stderr if done.stderr else b'')
        valid = done.returncode == 0 and bool(done.stdout.strip())
        status = dict(file=path.name, valid=valid, exit_code=done.returncode)
    except (OSError, subprocess.TimeoutExpired) as error:
        data = ('Snapshot unavailable: ' + type(error).__name__ + '\n').encode()
        status = dict(file=path.name, valid=False, error=type(error).__name__)
    path.write_bytes(data or b'No output from dumpsys input_method.\n')
    return status


def capture(args):
    if not re.fullmatch(r'[a-zA-Z0-9_-]{1,64}', args.label):
        raise ProbeError('Capture label must contain 1-64 letters, digits, underscores or hyphens')
    command = adb_command(args)
    check_device(command)  # Fail before creating a misleading empty log.
    folder = Path(args.log_dir or os.environ.get('VRWEBVIEW_LOG_DIR') or default_log_dir())
    folder.mkdir(parents=True, exist_ok=True)
    started = datetime.now().astimezone()
    stamp = started.strftime('%Y%m%d-%H%M%S-%f')
    log = folder / ('VRWebView-' + args.label + '-' + stamp + '.log')
    errors, summary = log.with_suffix('.adb-stderr.txt'), log.with_suffix('.summary.json')
    stopped, code, failure = False, None, None
    print('Capturing selected tags without text filtering:', log, flush=True)
    print('Start the trial in another terminal. Ctrl+C ends capture; no log buffers are cleared.', flush=True)
    try:
        with log.open('xb') as output, errors.open('xb') as stderr:
            buffers = ['-b', 'main', '-b', 'system', '-b', 'events'] if args.expect in ('window-ime', 'runtime', 'browser-ui', 'browser-input-compat', 'unity-input', 'unity-input-order', 'unity-input-values', 'unity-input-compat') else []
            extra_tags = list(SYSTEM_FOCUS_TAGS) if args.expect in ('window-ime', 'runtime', 'browser-ui', 'browser-input-compat', 'unity-input', 'unity-input-order', 'unity-input-values', 'unity-input-compat') else []
            process = subprocess.Popen(command + ['logcat', *buffers, '-v', 'threadtime', '-T', '1', *extra_tags, *TAGS],
                                       stdout=subprocess.PIPE, stderr=stderr, start_new_session=os.name != 'nt')
            try:
                for line in process.stdout:
                    output.write(line)
                    output.flush()
                    sys.stdout.buffer.write(line)
                    sys.stdout.buffer.flush()
            except KeyboardInterrupt:
                stopped = True
            finally:
                if process.poll() is None:
                    process.terminate()
                try:
                    code = process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()
                    code = process.wait(timeout=5)
                process.stdout.close()
    except OSError as error:
        failure = str(error)
    if log.exists():
        with log.open(encoding='utf-8', errors='replace') as lines:
            report = analyze(lines, args.expect)
    else:
        report = analyze([], args.expect)
    report.update(log=log.name, adb_stderr=errors.name, capture_label=args.label,
                  label_is_not_a_verified_boot_setting=True, interrupted=stopped, adb_exit_code=code,
                  captured_at=started.isoformat())
    if args.expect in ('unity-input-order', 'unity-input-values', 'unity-input-compat', 'browser-input-compat'):
        report['input_method_snapshot'] = input_method_snapshot(command, log.with_suffix('.input-method.txt'))
        if not report['input_method_snapshot']['valid']:
            report['issues'].append('input_method_snapshot_failed')
    if failure or (code not in (0, None) and not stopped):
        report['issues'].append('adb_capture_failed')
    if failure:
        report['capture_error'] = failure
    summary.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print('\nSaved log:', log)
    print('Saved summary:', summary)
    print('Rows:', report['rows'], 'host:', report['host_rows'], 'activity:', report['activity_rows'])
    if report['issues']:
        raise ProbeError('Capture needs inspection: ' + ', '.join(report['issues']) + '; files retained, ADB errors: ' + str(errors))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--adb', help='ADB executable; also VRWEBVIEW_ADB_EXE')
    parser.add_argument('--serial', help='Device ID; also ANDROID_SERIAL')
    commands = parser.add_subparsers(dest='command', required=True)
    commands.add_parser('doctor', help='Read-only ADB connection and Unity version check')
    cap = commands.add_parser('capture', help='Capture all probe families with the same tag list')
    cap.add_argument('--label', default='probe')
    cap.add_argument('--log-dir')
    cap.add_argument('--expect', choices=('any', 'host', 'activity', 'proxy', 'video', 'ime-video', 'focus', 'window-ime', 'runtime', 'browser-ui', 'browser-input-compat', 'unity-input', 'unity-input-order', 'unity-input-values', 'unity-input-compat'), default='any')
    summary = commands.add_parser('summarize', help='Inspect an existing log offline; no device is needed')
    summary.add_argument('log', type=Path)
    summary.add_argument('--expect', choices=('any', 'host', 'activity', 'proxy', 'video', 'ime-video', 'focus', 'window-ime', 'runtime', 'browser-ui', 'browser-input-compat', 'unity-input', 'unity-input-order', 'unity-input-values', 'unity-input-compat'), default='any')
    import probe_launch
    host = commands.add_parser('launch-host', help='Launch an installed HC2 probe; no build/install/capture')
    host.add_argument('case', nargs='?', choices=probe_launch.HOST_CASES, default='menu')
    host.add_argument('boot', nargs='?', choices=('off', 'on'))
    activity = commands.add_parser('launch-activity', help='Launch an installed Activity GPU probe')
    activity.add_argument('mode', nargs='?', choices=probe_launch.ACTIVITY_MODES, default='texture')
    for launcher in (host, activity):
        launcher.add_argument('--package', default=os.environ.get('VRWEBVIEW_PACKAGE', probe_launch.DEFAULT_PACKAGE))
    for name in ('editor-doctor', 'editor-check'):
        editor = commands.add_parser(name, help='Check installed Unity or run package tests in the Editor without HMD')
        editor.add_argument('--editor', help='Unity executable; also VRWEBVIEW_UNITY_EDITOR')
        editor.add_argument('--test-platform', choices=('EditMode', 'PlayMode'), default='EditMode')
        if name == 'editor-check':
            editor.add_argument('--log-dir')
            editor.add_argument('--timeout', type=int, default=1200)
            editor.add_argument('--shutdown-timeout', type=float, default=60, help='Maximum process-exit wait after test XML is saved')
    args = parser.parse_args()
    try:
        if args.command in ('launch-host', 'launch-activity'):
            request = probe_launch.LaunchRequest('host' if args.command == 'launch-host' else 'activity',
                args.case if args.command == 'launch-host' else args.mode, args.package, getattr(args, 'boot', None))
            command = adb_command(args)
            check_device(command)
            print(json.dumps(probe_launch.launch(command, request), indent=2))
            return 0
        if args.command in ('editor-doctor', 'editor-check'):
            import unity_validation
            if args.command == 'editor-doctor':
                report = unity_validation.doctor(ROOT, editor_version(), args.editor, args.test_platform)
                print(json.dumps(report, indent=2))
                return 0 if report['ready'] else 2
            try:
                report = unity_validation.run(ROOT, editor_version(), args.editor,
                    args.log_dir or os.environ.get('VRWEBVIEW_LOG_DIR') or default_log_dir(), args.timeout, args.test_platform, args.shutdown_timeout)
            except unity_validation.UnityValidationError as error:
                raise ProbeError(str(error)) from error
            print(json.dumps(report, indent=2))
            return 0
        if args.command == 'doctor':
            command = adb_command(args)
            check_device(command)
            print(json.dumps(dict(adb=command, unity_editor=editor_version(), device='ready'), indent=2))
        elif args.command == 'capture':
            capture(args)
        else:
            with args.log.open(encoding='utf-8', errors='replace') as lines:
                result = analyze(lines, args.expect)
            print(json.dumps(result, indent=2))
            if result['issues']:
                return 2
        return 0
    except (ProbeError, probe_launch.LaunchError, OSError) as error:
        print('ERROR:', error, file=sys.stderr)
        return 2


if __name__ == '__main__':
    raise SystemExit(main())
