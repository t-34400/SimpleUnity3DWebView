#!/usr/bin/env bash
# Run on the user's WSL machine, with the HC2 scene already built and installed.
set -euo pipefail
probe_case=${1:-menu}
case "$probe_case" in menu|keyboard|native|native-webinfo|webview|activity|transparent) ;; *) echo 'Usage: bash Tools/run_host_probe.sh [menu|keyboard|native|native-webinfo|webview|activity|transparent] [off|on]' >&2; exit 2;; esac
probe_boot_args=()
case "${2:-}" in
    '') ;;
    off) probe_boot_args=(--es vrwebview_boot_expect 0) ;;
    on) probe_boot_args=(--es vrwebview_boot_expect 1) ;;
    *) echo 'Boot expectation must be off or on' >&2; exit 2;;
esac
if (( $# > 2 )); then echo 'Too many arguments' >&2; exit 2; fi
probe_adb=${VRWEBVIEW_ADB_EXE:-'/mnt/c/Program Files/Unity/Hub/Editor/6000.4.5f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe'}
probe_package=${VRWEBVIEW_PACKAGE:-com.DefaultCompany.WebViewGitHub}
case "$probe_package" in *[!a-zA-Z0-9._]*) echo 'Invalid package ID' >&2; exit 2;; esac
probe_component=$("$probe_adb" shell cmd package resolve-activity --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER -p "$probe_package" | tr -d '\r' | tail -n 1)
case "$probe_component" in
    "$probe_package/com.unity3d.player.UnityPlayerGameActivity"|"$probe_package/com.unity3d.player.UnityPlayerActivity") ;;
    *) echo "Unexpected launcher: $probe_component" >&2; exit 2;;
esac
"$probe_adb" shell am force-stop "$probe_package"
"$probe_adb" shell am start -n "$probe_component" --es vrwebview_host_case "$probe_case" "${probe_boot_args[@]}"
echo "Started HC2 case=$probe_case expectedBoot=${2:-unchecked} on $probe_component; compare bootCheck and runtime owner in logcat."
