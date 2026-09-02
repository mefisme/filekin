# AGENTS.md — Filekin

## Purpose

This file defines shared rules for any coding agent working on Filekin. Keep it under 200 lines and
do not duplicate specifications or live handoff state here.

Before any work — reading, planning, review, or implementation — read this file, then `ENGINEERING-GUARDRAILS.md`, then `HANDOFF.md`, then the master specification documents relevant to the task.

`PROJECT-SETUP.md` is historical background, not current instruction; `HANDOFF.md` is authoritative for the current phase and task.

## Product

**Filekin** is a keyboard-first Windows file manager + terminal.

The visual Files workspace and its command bar are one synchronized filesystem context. Independent shell contexts live in terminal tabs.

## Source of Truth

The master specifications are:

- `PRODUCT.md`
- `FEATURES.md`
- `UX-DESIGN.md`
- `ARCHITECTURE.md`
- `ENGINEERING-GUARDRAILS.md`
- `DECISIONS.md`

`PRODUCT.md`, `FEATURES.md`, `UX-DESIGN.md`, `ARCHITECTURE.md`, and `DECISIONS.md` define *what* Filekin is. `ENGINEERING-GUARDRAILS.md` is different in kind: its rules are normative for *how* the code is written, and they apply to every change regardless of which feature is being built. Read it before you write code, not only when a product question arises.

Do not silently reinterpret a confirmed product decision. If implementation evidence conflicts with a specification, record the conflict in `HANDOFF.md` and surface it for a product decision.

## Project Phase

The throwaway PowerShell runspace + ConPTY spike is **complete**. The project is in production implementation.

The spike under `spikes/` remains disposable validation code and stays outside the production solution. Do not gradually turn it into the production application; reimplement validated concepts behind the production abstractions.

`HANDOFF.md` is authoritative for the current task and remaining scope.

## Engineering Priorities

1. Reliability and predictable behavior.
2. Fast keyboard-first interaction.
3. Clear architecture over clever abstractions.
4. Native Windows behavior where appropriate.
5. Reuse shared infrastructure without making every surface generic.
6. No speculative features outside the confirmed v1 scope.

## Technology Direction

- C# / .NET
- WPF application shell using a hybrid native/custom visual approach
- Persistent PowerShell runspace for the Files command bar
- ConPTY-backed independent terminal tabs
- JSON for human-readable user configuration
- SQLite for transactional history/undo state
- Native C# `TidyEngine`
- Self-contained .NET releases
- Traditional installer + portable ZIP
- GPLv3

Do not apply stock WPF visual templates as the product design. WPF is implementation infrastructure; Filekin has its own visual language.

## Agent Coordination

`HANDOFF.md` is the shared cross-agent state, and it is deliberately short so it can be read in full at the start of every session.

Before ending a meaningful work session, update it with:
- the current phase,
- the exact next task,
- anything newly blocked, and the decision it waits on,
- any standing contract or trap the work established,
- any new known problem.

Do **not** append a session changelog, a list of changed files, or a test count. Git records those, and they pushed this file past 2000 lines once already. When a feature is finished, replace its entry with the conclusion a future agent needs.

`HANDOFF-ARCHIVE.md` is historical storage, not current instruction. Move retired detail there only
when it remains useful, never act on it as live state, and do not rewrite another agent's history.

## Scope Discipline

Do not add AI features merely because an AI agent is implementing the project.

Filekin should not contain “AI slop”: unnecessary dashboards, decorative cards, excessive gradients, generic generated copy, invented abstractions, or features not justified by the product specifications.

## Open Source

Filekin is intended to be developed publicly under GNU GPLv3. Keep setup, build, and test procedures understandable to outside contributors.

## Documentation and Evidence Rule

When implementing or validating unfamiliar Windows, .NET, WPF, PowerShell-hosting, or ConPTY behavior, consult current authoritative documentation rather than relying on memory or assumptions.

Prefer official Microsoft/.NET/PowerShell documentation for platform/API behavior. Put durable
implementation conclusions in the relevant master specification or decision; keep only a current
blocker or load-bearing trap in `HANDOFF.md`.

## Specification Gaps

If a product behavior is genuinely unspecified and the choice would affect user-visible behavior, architecture, scope, or compatibility, do not silently invent the decision. Record the question and ask for a product decision.

Agents may make ordinary local implementation choices that do not alter confirmed behavior.

## Spike Integrity

The technical spike is allowed to **disprove** the proposed architecture.

Do not force the spike to satisfy the current design through hacks or hidden assumptions. If evidence contradicts a specification or architectural assumption:

1. reproduce/verify the behavior,
2. document the evidence in `HANDOFF.md`,
3. identify the affected specification/decision,
4. recommend options,
5. stop before building production code around an unresolved contradiction.
