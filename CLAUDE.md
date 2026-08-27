# CLAUDE.md — Filekin

Claude Code must follow `AGENTS.md` and the Filekin master specifications.

## Startup

At the beginning of a Filekin session:

1. Read `AGENTS.md`.
2. Read `ENGINEERING-GUARDRAILS.md`. Its rules are normative for every change.
3. Read `HANDOFF.md`, which carries the live cross-agent state.
4. Read the master specification documents relevant to the task.
5. Inspect the existing repository before proposing structural changes.

`PROJECT-SETUP.md` is historical: it records the completed spike and the one-time production setup
sequence. Read it for background, not for the current phase.

## Current Project Phase

**Production implementation.** The PowerShell runspace + ConPTY spike is complete, its findings are
recorded in `HANDOFF.md`, and the production solution is live at `https://github.com/mefisme/filekin`.
Work proceeds one confirmed v1 command or surface at a time.

`HANDOFF.md` names the current immediate next task and what is still unimplemented. Trust it over
this file for scope, because it is updated every session.

Do not migrate `spikes/` code into production. Validated concepts are reimplemented behind the
production abstractions.

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
