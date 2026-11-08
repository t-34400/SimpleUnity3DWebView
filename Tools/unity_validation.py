"""Run the existing package EditMode tests; no custom Editor entry point or Player build."""
from datetime import datetime
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

TEST_ASSEMBLY = 'T34400.VRWebView.EditMode.Tests'
TEST_NAMESPACE = 'T34400.VRWebView.Tests.'
TEST_FILTER = r'^T34400\.VRWebView\.Tests\.'


class UnityValidationError(Exception):
    pass


def doctor(project, version, editor=None):
    executable = editor or os.environ.get('VRWEBVIEW_UNITY_EDITOR')
    if not executable:
        roots = (Path('/mnt/c/Program Files/Unity/Hub/Editor'), Path('C:/Program Files/Unity/Hub/Editor'))
        candidates = [root / version / 'Editor/Unity.exe' for root in roots]
        executable = next((str(p) for p in candidates if p.is_file()), None) or shutil.which('Unity')
    available = bool(executable and Path(executable).is_file())
    manifest = json.loads((project / 'Packages/manifest.json').read_text())
    testable = 'com.t34400.vrwebview' in manifest.get('testables', [])
    tests_present = (project / 'Packages/com.t34400.vrwebview/Tests/EditMode').is_dir()
    return dict(ready=available and testable and tests_present, editor=executable,
                expected_editor_version=version, editor_available=available,
                package_testable=testable, package_tests_present=tests_present,
                requested_build_target='Android', test_platform='EditMode', test_assembly=TEST_ASSEMBLY,
                scope='Editor import/compilation and package EditMode tests; no Player build or HMD tests')


def unity_path(path, executable):
    """Windows Unity invoked from WSL requires Windows paths for every file argument."""
    if sys.platform.startswith('linux') and str(executable).lower().endswith('.exe'):
        try:
            return subprocess.check_output(['wslpath', '-w', str(path)], text=True).strip()
        except (OSError, subprocess.CalledProcessError) as error:
            raise UnityValidationError('Cannot translate a path for Windows Unity; wslpath is required') from error
    return str(path)


def read_results(path):
    try:
        root = ET.parse(path).getroot()
    except (OSError, ET.ParseError) as error:
        raise UnityValidationError('Unity test XML is missing or invalid: ' + str(error)) from error
    if root.tag.rsplit('}', 1)[-1] != 'test-run':
        raise UnityValidationError('Expected an NUnit test-run report')
    cases = [node for node in root.iter() if node.tag.rsplit('}', 1)[-1] == 'test-case']
    if not cases:
        raise UnityValidationError('Unity reported zero test cases; compilation/test coverage is not established')
    if any(not node.get('fullname', '').startswith(TEST_NAMESPACE) for node in cases):
        raise UnityValidationError('The XML contains tests outside the requested package namespace')
    results = [node.get('result', 'Unknown') for node in cases]
    if root.get('result') != 'Passed' or any(value != 'Passed' for value in results):
        raise UnityValidationError('Unity package tests did not all pass (failed, skipped or inconclusive cases remain)')
    declared = root.get('total')
    if declared is None or int(declared) != len(cases):
        raise UnityValidationError('Unity XML total does not match the recorded test cases')
    return dict(total=len(cases), passed=len(cases), failed=0)


def run(project, version, editor, output_dir, timeout):
    setup = doctor(project, version, editor)
    if not setup['ready']:
        raise UnityValidationError('Editor check not run: ' + json.dumps(setup))
    if timeout <= 0:
        raise UnityValidationError('Timeout must be positive')
    started = datetime.now().astimezone()
    output_dir = Path(output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    output = output_dir / ('VRWebView-UnityCheck-' + started.strftime('%Y%m%d-%H%M%S-%f'))
    output.mkdir(exist_ok=False)  # A stale XML from a previous run must never establish success.
    xml = output / 'editmode-results.xml'
    log = output / 'Editor.log'
    executable = setup['editor']
    command = [executable, '-batchmode', '-nographics', '-runTests',
               '-projectPath', unity_path(project, executable), '-buildTarget', 'Android',
               '-testPlatform', 'EditMode', '-assemblyNames', TEST_ASSEMBLY,
               '-testFilter', TEST_FILTER, '-testResults', unity_path(xml, executable),
               '-logFile', unity_path(log, executable)]
    report = dict(setup, started_at=started.isoformat(), status='failed', command=command,
                  test_results=xml.name, editor_log=log.name, exit_code=None)
    error = None
    print('Running Editor package tests. Reports:', output, flush=True)
    try:
        completed = subprocess.run(command, timeout=timeout)
        report['exit_code'] = completed.returncode
        if completed.returncode:
            raise UnityValidationError('Unity exited with code ' + str(completed.returncode))
        report['tests'] = read_results(xml)
        if not log.is_file() or not log.stat().st_size:
            raise UnityValidationError('Editor log is missing or empty')
        text = log.read_text(encoding='utf-8', errors='replace')
        observed = re.search(r'(?:Initialize engine version|Unity Editor version):\s*(\S+)', text, re.I)
        report['observed_editor_version'] = observed[1] if observed else None
        if observed and observed[1] != version:
            raise UnityValidationError('Observed Editor version differs from the project version')
        report['status'] = 'passed'
    except (OSError, ValueError, subprocess.TimeoutExpired, UnityValidationError) as failure:
        error = str(failure)
        report['error'] = error
    except KeyboardInterrupt:
        error = 'Editor check interrupted; no successful result claimed'
        report['error'] = error
    finally:
        (output / 'summary.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
        print('Saved Editor summary:', output / 'summary.json', flush=True)
    if error:
        raise UnityValidationError(error)
    return report
