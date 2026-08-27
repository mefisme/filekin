# PROJECT-SETUP.md — Filekin

## Status

**Historical.** The spike below is complete, its exit criteria are recorded in `HANDOFF.md`, and the
production setup that follows it is done. Read this document for background on why the architecture
was validated the way it was. For the current phase, task, and remaining scope, read `HANDOFF.md`.

## Goal

Establish Filekin safely by validating the highest-risk Windows shell/terminal architecture **before** production application development.

## Step 0 — Read Before Coding

Read:

1. `AGENTS.md`
2. `HANDOFF.md`
3. `PRODUCT.md`
4. `FEATURES.md`
5. `UX-DESIGN.md`
6. `ARCHITECTURE.md`
7. `ENGINEERING-GUARDRAILS.md`
8. `DECISIONS.md`

Do not infer product behavior from this setup document when a master specification already defines it.

# Step 1 — Build the Throwaway Shell/Terminal Spike

This is the first engineering task.

Create a small disposable C#/.NET Windows test application whose only purpose is to prove or disprove Filekin's proposed PowerShell runspace + ConPTY integration.

**Do not treat this as the production Filekin application.**

Keep it isolated, clearly named as a spike/prototype, and easy to delete.

## What the Spike Must Prove

### A. Persistent PowerShell Runspace

Prove that a hosted PowerShell runspace can execute multiple commands while preserving session state.

Test at minimum:

```powershell
$x = "hello"
Write-Output $x
```

The second command must observe state established by the first.

Also validate reasonable persistence of aliases/modules/session state needed by the command-bar model.

### B. Files → PowerShell Location Synchronization

Provide a minimal visual representation of a filesystem location.

When the test UI changes that location, update the PowerShell runspace to the same filesystem-backed location.

Do not use the application's process-wide current directory as the primary state model.

### C. PowerShell → Files Location Synchronization

Execute commands such as:

```powershell
cd ..
Set-Location <filesystem-path>
```

After command completion, detect the resulting runspace filesystem location and update the visual test location.

The visual location and command-bar runspace location must never silently diverge.

### D. Non-Filesystem Provider Detection

Test a PowerShell provider location such as:

```powershell
cd HKLM:\
```

Prove that Filekin can detect that the resulting PowerShell location is not representable by the Files hierarchy.

The production rule is that this context belongs in an independent terminal tab. The spike only needs to validate reliable detection and the feasibility of routing it.

### E. Finite Native Commands

Validate finite native commands through the command-bar execution path.

Examples:

```text
git status
where.exe git
```

Capture stdout/stderr and exit status without requiring a terminal surface.

Use commands actually available on the test machine; document substitutions.

### F. ConPTY Terminal Session

Create a minimal terminal-hosting proof using Windows ConPTY.

The test must demonstrate:

```text
test terminal surface
→ ConPTY
→ PowerShell
```

Validate normal input/output and resizing sufficiently to establish feasibility.

### G. Interactive Tool Inside PowerShell

From the ConPTY-backed PowerShell terminal, launch at least one available interactive program.

Preferred examples if installed:

```text
claude
codex
python
ssh
```

The root process must remain PowerShell.

When the child tool exits, the PowerShell prompt should remain.

When root PowerShell exits, the test terminal session should end.

### H. Routing Proof

Implement only enough routing to prove the architecture:

```text
known finite command
→ runspace/result path

known interactive command
→ ConPTY PowerShell terminal path
```

The final interactive-tool registry is **not** part of this spike.

AI coding agents must eventually be a first-class registry category in production.

### I. Unexpected Interactivity Investigation

Investigate what happens when an unknown/native command launched through the finite path unexpectedly requires interactive terminal input.

Do not invent a production solution without evidence.

Record:
- what can be detected reliably,
- what cannot,
- whether a running process can realistically be promoted,
- whether routing must occur before process creation,
- recommended production fallback behavior.

This is an explicit research outcome of the spike.

# Spike Exit Criteria

The spike is complete only when `HANDOFF.md` records:

1. what worked,
2. what failed,
3. relevant APIs/libraries used,
4. observed lifecycle behavior,
5. location-synchronization results,
6. ConPTY results,
7. interactive-routing findings,
8. unexpected-interactivity findings,
9. architectural changes recommended from evidence.

Do not proceed merely because a demo window opens.

# After the Spike

Stop and review the findings against `ARCHITECTURE.md`.

If the proposed architecture is validated, begin production repository/project scaffolding.

If evidence contradicts the architecture, update the handoff and request/record the necessary product/architecture decision before building around the contradiction.

## Production Setup — After Validation Only

Once the spike is accepted:

- create the production Filekin solution/project structure,
- establish the GPLv3 `LICENSE`,
- create/update public `README.md`,
- create `CONTRIBUTING.md`,
- create `SECURITY.md`,
- document build/test prerequisites,
- establish formatting/analyzer/test conventions,
- begin production implementation from the confirmed architecture.

Do not copy prototype code wholesale merely because it worked. Reimplement validated concepts cleanly behind the production abstractions defined by the architecture.

## Research Requirement for the Spike

Use current authoritative documentation while implementing the spike, especially for:

```text
PowerShell SDK / runspace hosting
PowerShell providers and location behavior
Windows Pseudoconsole (ConPTY)
process creation and console/PTY lifecycle
WPF hosting/interoperability where relevant
```

Prefer official Microsoft, .NET, and PowerShell documentation.

Do not treat remembered API behavior or generated code as proof. The running spike and authoritative documentation are the evidence.

## Failure Is a Valid Spike Result

The spike is an experiment, not a demonstration whose conclusion is predetermined.

A failed assumption is useful if it is clearly reproduced and documented.

Do not add increasingly complex workarounds merely to make the proposed architecture appear valid. If a core assumption fails, record it in `HANDOFF.md` and stop for architecture review before production scaffolding.
