"""Editor launcher contract tests; the fake executable does not compile Unity code."""
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

TOOLS = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('unity_validation', TOOLS / 'unity_validation.py')
unity = importlib.util.module_from_spec(spec)
spec.loader.exec_module(unity)
XML = '<test-run result="Passed" total="1"><test-case fullname="T34400.VRWebView.Tests.PointerTests.Sample" result="Passed"/></test-run>'


class ResultTests(unittest.TestCase):
    def test_zero_test_cases_are_not_compilation_success(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / 'result.xml'
            path.write_text('<test-run result="Passed" total="0"/>')
            with self.assertRaises(unity.UnityValidationError): unity.read_results(path)

    def test_failed_child_report_retains_failure_and_location(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / 'result.xml'
            path.write_text('<test-run result="Failed(Child)" total="2">'
                '<test-case fullname="T34400.VRWebView.Tests.PointerTests.Success" result="Passed"/>'
                '<test-case fullname="T34400.VRWebView.Tests.PointerTests.Cancel" result="Failed">'
                '<failure><message>Expected: 1; But was: 0</message>'
                '<stack-trace>PointerTests.cs:30</stack-trace></failure></test-case></test-run>')
            report = unity.inspect_results(path)
            self.assertEqual((report['total'], report['passed'], report['failed']), (2, 1, 1))
            self.assertEqual(report['failures'][0]['stack_trace'], 'PointerTests.cs:30')
            self.assertIn('Expected: 1', report['failures'][0]['message'])
            with self.assertRaisesRegex(unity.UnityValidationError, 'PointerTests.Cancel'):
                unity.read_results(path)

    def test_namespace_filter_matches_the_existing_unity_test_class(self):
        import re
        self.assertIsNotNone(re.search(unity.TEST_FILTER, 'T34400.VRWebView.Tests.PointerTests.ForeignPointerDoesNotReleaseOwner'))
        self.assertIsNone(re.search(unity.TEST_FILTER, 'Other.T34400.VRWebView.Tests.PointerTests.Sample'))

    def test_wsl_translates_file_arguments_with_spaces(self):
        with mock.patch.object(unity.sys, 'platform', 'linux'), mock.patch.object(unity.subprocess, 'check_output', return_value='C:\\My Project\\results.xml\n') as translate:
            actual = unity.unity_path(Path('/mnt/c/My Project/results.xml'), '/mnt/c/Unity/Editor/Unity.exe')
            self.assertEqual(actual, 'C:\\My Project\\results.xml')
            translate.assert_called_once_with(['wslpath', '-w', '/mnt/c/My Project/results.xml'], text=True)


@unittest.skipIf(os.name == 'nt', 'Executable fixture uses a POSIX shebang')
class LauncherTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='vrwebview editor test ')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.editor = self.root / 'fake-unity'
        self.output = self.root / 'reports'
        self.editor.write_text('''#!/usr/bin/env python3
from pathlib import Path
import os,sys,time
args=sys.argv
with Path(__file__).with_suffix('.launches').open('a') as launches: launches.write('launch\\n')
assert '-runTests' in args and '-batchmode' in args
assert '-quit' not in args
platform=os.environ.get('VRWEBVIEW_EDITOR_TEST_PLATFORM','EditMode')
assert args[args.index('-testPlatform')+1]==platform
assert ('-nographics' in args)==(platform=='EditMode')
assert ('-vrwebviewEditorTestsWithoutXR' in args)==(platform=='PlayMode')
assert args[args.index('-buildTarget')+1]=='Android'
assert args[args.index('-assemblyNames')+1]=='T34400.VRWebView.'+platform+'.Tests'
mode=os.environ.get('VRWEBVIEW_EDITOR_TEST_MODE','passed')
if mode=='timeout': time.sleep(10)
xml=Path(args[args.index('-testResults')+1]);log=Path(args[args.index('-logFile')+1])
if mode!='no-log': log.write_text('Initialize engine version: '+('6000.3.0f1' if mode=='wrong-version' else '6000.4.5f1')+' (fixture)\\n')
if platform=='PlayMode' and mode not in ('no-isolation','native-crash','no-log'):
 with log.open('a') as stream: stream.write('VRWEBVIEW_EDITOR_TESTS: XR auto-start isolation verified before scene load\\n')
if mode=='native-crash':
 with log.open('a') as stream: stream.write('xrCreateSession: XR_ERROR_GRAPHICS_DEVICE_INVALID\\nNative Crash Reporting\\n')
 sys.exit(5)
if mode!='no-xml':
 result='Skipped' if mode=='skipped' else 'Failed' if mode in ('failed','failed-exit','failed-hang') else 'Passed'
 name='Other.Tests.Sample' if mode=='foreign' else 'T34400.VRWebView.Tests.PointerTests.Sample'
 case='' if mode=='zero' else '<test-case fullname="'+name+'" result="'+result+'"/>'
 root_result='Failed' if mode=='root-failed' else 'Passed'
 total='0' if mode=='zero' else '4' if mode=='wrong-total' else '1'
 xml.write_text('<test-run result="'+root_result+'" total="'+total+'">'+case+'</test-run>')
if mode in ('passed-hang','failed-hang'): time.sleep(10)
if mode=='failed-exit': sys.exit(2)
if mode=='exit-error': sys.exit(17)
''')
        self.editor.chmod(0o755)

    def run_check(self, mode='passed', timeout=5, test_platform='EditMode', shutdown_timeout=60):
        env = dict(os.environ, VRWEBVIEW_EDITOR_TEST_MODE=mode, VRWEBVIEW_EDITOR_TEST_PLATFORM=test_platform)
        command = [sys.executable, str(TOOLS / 'probe.py'), 'editor-check', '--editor', str(self.editor),
                   '--log-dir', str(self.output), '--timeout', str(timeout), '--test-platform', test_platform,
                   '--shutdown-timeout', str(shutdown_timeout)]
        return subprocess.run(command, env=env, capture_output=True, text=True, timeout=15)

    def summary(self):
        files = list(self.output.glob('*/summary.json'))
        self.assertEqual(len(files), 1)
        return json.loads(files[0].read_text())

    def test_success_requires_exit_xml_scope_and_log(self):
        result = self.run_check()
        self.assertEqual(result.returncode, 0, result.stderr)
        summary = self.summary()
        self.assertEqual(summary['status'], 'passed')
        self.assertEqual(summary['tests']['passed'], 1)
        self.assertEqual(summary['observed_editor_version'], '6000.4.5f1')

    def test_playmode_uses_its_own_assembly_platform_and_result_file(self):
        result = self.run_check(test_platform='PlayMode')
        self.assertEqual(result.returncode, 0, result.stderr)
        summary = self.summary()
        self.assertEqual(summary['test_platform'], 'PlayMode')
        self.assertEqual(summary['test_assembly'], unity.PLAYMODE_TEST_ASSEMBLY)
        self.assertEqual(summary['test_results'], 'playmode-results.xml')
        command = summary['command']
        self.assertEqual(command[command.index('-testPlatform') + 1], 'PlayMode')
        self.assertEqual(command[command.index('-assemblyNames') + 1], unity.PLAYMODE_TEST_ASSEMBLY)
        self.assertNotIn('-buildPlayerPath', command)
        self.assertNotIn('-nographics', command)
        self.assertIn(unity.XR_ISOLATION_ARGUMENT, command)
        self.assertTrue(summary['xr_isolation_verified'])
        self.assertEqual(summary['graphics_mode'], 'enabled')

    def test_editmode_remains_headless_without_the_playmode_override(self):
        self.assertEqual(self.run_check().returncode, 0)
        summary = self.summary()
        self.assertIn('-nographics', summary['command'])
        self.assertNotIn(unity.XR_ISOLATION_ARGUMENT, summary['command'])
        self.assertFalse(summary['xr_isolation_requested'])
        self.assertEqual(summary['graphics_mode'], 'null')

    def test_passing_playmode_xml_without_isolation_evidence_is_failure(self):
        result = self.run_check('no-isolation', test_platform='PlayMode')
        self.assertEqual(result.returncode, 2)
        summary = self.summary()
        self.assertFalse(summary['xr_isolation_verified'])
        self.assertEqual(summary['tests']['passed'], 1)
        self.assertEqual(summary['exit_code'], 0)
        self.assertIn('PlayMode XR isolation was not verified', summary['error'])

    def test_native_crash_and_missing_xml_retain_the_underlying_diagnostic(self):
        result = self.run_check('native-crash', test_platform='PlayMode')
        self.assertEqual(result.returncode, 2)
        summary = self.summary()
        self.assertEqual(summary['exit_code'], 5)
        self.assertTrue(summary['native_crash_observed'])
        self.assertTrue(summary['xr_graphics_device_invalid_observed'])
        self.assertIn('Editor native crash', summary['error'])
        self.assertIn('XML is missing', summary['error'])

    def test_invalid_test_platform_cannot_start_an_editor(self):
        result = subprocess.run([sys.executable, str(TOOLS / 'probe.py'), 'editor-check', '--test-platform', 'Android'], capture_output=True, text=True)
        self.assertEqual(result.returncode, 2)
        self.assertFalse(self.output.exists())

    def test_stale_xml_does_not_rescue_a_missing_current_report(self):
        self.output.mkdir()
        (self.output / 'editmode-results.xml').write_text(XML)
        result = self.run_check('no-xml')
        self.assertEqual(result.returncode, 2)
        self.assertEqual(self.summary()['status'], 'failed')

    def test_zero_tests_fail_even_when_unity_exit_is_zero(self):
        self.assertEqual(self.run_check('zero').returncode, 2)
        self.assertEqual(self.summary()['status'], 'failed')

    def test_skipped_tests_fail(self):
        self.assertEqual(self.run_check('skipped').returncode, 2)

    def test_failed_tests_fail_even_with_success_exit_code(self):
        self.assertEqual(self.run_check('failed').returncode, 2)

    def test_foreign_tests_cannot_establish_package_success(self):
        self.assertEqual(self.run_check('foreign').returncode, 2)

    def test_failed_root_suite_cannot_establish_success(self):
        self.assertEqual(self.run_check('root-failed').returncode, 2)

    def test_inconsistent_test_total_fails(self):
        self.assertEqual(self.run_check('wrong-total').returncode, 2)

    def test_missing_editor_log_fails(self):
        self.assertEqual(self.run_check('no-log').returncode, 2)

    def test_wrong_observed_editor_version_fails(self):
        self.assertEqual(self.run_check('wrong-version').returncode, 2)

    def test_nonzero_process_exit_overrides_passing_xml(self):
        self.assertEqual(self.run_check('exit-error').returncode, 2)
        self.assertEqual(self.summary()['exit_code'], 17)

    def test_timeout_is_failure_and_retains_summary(self):
        self.assertEqual(self.run_check('timeout', timeout=1).returncode, 2)
        self.assertEqual(self.summary()['status'], 'failed')

    def test_failed_xml_is_preserved_with_nonzero_exit(self):
        result = self.run_check('failed-exit')
        self.assertEqual(result.returncode, 2)
        summary = self.summary()
        self.assertEqual(summary['exit_code'], 2)
        self.assertTrue(summary['process_exit_observed'])
        self.assertEqual(summary['tests']['failed'], 1)
        self.assertEqual(summary['test_report']['failures'][0]['name'], 'T34400.VRWebView.Tests.PointerTests.Sample')
        self.assertIn('PointerTests.Sample', result.stdout)

    def test_failed_xml_is_not_hidden_by_shutdown_timeout(self):
        result = self.run_check('failed-hang', shutdown_timeout=.2)
        self.assertEqual(result.returncode, 2)
        summary = self.summary()
        self.assertEqual(summary['failure_kind'], 'editor_exit_timeout')
        self.assertEqual(summary['tests']['failed'], 1)
        self.assertIsNone(summary['exit_code'])
        self.assertFalse(summary['process_exit_observed'])
        self.assertTrue(summary['process_cleanup']['child_exit_observed'])
        self.assertLess(summary['elapsed_seconds'], 4)
        self.assertIn('1 total, 0 passed, 1 failed', result.stdout)
        self.assertEqual(self.editor.with_suffix('.launches').read_text().splitlines(), ['launch'])

    def test_passing_xml_without_exit_is_not_success(self):
        result = self.run_check('passed-hang', shutdown_timeout=.2)
        self.assertEqual(result.returncode, 2)
        summary = self.summary()
        self.assertEqual(summary['status'], 'failed')
        self.assertEqual(summary['failure_kind'], 'editor_exit_timeout')
        self.assertEqual(summary['tests']['passed'], 1)
        self.assertFalse(summary['process_exit_observed'])

    def test_invalid_shutdown_timeout_cannot_launch(self):
        self.assertEqual(self.run_check(shutdown_timeout=0).returncode, 2)
        self.assertFalse(self.editor.with_suffix('.launches').exists())
        self.assertFalse(self.output.exists())

    def test_in_progress_summary_exists_before_test_results(self):
        def inspect_running(*args):
            summary = self.summary()
            self.assertEqual(summary['status'], 'running')
            self.assertEqual(summary['phase'], 'running_tests_or_importing')
            self.assertFalse(summary['process_exit_observed'])
            raise unity.UnityValidationError('synthetic wait failure')
        with mock.patch.object(unity, 'wait_for_editor', side_effect=inspect_running):
            with self.assertRaises(unity.UnityValidationError):
                unity.run(TOOLS.parent, '6000.4.5f1', str(self.editor), self.output, 5)
        self.assertEqual(self.summary()['status'], 'failed')
        self.assertTrue(self.summary()['process_cleanup']['kill_requested'])

    def test_heartbeat_reports_waiting_phase_without_relaunch(self):
        from io import StringIO
        process = mock.Mock()
        process.poll.return_value = None
        process.wait.side_effect = subprocess.TimeoutExpired('fixture', .25)
        report = {'phase': 'running_tests_or_importing'}
        with mock.patch.object(unity.time, 'monotonic', side_effect=[31, 61]), \
             mock.patch.object(unity, 'collect_results', return_value=False), \
             mock.patch.object(unity, 'save_summary') as save, \
             mock.patch('sys.stdout', new_callable=StringIO) as output:
            with self.assertRaises(unity.UnityValidationError):
                unity.wait_for_editor(process, report, self.output, self.root / 'x.xml', self.root / 'Editor.log', 0, 60, 10)
        self.assertIn('31s elapsed; phase=running_tests_or_importing', output.getvalue())
        save.assert_called_once()
        self.assertEqual(report['failure_kind'], 'overall_timeout')

    def test_path_translation_failure_still_writes_a_failure_summary(self):
        with mock.patch.object(unity, 'unity_path', side_effect=unity.UnityValidationError('synthetic path conversion failure')):
            with self.assertRaises(unity.UnityValidationError):
                unity.run(TOOLS.parent, '6000.4.5f1', str(self.editor), self.output, 5)
        self.assertEqual(self.summary()['status'], 'failed')
        self.assertIn('path conversion failure', self.summary()['error'])

    def test_missing_editor_is_not_ready(self):
        result = subprocess.run([sys.executable, str(TOOLS / 'probe.py'), 'editor-doctor', '--editor', str(self.root / 'missing')],
                                capture_output=True, text=True)
        self.assertEqual(result.returncode, 2)
        self.assertFalse(json.loads(result.stdout)['ready'])
        self.assertFalse(self.output.exists())


if __name__ == '__main__':
    unittest.main()
