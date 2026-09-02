# CLAUDE.md — Filekin

Claude Code follows `AGENTS.md`; this file only supplies its vendor-specific entry point. Keep it
under 200 lines and do not duplicate the product specifications or live handoff here.

## Start every session

1. Read `AGENTS.md`.
2. Read the normative `ENGINEERING-GUARDRAILS.md`.
3. Read the short live `HANDOFF.md`.
4. Read only the master-spec sections relevant to the task.
5. Inspect the repository before proposing structural changes.

`PROJECT-SETUP.md` and `HANDOFF-ARCHIVE.md` are history, not current instruction. Production work
reimplements any validated spike concept behind production abstractions; it never grows `spikes/` into
the app.

## Work and handoff

Prefer small, testable changes. Do not invent missing features or silently change confirmed behavior.
Surface specification conflicts before implementing around them. At the end of meaningful work,
replace the live state in `HANDOFF.md` with the next agent's exact task, blockers, and new traps—never a
session diary or test ledger.
