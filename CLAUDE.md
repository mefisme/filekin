# CLAUDE.md — Filekin

Claude Code must follow `AGENTS.md` and the Filekin master specifications.

## Startup

At the beginning of a Filekin session:

1. Read `AGENTS.md`.
2. Read `PROJECT-SETUP.md`.
3. Read `HANDOFF.md`.
4. Read the master specification documents relevant to the task.
5. Inspect the existing repository before proposing structural changes.

## Current Project Phase

The first engineering activity is a **throwaway technical spike** validating the PowerShell runspace + ConPTY architecture.

Do not start building production Filekin UI or migrate spike code into production until the spike exit criteria in `PROJECT-SETUP.md` are satisfied and documented.

## Working Style

Prefer small, testable changes.

Explain architectural deviations before implementing them.

Do not assume a missing feature is desired. Check `FEATURES.md` and `DECISIONS.md`.

When a task is complete, update `HANDOFF.md` so Codex or another agent can continue without reconstructing the session.

## Filekin-Specific Invariants

- Files hierarchy and Files command bar always share the same filesystem location.
- Filesystem `cd` / `Set-Location` can move the visual Files location.
- GUI Files navigation updates the command-bar runspace location.
- Non-filesystem PowerShell locations belong in an independent terminal tab.
- Terminal tabs host PowerShell through ConPTY; interactive tools run inside that shell.
- Terminal tabs inherit Files location once, then become independent.
- Child tool exit returns to PowerShell.
- Root PowerShell exit closes the terminal tab.
- `/` and `@` syntax belong to the Files command bar, not normal independent terminal input.
