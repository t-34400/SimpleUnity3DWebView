#!/usr/bin/env bash
# Compatibility entry: never discard HC/other-probe rows from an activity capture.
set -euo pipefail
if (( $# > 0 )); then echo 'Usage: bash Tools/capture_activity_gpu_probe.sh' >&2; exit 2; fi
probe_tools=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
exec python3 "$probe_tools/probe.py" capture --label activity-gpu --expect activity
