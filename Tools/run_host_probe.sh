#!/usr/bin/env bash
# Run on the user's WSL machine, with the HC1 scene already built and installed.
set -euo pipefail
probe_case=${1:-menu}
case "$probe_case" in menu|keyboard|native|webview|activity|transparent) ;; *) echo 'Usage: bash Tools/run_host_probe.sh [menu|keyboard|native|webview|activity|transparent]' >&2; exit 2;; esac
probe_adb=${VRWEBVIEW_ADB_EXE:-'/mnt/c/Program Files/Unity/Hub/Editor/6000.4.5f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe'}
probe_package=${VRWEBVIEW_PACKAGE:-com.DefaultCompany.WebViewGitHub}
case "$probe_package" in *[!a-zA-Z0-9._]*) echo 'Invalid package ID' >&2; exit 2;; esac
probe_component=$("$probe_adb" shell cmd package resolve-activity --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER -p "$probe_package" | tr -d '\r' | tail -n 1)
case "$probe_component" in
    "$probe_package/com.unity3d.player.UnityPlayerGameActivity"|"$probe_package/com.unity3d.player.UnityPlayerActivity") ;;
    *) echo "Unexpected launcher: $probe_component" >&2; exit 2;;
esac
"$probe_adb" shell am force-stop "$probe_package"
"$probe_adb" shell am start -n "$probe_component" --es vrwebview_host_case "$probe_case"
echo "Started HC1 case=$probe_case on $probe_component; compare the runtime owner in logcat."
