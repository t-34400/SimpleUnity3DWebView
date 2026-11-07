#!/usr/bin/env bash
# Start in a separate WSL terminal before running run_host_probe.sh.
set -euo pipefail
probe_label=${1:-game}
case "$probe_label" in game|activity|off|on) ;; *) echo 'Usage: bash Tools/capture_host_probe.sh [off|on|game|activity]' >&2; exit 2;; esac
probe_adb=${VRWEBVIEW_ADB_EXE:-'/mnt/c/Program Files/Unity/Hub/Editor/6000.4.5f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe'}
probe_output=${VRWEBVIEW_LOG_DIR:-/mnt/c/Users/tmiyoshi/Downloads}
mkdir -p "$probe_output"
probe_log="$probe_output/VRWebView-host-$probe_label-$(date +%Y%m%d-%H%M%S).log"
echo "Capturing to $probe_log; press Ctrl+C after the trials."
# Start at the current time without clearing the device's other logs.
"$probe_adb" logcat -v threadtime -T 1 VRWebViewHost:I VRWebViewFocus:I Unity:I KeyboardInputMethodService:V ImeTracker:V InputMethodManager:V '*:S' \
  | tr -d '\r' \
  | sed -u -n -E '/VRWebViewHost|VRWebViewFocus|\[MetaXRFeature\].*OnSessionStateChange|PHASE_IME_|Timeout while waiting for keyboard|onShowInputRequested|Ignoring showSoftInput|is not served/p' \
  | tee "$probe_log"
