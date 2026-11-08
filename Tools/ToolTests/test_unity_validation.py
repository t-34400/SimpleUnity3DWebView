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
assert '-runTests' in args and '-batchmode' in args
assert '-quit' not in args
assert args[args.index('-testPlatform')+1]=='EditMode'
assert args[args.index('-buildTarget')+1]=='Android'
assert args[args.index('-assemblyNames')+1]=='T34400.VRWebView.EditMode.Tests'
mode=os.environ.get('VRWEBVIEW_EDITOR_TEST_MODE','passed')
if mode=='timeout': time.sleep(10)
xml=Path(args[args.index('-testResults')+1]);log=Path(args[args.index('-logFile')+1])
if mode!='no-log': log.write_text('Initialize engine version: '+('6000.3.0f1' if mode=='wrong-version' else '6000.4.5f1')+' (fixture)\\n')
if mode!='no-xml':
 result='Skipped' if mode=='skipped' else 'Failed' if mode=='failed' else 'Passed'
 name='Other.Tests.Sample' if mode=='foreign' else 'T34400.VRWebView.Tests.PointerTests.Sample'
 case='' if mode=='zero' else '<test-case fullname="'+name+'" result="'+result+'"/>'
 root_result='Failed' if mode=='root-failed' else 'Passed'
 total='0' if mode=='zero' else '4' if mode=='wrong-total' else '1'
 xml.write_text('<test-run result="'+root_result+'" total="'+total+'">'+case+'</test-run>')
if mode=='exit-error': sys.exit(17)
''')
        self.editor.chmod(0o755)

    def run_check(self, mode='passed', timeout=5):
        env = dict(os.environ, VRWEBVIEW_EDITOR_TEST_MODE=mode)
        command = [sys.executable, str(TOOLS / 'probe.py'), 'editor-check', '--editor', str(self.editor),
                   '--log-dir', str(self.output), '--timeout', str(timeout)]
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
