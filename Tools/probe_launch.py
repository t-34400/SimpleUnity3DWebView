"""Launch the existing installed probes; no build, install, capture or IME inference."""
from dataclasses import dataclass
import re
import subprocess

HOST_CASES = ('menu', 'keyboard', 'native', 'native-webinfo', 'webview', 'activity', 'transparent')
ACTIVITY_MODES = ('texture', 'native', 'video', 'ime', 'passthrough', 'head')
DEFAULT_PACKAGE = 'com.DefaultCompany.WebViewGitHub'
LAUNCHERS = ('com.unity3d.player.UnityPlayerGameActivity', 'com.unity3d.player.UnityPlayerActivity')


class LaunchError(Exception):
    pass


@dataclass(frozen=True)
class LaunchRequest:
    family: str
    selection: str
    package: str = DEFAULT_PACKAGE
    boot: str = None

    def __post_init__(self):
        if self.family not in ('host', 'activity'):
            raise LaunchError('Unknown probe family')
        choices = HOST_CASES if self.family == 'host' else ACTIVITY_MODES
        if self.selection not in choices:
            raise LaunchError('Unknown probe case/mode')
        if not re.fullmatch(r'[a-zA-Z0-9._]+', self.package):
            raise LaunchError('Invalid package ID')
        if self.boot not in (None, 'off', 'on') or (self.family != 'host' and self.boot is not None):
            raise LaunchError('Boot expectation applies only to host: off or on')

    @property
    def extras(self):
        key = 'vrwebview_host_case' if self.family == 'host' else 'vrwebview_activity_gpu_mode'
        values = ['--es', key, self.selection]
        if self.boot is not None:
            values += ['--es', 'vrwebview_boot_expect', '0' if self.boot == 'off' else '1']
        return values


def adb_step(command, arguments, phase):
    try:
        completed = subprocess.run([*command, *arguments], capture_output=True, text=True, timeout=30)
    except (OSError, subprocess.TimeoutExpired) as error:
        raise LaunchError('ADB ' + phase + ' failed: ' + str(error)) from error
    output = completed.stdout + '\n' + completed.stderr
    # Android shell commands can print an error while adb itself exits successfully.
    failed = re.search(r'(?im)^\s*(?:Error\b|Failure\b|[\w.]*Exception\b|Permission Denial\b|No activities found\b)'
                       r'|Activity not started|unable to resolve Intent', output)
    if completed.returncode or failed:
        raise LaunchError('ADB ' + phase + ' failed (exit ' + str(completed.returncode) + '): ' + output.strip())
    return completed.stdout


def launch(command, request):
    # Caller uses the same read-only connection check as capture/doctor first.
    output = adb_step(command, ['shell', 'cmd', 'package', 'resolve-activity', '--brief', '-a',
        'android.intent.action.MAIN', '-c', 'android.intent.category.LAUNCHER', '-p', request.package], 'resolve launcher')
    lines = output.splitlines()
    component = lines[-1].strip() if lines else ''
    if component not in tuple(request.package + '/' + name for name in LAUNCHERS):
        raise LaunchError('Unexpected launcher; app was not stopped: ' + component)
    adb_step(command, ['shell', 'am', 'force-stop', request.package], 'force-stop')
    start_output = adb_step(command, ['shell', 'am', 'start', '-n', component, *request.extras], 'start')
    return dict(status='launch_requested', family=request.family, selection=request.selection,
                package=request.package, component=component, expected_boot=request.boot,
                runtime_outcome='not_observed', adb_start_output=start_output.strip())
