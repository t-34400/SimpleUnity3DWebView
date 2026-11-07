#!/usr/bin/env bash
set -euo pipefail
probe_adb=${VRWEBVIEW_ADB_EXE:-'/mnt/c/Program Files/Unity/Hub/Editor/6000.4.5f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe'}
probe_output=${VRWEBVIEW_LOG_DIR:-/mnt/c/Users/tmiyoshi/Downloads}
mkdir -p "$probe_output"
probe_log="$probe_output/VRWebView-activity-gpu-$(date +%Y%m%d-%H%M%S).log"
echo "Capturing to $probe_log; press Ctrl+C after the trials."
"$probe_adb" logcat -v threadtime -T 1 VRWebViewActivityGPU:I VRWebViewFocus:I Unity:I AndroidRuntime:E KeyboardInputMethodService:V ImeTracker:V InputMethodManager:V '*:S' \
  | tr -d '\r' \
  | sed -u -n -E '/VRWebViewActivityGPU|VRWebViewFocus|\[MetaXRFeature\].*OnSessionStateChange|PHASE_IME_|Timeout while waiting for keyboard|onShowInputRequested|Ignoring showSoftInput|is not served|AndroidRuntime/p' \
  | tee "$probe_log"
