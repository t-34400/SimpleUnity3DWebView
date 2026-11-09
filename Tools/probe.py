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
        'InputMethodManager:V', '*:S')
SYSTEM_FOCUS_TAGS = ('WindowManager:I', 'InputDispatcher:I', 'input_focus:I')


class ProbeError(Exception):
    pass


def analyze(lines, expected='any'):
    """Summarize recorded metadata; do not infer HMD visibility or trial names from order."""
    result = dict(rows=0, runtime_rows=0, runtime_ready=False, runtime_connections=0, runtime_hover_enter=0, runtime_hover_exit=0, runtime_failures=0, runtime_window_events=[], host_rows=0, activity_rows=0, focus_rows=0, legacy_rows=0, trials=[],
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
        if 'VRWebViewRuntime' in line:
            result['runtime_rows'] += 1
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
    if not result['rows']:
        result['issues'].append('empty_log')
    if not (result['runtime_rows'] or result['host_rows'] or result['activity_rows'] or result['focus_rows'] or result['legacy_rows']):
        result['issues'].append('no_probe_rows')
    if result['runtime_failures']:
        result['issues'].append('runtime_browser_failed')
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
            buffers = ['-b', 'main', '-b', 'system', '-b', 'events'] if args.expect in ('window-ime', 'runtime') else []
            extra_tags = list(SYSTEM_FOCUS_TAGS) if args.expect in ('window-ime', 'runtime') else []
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
    cap.add_argument('--expect', choices=('any', 'host', 'activity', 'proxy', 'video', 'ime-video', 'focus', 'window-ime', 'runtime'), default='any')
    summary = commands.add_parser('summarize', help='Inspect an existing log offline; no device is needed')
    summary.add_argument('log', type=Path)
    summary.add_argument('--expect', choices=('any', 'host', 'activity', 'proxy', 'video', 'ime-video', 'focus', 'window-ime', 'runtime'), default='any')
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
