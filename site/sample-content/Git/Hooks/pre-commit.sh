#!/usr/bin/env bash
# Refuse commits that still contain merge conflict markers.
set -euo pipefail

if git diff --cached --name-only -z | xargs -0 grep -lE '^(<<<<<<<|>>>>>>>)' 2>/dev/null; then
  echo "Conflict markers found, commit aborted." >&2
  exit 1
fi
