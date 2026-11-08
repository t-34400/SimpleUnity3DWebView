#!/usr/bin/env bash
# Compatibility entry point; launch only when explicitly invoked.
set -euo pipefail
exec python3 "$(dirname "$0")/probe.py" launch-activity "$@"
