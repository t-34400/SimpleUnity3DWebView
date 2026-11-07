#!/usr/bin/env bash
# Fixed AG1 trials. Run from the Unity project in WSL after building the AG1 scene.
set -euo pipefail
probe_mode=${1:-texture}
case "$probe_mode" in texture|native|video|ime|passthrough) ;; *) echo 'Usage: bash Tools/run_activity_gpu_probe.sh [texture|native|video|ime|passthrough]' >&2; exit 2;; esac
probe_adb=${VRWEBVIEW_ADB_EXE:-'/mnt/c/Program Files/Unity/Hub/Editor/6000.4.5f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe'}
probe_package=${VRWEBVIEW_PACKAGE:-com.DefaultCompany.WebViewGitHub}
case "$probe_package" in ''|*[!a-zA-Z0-9._]*) echo 'Invalid package ID' >&2; exit 2;; esac
probe_component=$("$probe_adb" shell cmd package resolve-activity --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER -p "$probe_package" | tr -d '\r' | tail -n 1)
case "$probe_component" in "$probe_package/com.unity3d.player.UnityPlayerGameActivity"|"$probe_package/com.unity3d.player.UnityPlayerActivity") ;; *) echo "Unexpected launcher: $probe_component" >&2; exit 2;; esac
"$probe_adb" shell am force-stop "$probe_package"
"$probe_adb" shell am start -n "$probe_component" --es vrwebview_activity_gpu_mode "$probe_mode"
echo "Started AG1 mode=$probe_mode; native host returns after 120 seconds."
