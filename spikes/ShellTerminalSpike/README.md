# Shell/Terminal Spike

This directory is disposable validation code for `PROJECT-SETUP.md` Step 1. It is not the production Filekin application and must not become the production scaffold.

The harness exercises:

- persistent hosted PowerShell state,
- bidirectional filesystem-location synchronization,
- non-filesystem provider detection and lockstep restoration,
- finite native stdout/stderr/exit-code capture,
- deterministic finite/interactive routing,
- a native Windows ConPTY session with PowerShell as the root shell,
- terminal input/output, resize, interactive Python child lifecycle, and root-shell exit,
- unexpected interactivity on the finite path.

Run the automated evidence suite from the repository root:

```powershell
.\.tools\dotnet\dotnet.exe run --project .\spikes\ShellTerminalSpike\ShellTerminalSpike.csproj
```

Run the minimal visual location test UI:

```powershell
.\.tools\dotnet\dotnet.exe run --project .\spikes\ShellTerminalSpike\ShellTerminalSpike.csproj -- interactive
```

Generated evidence is written to `artifacts/latest-results.json`.
