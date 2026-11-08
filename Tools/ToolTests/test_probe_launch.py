"""Launch contract tests use a synthetic ADB; no connected device or APK is used."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
import probe_launch as launch


class RequestTests(unittest.TestCase):
    def test_invalid_requests_are_rejected_before_adb(self):
        for arguments in [('other', 'native'), ('host', 'unknown'), ('activity', 'keyboard'),
                          ('host', 'menu', ''), ('host', 'menu', 'bad;pkg'),
                          ('host', 'menu', 'good.pkg', 'maybe'), ('activity', 'ime', 'good.pkg', 'on')]:
            with self.subTest(arguments=arguments), self.assertRaises(launch.LaunchError):
                launch.LaunchRequest(*arguments)

    def test_timeout_is_not_a_successful_launch(self):
        with mock.patch.object(launch.subprocess, 'run', side_effect=subprocess.TimeoutExpired('adb', 30)):
            with self.assertRaisesRegex(launch.LaunchError, 'resolve launcher failed'):
                launch.launch(['fake-adb'], launch.LaunchRequest('host', 'menu'))

    def test_missing_adb_is_not_a_successful_launch(self):
        with mock.patch.object(launch.subprocess, 'run', side_effect=OSError('synthetic launch failure')):
            with self.assertRaisesRegex(launch.LaunchError, 'resolve launcher failed'):
                launch.launch(['fake-adb'], launch.LaunchRequest('host', 'menu'))


@unittest.skipIf(os.name == 'nt', 'Executable fake ADB fixture uses a POSIX shebang')
class LauncherTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='vrwebview launch test ')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.adb = self.root / 'fake adb'
        self.calls = self.root / 'calls.jsonl'
        self.adb.write_text('''#!/usr/bin/env python3
import json,os,sys
from pathlib import Path
args=sys.argv[1:]
with Path(os.environ['VRWEBVIEW_TEST_CALLS']).open('a') as f:f.write(json.dumps(args)+'\\n')
if args[:1]==['-s']: args=args[2:]
mode=os.environ.get('VRWEBVIEW_TEST_MODE','normal')
if args==['get-state']:
 print('offline' if mode=='offline' else 'device');sys.exit(0)
if args[:3]==['shell','cmd','package']:
 if mode=='resolve-failure':print('Error: synthetic resolve failure');sys.exit(0)
 if mode=='resolve-exit':print('synthetic resolve failure',file=sys.stderr);sys.exit(1)
 package=args[-1]
 component=os.environ.get('VRWEBVIEW_TEST_COMPONENT',package+'/com.unity3d.player.UnityPlayerGameActivity')
 sys.stdout.write('priority=0 preferredOrder=0 match=0x108000\\r\\n'+component+'\\r\\n');sys.exit(0)
if args[:3]==['shell','am','force-stop']:
 if mode=='stop-failure':print('SecurityException: synthetic denial',file=sys.stderr);sys.exit(0)
 sys.exit(0)
if args[:3]==['shell','am','start']:
 if mode=='start-failure':print('Error type 3\\nError: synthetic missing activity');sys.exit(0)
 if mode=='start-exit':print('synthetic start failure',file=sys.stderr);sys.exit(1)
 if mode=='start-warning':print('Warning: Activity not started, intent delivered to existing instance');sys.exit(0)
 print('Starting: Intent { synthetic probe launch }');sys.exit(0)
print('Unexpected ADB operation',file=sys.stderr);sys.exit(9)
''')
        self.adb.chmod(0o755)
        self.env = dict(os.environ, VRWEBVIEW_ADB_EXE=str(self.adb), VRWEBVIEW_TEST_CALLS=str(self.calls))
        for key in ['ANDROID_SERIAL', 'VRWEBVIEW_PACKAGE', 'VRWEBVIEW_TEST_MODE', 'VRWEBVIEW_TEST_COMPONENT']:
            self.env.pop(key, None)

    def run_tool(self, *args, mode='normal', env=None, wrapper=None):
        values = dict(self.env, VRWEBVIEW_TEST_MODE=mode, **(env or {}))
        if wrapper:
            command = ['bash', str(TOOLS / wrapper), *args]
        else:
            command = [sys.executable, str(TOOLS / 'probe.py'), *args]
        return subprocess.run(command, env=values, capture_output=True, text=True, cwd=self.root)

    def recorded(self):
        return [json.loads(line) for line in self.calls.read_text().splitlines()] if self.calls.exists() else []

    def assert_launch(self, result, selection, key, boot=None, package=launch.DEFAULT_PACKAGE):
        self.assertEqual(result.returncode, 0, result.stderr)
        report = json.loads(result.stdout)
        self.assertEqual(report['status'], 'launch_requested')
        self.assertEqual(report['runtime_outcome'], 'not_observed')
        self.assertEqual(report['selection'], selection)
        self.assertEqual(report['expected_boot'], boot)
        calls = self.recorded()
        self.assertEqual(len(calls), 4)
        self.assertEqual(calls[0], ['get-state'])
        self.assertEqual(calls[1][-2:], ['-p', package])
        self.assertEqual(calls[2], ['shell', 'am', 'force-stop', package])
        extras = ['--es', key, selection]
        if boot is not None: extras += ['--es', 'vrwebview_boot_expect', '0' if boot == 'off' else '1']
        self.assertEqual(calls[3], ['shell', 'am', 'start', '-n', report['component'], *extras])
        self.calls.unlink()

    def test_host_defaults_preserve_menu_without_boot_extra(self):
        self.assert_launch(self.run_tool('launch-host'), 'menu', 'vrwebview_host_case')

    def test_all_host_cases_and_boot_extras_match_the_old_entry_point(self):
        for case in launch.HOST_CASES:
            for boot in (None, 'off', 'on'):
                with self.subTest(case=case, boot=boot):
                    args = ['launch-host', case] + ([boot] if boot else [])
                    self.assert_launch(self.run_tool(*args), case, 'vrwebview_host_case', boot)

    def test_all_activity_modes_and_default_match_the_old_entry_point(self):
        self.assert_launch(self.run_tool('launch-activity'), 'texture', 'vrwebview_activity_gpu_mode')
        for mode in launch.ACTIVITY_MODES:
            with self.subTest(mode=mode):
                self.assert_launch(self.run_tool('launch-activity', mode), mode, 'vrwebview_activity_gpu_mode')

    def test_both_legacy_wrappers_work_outside_the_project_directory(self):
        self.assert_launch(self.run_tool('native-webinfo', 'off', wrapper='run_host_probe.sh'),
                           'native-webinfo', 'vrwebview_host_case', 'off')
        self.assert_launch(self.run_tool('passthrough', wrapper='run_activity_gpu_probe.sh'),
                           'passthrough', 'vrwebview_activity_gpu_mode')

    def test_package_environment_and_explicit_override_are_preserved(self):
        values = {'VRWEBVIEW_PACKAGE': 'example.from_env'}
        self.assert_launch(self.run_tool('launch-host', env=values), 'menu', 'vrwebview_host_case', package='example.from_env')
        self.assert_launch(self.run_tool('launch-host', '--package', 'example.override', env=values),
                           'menu', 'vrwebview_host_case', package='example.override')

    def test_unity_player_activity_and_crlf_resolver_output_are_accepted(self):
        component = launch.DEFAULT_PACKAGE + '/com.unity3d.player.UnityPlayerActivity'
        self.assert_launch(self.run_tool('launch-host', env={'VRWEBVIEW_TEST_COMPONENT': component}), 'menu', 'vrwebview_host_case')

    def test_serial_is_used_for_every_operation(self):
        result = self.run_tool('--serial', 'selected-device', 'launch-host', env={'ANDROID_SERIAL': 'other-device'})
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(len(self.recorded()), 4)
        self.assertTrue(all(call[:2] == ['-s', 'selected-device'] for call in self.recorded()))

    def test_legacy_wrapper_uses_android_serial(self):
        result = self.run_tool(wrapper='run_host_probe.sh', env={'ANDROID_SERIAL': 'selected-device'})
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertTrue(all(call[:2] == ['-s', 'selected-device'] for call in self.recorded()))

    def test_invalid_arguments_make_no_adb_calls(self):
        for args in [('launch-host', 'unknown'), ('launch-host', 'menu', 'off', 'extra'),
                     ('launch-host', '--package', ''), ('launch-host', '--package', 'x;touch /tmp/unwanted'),
                     ('launch-activity', 'texture', 'on')]:
            with self.subTest(args=args):
                self.assertEqual(self.run_tool(*args).returncode, 2)
                self.assertEqual(self.recorded(), [])

    def test_offline_device_does_not_resolve_or_stop_an_app(self):
        result = self.run_tool('launch-host', mode='offline')
        self.assertEqual(result.returncode, 2)
        self.assertEqual(self.recorded(), [['get-state']])

    def test_wrong_or_empty_launcher_never_stops_an_app(self):
        for component in ['', 'other.pkg/com.unity3d.player.UnityPlayerActivity', launch.DEFAULT_PACKAGE + '/custom.Activity']:
            with self.subTest(component=component):
                result = self.run_tool('launch-host', env={'VRWEBVIEW_TEST_COMPONENT': component})
                self.assertEqual(result.returncode, 2)
                self.assertEqual(len(self.recorded()), 2)
                self.calls.unlink()

    def test_resolver_failure_with_zero_or_nonzero_exit_never_stops_an_app(self):
        for mode in ['resolve-failure', 'resolve-exit']:
            with self.subTest(mode=mode):
                result = self.run_tool('launch-host', mode=mode)
                self.assertEqual(result.returncode, 2)
                self.assertEqual(len(self.recorded()), 2)
                self.calls.unlink()

    def test_force_stop_failure_never_launches_an_app(self):
        result = self.run_tool('launch-host', mode='stop-failure')
        self.assertEqual(result.returncode, 2)
        self.assertEqual(len(self.recorded()), 3)
        self.assertIn('force-stop failed', result.stderr)

    def test_start_failure_or_reused_activity_is_not_reported_as_success(self):
        for mode in ['start-failure', 'start-exit', 'start-warning']:
            with self.subTest(mode=mode):
                result = self.run_tool('launch-host', mode=mode)
                self.assertEqual(result.returncode, 2)
                self.assertEqual(len(self.recorded()), 4)
                self.assertNotIn('launch_requested', result.stdout)
                self.calls.unlink()


if __name__ == '__main__':
    unittest.main()
