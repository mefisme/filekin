#!/usr/bin/env bash
# Stop gate.
#
# "Done" means the validation block in HANDOFF.md passed, not that the work looked
# finished. An agent that says done without running it has happened, so this runs
# it instead of trusting that it was run.
#
# It is silent and free unless src/ or tests/ actually changed, so answering a
# question costs no build. It honours stop_hook_active, so it forces one round of
# fixing rather than looping for ever; a second red stop returns control to the
# user with the failure named.
set -u

cd "${CLAUDE_PROJECT_DIR:-.}" 2>/dev/null || exit 0

payload="$(cat 2>/dev/null || true)"
case "$payload" in
  *'"stop_hook_active":true'*|*'"stop_hook_active": true'*) exit 0 ;;
esac

# Nothing changed under the code, so this block can have broken nothing new.
[ -n "$(git status --porcelain -- src tests 2>/dev/null)" ] || exit 0

# The reason is fed straight back as JSON, so it stays plain text: no quotes,
# no backslashes, no newlines.
block() {
  printf '{"decision":"block","reason":"%s"}\n' "$1"
  exit 0
}

if tasklist.exe 2>/dev/null | grep -qi '^Filekin\.exe'; then
  block "Filekin.exe is running, so the validation block cannot build. Close Filekin, then run the four checks in HANDOFF.md and report what they said."
fi

dotnet build Filekin.sln -c Release -m:1 --no-restore >/dev/null 2>&1 ||
  block "The Release build failed, so this work is not done. Run: dotnet build Filekin.sln -c Release -m:1 --no-restore -- read the errors and fix them."

dotnet test Filekin.sln -c Release --no-build --no-restore -m:1 >/dev/null 2>&1 ||
  block "Release tests failed, so this work is not done. Run: dotnet test Filekin.sln -c Release --no-build --no-restore -m:1 -- read which test failed and fix it. Never report a pass you did not see."

dotnet format Filekin.sln --verify-no-changes --no-restore >/dev/null 2>&1 ||
  block "Formatting is not clean. Run: dotnet format Filekin.sln --no-restore -- then check the diff it made."

git diff --check >/dev/null 2>&1 ||
  block "The diff has whitespace errors. Run: git diff --check -- and fix the lines it names."

exit 0
