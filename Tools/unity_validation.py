"""Run package tests in the Editor; no custom entry point or Player build."""
from datetime import datetime
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

TEST_ASSEMBLY = 'T34400.VRWebView.EditMode.Tests'
PLAYMODE_TEST_ASSEMBLY = 'T34400.VRWebView.PlayMode.Tests'
TEST_NAMESPACE = 'T34400.VRWebView.Tests.'
TEST_FILTER = r'^T34400\.VRWebView\.Tests\.'


class UnityValidationError(Exception):
    pass


def doctor(project, version, editor=None, test_platform='EditMode'):
    if test_platform not in ('EditMode', 'PlayMode'):
        raise UnityValidationError('Only Editor EditMode/PlayMode tests are supported')
    executable = editor or os.environ.get('VRWEBVIEW_UNITY_EDITOR')
    if not executable:
        roots = (Path('/mnt/c/Program Files/Unity/Hub/Editor'), Path('C:/Program Files/Unity/Hub/Editor'))
        candidates = [root / version / 'Editor/Unity.exe' for root in roots]
        executable = next((str(p) for p in candidates if p.is_file()), None) or shutil.which('Unity')
    available = bool(executable and Path(executable).is_file())
    manifest = json.loads((project / 'Packages/manifest.json').read_text())
    testable = 'com.t34400.vrwebview' in manifest.get('testables', [])
    tests_present = (project / 'Packages/com.t34400.vrwebview/Tests' / test_platform).is_dir()
    return dict(ready=available and testable and tests_present, editor=executable,
                expected_editor_version=version, editor_available=available,
                package_testable=testable, package_tests_present=tests_present,
                requested_build_target='Android', test_platform=test_platform,
                test_assembly=TEST_ASSEMBLY if test_platform == 'EditMode' else PLAYMODE_TEST_ASSEMBLY,
                scope='Editor import/compilation and package ' + test_platform + ' tests; no Player build or HMD tests')


def unity_path(path, executable):
    """Windows Unity invoked from WSL requires Windows paths for every file argument."""
    if sys.platform.startswith('linux') and str(executable).lower().endswith('.exe'):
        try:
            return subprocess.check_output(['wslpath', '-w', str(path)], text=True).strip()
        except (OSError, subprocess.CalledProcessError) as error:
            raise UnityValidationError('Cannot translate a path for Windows Unity; wslpath is required') from error
    return str(path)


def inspect_results(path):
    """Keep test evidence separate from process completion, including on timeout."""
    try:
        root = ET.parse(path).getroot()
    except (OSError, ET.ParseError) as error:
        raise UnityValidationError('Unity test XML is missing or invalid: ' + str(error)) from error
    if root.tag.rsplit('}', 1)[-1] != 'test-run':
        raise UnityValidationError('Expected an NUnit test-run report')
    cases = [node for node in root.iter() if node.tag.rsplit('}', 1)[-1] == 'test-case']
    passed = sum(node.get('result') == 'Passed' for node in cases)
    failures = []
    for node in cases:
        if node.get('result') == 'Passed':
            continue
        failures.append(dict(name=node.get('fullname', ''), result=node.get('result', 'Unknown'),
                             message=(node.findtext('failure/message') or '')[:8000],
                             stack_trace=(node.findtext('failure/stack-trace') or '')[:8000]))
    errors = []
    if not cases:
        errors.append('Unity reported zero test cases; compilation/test coverage is not established')
    if any(not node.get('fullname', '').startswith(TEST_NAMESPACE) for node in cases):
        errors.append('The XML contains tests outside the requested package namespace')
    if root.get('result') != 'Passed' or passed != len(cases):
        names = ', '.join(item['name'] for item in failures[:10])
        errors.append('Unity package tests did not all pass' + (': ' + names if names else ''))
    try:
        if int(root.get('total', '')) != len(cases):
            errors.append('Unity XML total does not match the recorded test cases')
    except ValueError:
        errors.append('Unity XML total is missing or invalid')
    return dict(result=root.get('result'), total=len(cases), passed=passed,
                failed=sum(node.get('result', '').startswith('Failed') for node in cases),
                other=len(cases) - passed - sum(node.get('result', '').startswith('Failed') for node in cases),
                failures=failures, validation_errors=errors,
                started_at=root.get('start-time'), finished_at=root.get('end-time'))


def read_results(path):
    evidence = inspect_results(path)
    if evidence['validation_errors']:
        raise UnityValidationError('; '.join(evidence['validation_errors']))
    return dict(total=evidence['total'], passed=evidence['passed'], failed=0)


def save_summary(output, report, started):
    report['elapsed_seconds'] = round(time.monotonic() - started, 3)
    temporary = output / 'summary.json.tmp'
    temporary.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    temporary.replace(output / 'summary.json')


def collect_results(report, xml, announce=False):
    try:
        evidence = inspect_results(xml)
    except UnityValidationError:
        return False  # XML may still be in the middle of being written.
    report['test_report'] = evidence
    report['tests'] = {key: evidence[key] for key in ('total', 'passed', 'failed', 'other')}
    if announce:
        print('Unity test report: {total} total, {passed} passed, {failed} failed, {other} other.'.format(**evidence), flush=True)
        for item in evidence['failures'][:10]:
            print('  ' + item['result'] + ': ' + item['name'], flush=True)
    return True


def collect_log(report, log, version):
    errors = []
    try:
        text = log.read_text(encoding='utf-8', errors='replace')
    except OSError:
        text = ''
    if not text:
        errors.append('Editor log is missing or empty')
    observed = re.search(r'(?:Initialize engine version|Unity Editor version):\s*(\S+)', text, re.I)
    report['observed_editor_version'] = observed[1] if observed else None
    if observed and observed[1] != version:
        errors.append('Observed Editor version differs from the project version')
    return errors


def wait_for_editor(process, report, output, xml, log, started, timeout, shutdown_timeout):
    result_time = None
    next_progress = started + 30
    while True:
        now = time.monotonic()
        if result_time is None and collect_results(report, xml, announce=True):
            result_time = now
            report['phase'] = 'waiting_for_editor_exit'
            print('Test XML saved; waiting at most ' + str(shutdown_timeout) + ' seconds for Editor process exit.', flush=True)
            save_summary(output, report, started)
        code = process.poll()
        if code is not None:
            report['exit_code'] = code
            report['process_exit_observed'] = True
            return
        if result_time is not None and now - result_time >= shutdown_timeout:
            report['failure_kind'] = 'editor_exit_timeout'
            raise UnityValidationError('Test XML was saved, but Editor process exit was not observed within '
                                       + str(shutdown_timeout) + ' seconds; see test_report for the actual test results')
        if now - started >= timeout:
            report['failure_kind'] = 'overall_timeout'
            raise UnityValidationError('Editor check exceeded overall timeout of ' + str(timeout) + ' seconds')
        if now >= next_progress:
            try:
                size = log.stat().st_size
            except OSError:
                size = 0
            print('Editor check: ' + str(int(now - started)) + 's elapsed; phase=' + report['phase']
                  + '; Editor.log=' + str(size) + ' bytes; process still running.', flush=True)
            save_summary(output, report, started)
            next_progress = now + 30
        remaining = timeout - (now - started)
        if result_time is not None:
            remaining = min(remaining, shutdown_timeout - (now - result_time))
        try:
            process.wait(timeout=max(.01, min(.25, remaining)))
        except subprocess.TimeoutExpired:
            pass


def stop_launched_process(process, report, executable):
    """Stop only our child, never an unrelated Unity, Hub, Java or adb process."""
    if process is None or process.poll() is not None:
        return
    cleanup = dict(child_pid=process.pid, kill_requested=False, child_exit_observed=False)
    report['process_cleanup'] = cleanup
    try:
        process.kill()
        cleanup['kill_requested'] = True
        cleanup['child_exit_code'] = process.wait(timeout=5)
        cleanup['child_exit_observed'] = True
    except (OSError, subprocess.TimeoutExpired) as error:
        cleanup['error'] = str(error)
    if sys.platform.startswith('linux') and str(executable).lower().endswith('.exe'):
        cleanup['windows_editor_exit_verified'] = False
        print('WSL child stopped/requested; Windows Unity exit is NOT verified. Check that this batch Editor has closed before reopening the project.', flush=True)


def run(project, version, editor, output_dir, timeout, test_platform='EditMode', shutdown_timeout=60):
    setup = doctor(project, version, editor, test_platform)
    if not setup['ready']:
        raise UnityValidationError('Editor check not run: ' + json.dumps(setup))
    if timeout <= 0 or shutdown_timeout <= 0:
        raise UnityValidationError('Timeouts must be positive')
    started_at = datetime.now().astimezone()
    started = time.monotonic()
    output_dir = Path(output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    output = output_dir / ('VRWebView-UnityCheck-' + started_at.strftime('%Y%m%d-%H%M%S-%f'))
    output.mkdir(exist_ok=False)  # A stale XML from a previous run must never establish success.
    xml = output / (test_platform.lower() + '-results.xml')
    log = output / 'Editor.log'
    executable = setup['editor']
    report = dict(setup, started_at=started_at.isoformat(), status='running', phase='launching', command=None,
                  test_results=xml.name, editor_log=log.name, exit_code=None, process_exit_observed=False,
                  timeout_seconds=timeout, shutdown_timeout_seconds=shutdown_timeout)
    error = None
    process = None
    print('Running Editor package tests. Reports:', output, flush=True)
    save_summary(output, report, started)
    try:
        command = [executable, '-batchmode', '-nographics', '-runTests',
                   '-projectPath', unity_path(project, executable), '-buildTarget', 'Android',
                   '-testPlatform', test_platform, '-assemblyNames', setup['test_assembly'],
                   '-testFilter', TEST_FILTER, '-testResults', unity_path(xml, executable),
                   '-logFile', unity_path(log, executable)]
        report['command'] = command
        process = subprocess.Popen(command)  # Exactly one launch; no retries.
        report['child_pid'] = process.pid
        report['phase'] = 'running_tests_or_importing'
        save_summary(output, report, started)
        wait_for_editor(process, report, output, xml, log, started, timeout, shutdown_timeout)
        # Preserve XML even when process exit is nonzero. A timeout must not hide test failures.
        collect_results(report, xml)
        errors = []
        if report['exit_code']:
            errors.append('Unity exited with code ' + str(report['exit_code']))
        try:
            read_results(xml)
        except UnityValidationError as failure:
            errors.append(str(failure))
        errors.extend(collect_log(report, log, version))
        if errors:
            raise UnityValidationError('; '.join(errors))
        report['status'] = 'passed'
    except (OSError, ValueError, UnityValidationError) as failure:
        error = str(failure)
    except KeyboardInterrupt:
        error = 'Editor check interrupted; no successful result claimed'
        report['failure_kind'] = 'interrupted'
    finally:
        if error:
            report['status'] = 'failed'
            report['error'] = error
            stop_launched_process(process, report, executable)
        collect_results(report, xml)
        collect_log(report, log, version)
        report['phase'] = 'finished'
        report['finished_at'] = datetime.now().astimezone().isoformat()
        save_summary(output, report, started)
        print('Saved Editor summary:', output / 'summary.json', flush=True)
    if error:
        raise UnityValidationError(error)
    return report
