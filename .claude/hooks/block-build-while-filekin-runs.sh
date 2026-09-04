#!/usr/bin/env bash
# PreToolUse guard on Bash.
#
# Filekin holds its own build output while it runs, so a build started now dies
# with MSB3027 "file is locked by: Filekin (<pid>)" after wasting the copy step.
# HANDOFF.md says so in prose; prose does not stop an agent that never read it.
# This does.
#
# It guards only dotnet build/test/publish/format, and only while Filekin.exe is
# actually running, so an ordinary command is never delayed by it.
set -u

payload="$(cat 2>/dev/null || true)"

# Cheap reject first: most commands have nothing to do with the build.
case "$payload" in
  *dotnet*) ;;
  *) exit 0 ;;
esac

# The command field is one JSON string, so the class stays inside one value.
printf '%s' "$payload" | grep -Eq 'dotnet[^"]*(build|test|publish|format)' || exit 0

tasklist.exe 2>/dev/null | grep -qi '^Filekin\.exe' || exit 0

cat <<'JSON'
{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"deny","permissionDecisionReason":"Filekin.exe is running, so it holds its own build output. This build would fail with MSB3027 (file locked by Filekin), not with a real code error. Ask the user to close Filekin, or close it for them, then run the build again."}}
JSON
