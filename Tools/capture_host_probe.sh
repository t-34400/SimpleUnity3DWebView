#!/usr/bin/env bash
# Compatibility entry: every probe family is captured by the common collector.
set -euo pipefail
probe_label=${1:-game}
case "$probe_label" in game|activity|off|on) ;; *) echo 'Usage: bash Tools/capture_host_probe.sh [off|on|game|activity]' >&2; exit 2;; esac
if (( $# > 1 )); then echo 'Too many arguments' >&2; exit 2; fi
probe_tools=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
exec python3 "$probe_tools/probe.py" capture --label "host-$probe_label" --expect host
