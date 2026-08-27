# AGENTS.md — Filekin

## Purpose

This file defines shared rules for any coding agent working on Filekin.

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

The throwaway PowerShell runspace + ConPTY spike is **complete** and its findings are recorded in `HANDOFF.md`. The project is in production implementation.

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

`HANDOFF.md` is the shared cross-agent state.

Before ending a meaningful work session:
- update current status,
- list files changed,
- record tests/results,
- record unresolved questions,
- state the exact recommended next step.

Do not erase useful handoff history merely because another agent wrote it.

## Scope Discipline

Do not add AI features merely because an AI agent is implementing the project.

Filekin should not contain “AI slop”: unnecessary dashboards, decorative cards, excessive gradients, generic generated copy, invented abstractions, or features not justified by the product specifications.

## Open Source

Filekin is intended to be developed publicly under GNU GPLv3. Keep setup, build, and test procedures understandable to outside contributors.

## Documentation and Evidence Rule

When implementing or validating unfamiliar Windows, .NET, WPF, PowerShell-hosting, or ConPTY behavior, consult current authoritative documentation rather than relying on memory or assumptions.

Prefer official Microsoft/.NET/PowerShell documentation for platform/API behavior. Record important implementation-relevant sources or conclusions in `HANDOFF.md` when they materially affect architecture.

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
