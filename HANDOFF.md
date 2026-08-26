# HANDOFF.md — Filekin

## Purpose

Shared handoff state for Codex, Claude Code, and other implementation agents.

Keep this document current enough that another agent can continue the project without relying on chat history.

## Current Phase

**Core services are in place, and the WPF Files-shell now renders a real, live filesystem hierarchy. The Files listing, path bar, sorting, navigation, and selection are wired to the filesystem; the command bar and terminal host are not yet wired, and no VT terminal renderer exists yet.**

The public repository is live at `https://github.com/mefisme/filekin`, with `main` protected by an active repository ruleset. The production solution contains platform-neutral shell/location/terminal contracts, an asynchronous persistent PowerShell runspace adapter, a ConPTY-backed terminal-host service, and a `Filekin.Core.Commands` router that classifies command-bar input (app `/` command vs. finite shell vs. known-interactive terminal) and dispatches it across the runspace backend and terminal host, including provider-delegation terminal launches. `Filekin.App` now starts a custom dark WPF shell preview with Filekin tabs, `@` Locations, `/places` and `/drives`, terminal-style file rows, the command bar/result state, and expandable finite-command output. Its `ShellViewModel` intentionally contains design/sample data only; production navigation, selection, command execution, sorting, and terminal rendering remain unwired.

## Current Product Identity

- Name: **Filekin**
- Category: keyboard-first Windows file manager + terminal
- License direction: GNU GPLv3
- Distribution: traditional installer + portable ZIP
- Runtime deployment: self-contained .NET

## GitHub Repository

- Public repository: `https://github.com/mefisme/filekin`
- Default branch: `main`
- Initial commit: `caba0d8` (`chore: establish Filekin production foundation`)
- GitHub recognizes the license as GPL-3.0.
- `.github/` contains SHA-pinned secretless Windows CI, `CODEOWNERS`, a PR template, and weekly Dependabot configuration for GitHub Actions and NuGet.
- Initial CI run `32869871853`: passed restore, Release build, all tests, and formatting verification.
- **Active branch protection**: repository ruleset `main` (id `21453006`), enforcement `active`, targeting the default branch. Rules: pull-request required with `required_approving_review_count = 1`, `require_code_owner_review = false`, `require_extra_approval_for_unattributed_changes = false`; required status check `Build, test, and format (Windows)` bound to the GitHub Actions app (`integration_id 15368`); block deletion and non-fast-forward. Bypass actor: repository admin role (`actor_id 5`, `bypass_mode always`) as the owner emergency bypass so the solo owner is not locked out.
- **CODEOWNERS is review routing only**, not a mandatory gate: `require_code_owner_review` is deliberately false so code-owner paths route review requests without adding a second required approval beyond the one requested review.

## Immediate Next Task

The command router is done. Two remaining non-UI command-bar core pieces come next, and both are testable without a window:

1. The **`@` reference resolver** (`ARCHITECTURE.md` §3). **Done (2026-08-25):** `Filekin.Core.Commands.References` (`ReferenceResolver`, `IReferenceResolver`, `ReferenceContext`, `ReferenceResolution`, `INamedLocationResolver`) resolves `@thisfolder`, `@selection` (multi-item), and named locations to quoted paths, light-touch, with unknown/native `@` passing through; `WindowsKnownFolderLocations` supplies `@desktop/@documents/@downloads/@pictures/@music/@videos/@home`. See Work Completed and Recommended Next Step.
2. The **application-command (`/`) dispatch** subsystem that consumes `CommandRouterResult.AppCommandInput` and runs built-in commands. **Done (2026-08-25):** `Filekin.Core.Commands.App` + the four core file-operation commands over `IFileSystemOperations`, with `WindowsFileSystemOperations`.

The first **static WPF Files-shell design preview** is now present (2026-08-25). It establishes the dark visual tokens, custom control styles, window/tab/sidebar/file-row/command-bar composition, and expandable finite-command output, but deliberately uses sample data and visual-only tabs/navigation.

The next production seam is to replace one static surface at a time without losing the validated visual language:

1. **Done (2026-08-25):** the Files hierarchy is wired to real filesystem enumeration/navigation/selection state, with clickable and keyboard-operable sortable column headers as required by `DECISIONS.md`. See Work Completed.
2. Replace the visual-only command row with an actual input control and wire `ReferenceResolver.ResolveLine(input, context)` → `CommandRouter` / `AppCommandDispatcher`, preserving the current expandable finite-output behavior. The live selection/location is already exposed: `ShellViewModel.BuildReferenceContext()` returns a `ReferenceContext` from the current folder and Files selection.
3. Add a terminal renderer that interprets the raw VT/ANSI stream from `ConPtyTerminalSession.OutputReceived`; do not treat a plain text control as a terminal.

Do not present the fake command result, static tabs, or static Locations/Surfaces as completed product behavior. The Files listing itself is now real.

## Spike Status

**Complete on the test machine.**

- Disposable project: `spikes/ShellTerminalSpike/`
- Automated result: **25 passed, 0 failed**
- Evidence: `spikes/ShellTerminalSpike/artifacts/latest-results.json`
- Final environment: Windows 10.0.26200 x64, .NET runtime 10.0.10, workspace-local SDK 10.0.400, Microsoft.PowerShell.SDK 7.6.5, external PowerShell 7.6.4, Python 3.13.15
- This is validation code only and is not a production Filekin scaffold.

## Findings

### What Worked

1. **Persistent PowerShell runspace**
   - One hosted runspace preserved `$x = "hello"` for a later `Write-Output $x` invocation.
   - Aliases, functions, and an imported module remained available across separate executions.
   - `InitialSessionState.CreateDefault2()`, `RunspaceFactory.CreateRunspace(...)`, and repeated `PowerShell.Invoke()` against the same runspace were sufficient for the proof.

2. **Files → PowerShell location synchronization**
   - The minimal test UI changed its visible `FILES LOCATION` and set the runspace with `Set-Location -LiteralPath`.
   - The runspace reported the matching FileSystem provider path.
   - `Environment.CurrentDirectory` did not change, proving that process-wide current directory is not required as the primary state model.

3. **PowerShell → Files location synchronization**
   - `cd ..` / `Set-Location` results were read from the runspace's `PathInfo` after command completion and updated the visible test location.
   - The manual UI pass showed `D:\GitHub\filekin\spikes\ShellTerminalSpike` changing to `D:\GitHub\filekin\spikes` after `ps cd ..`.

4. **Non-filesystem provider detection**
   - `Set-Location HKLM:\` reliably produced provider `Registry` and path `HKLM:\`.
   - The test restored the runspace to the prior Files filesystem path immediately, preserving the no-divergence rule.
   - The manual UI displayed `ROUTE TO TERMINAL: provider=Registry; path=HKLM:\` while Files remained at its prior filesystem location.

5. **Finite native commands**
   - `where.exe git` returned stdout and exit code 0.
   - `git status` (run from this non-Git directory) returned stderr and exit code 128; this was an intentional available-machine substitution that proved failure capture.
   - A purpose-built native probe independently proved stdout capture, stderr capture, and a nonzero exit code of 7.

6. **ConPTY terminal session**
   - The path `test terminal surface → ConPTY → PowerShell` worked with UTF-8 pipe input/output.
   - `ResizePseudoConsole(100, 30)` was observed inside PowerShell as `100x30` through `$Host.UI.RawUI.WindowSize`.
   - PowerShell was the root process created with `STARTUPINFOEX` and `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE`.

7. **Interactive child lifecycle**
   - Python 3.13.15 was used because it was installed and stable for automation.
   - `python -q` launched inside the ConPTY-backed root PowerShell, accepted input, and emitted output.
   - `exit()` returned to the same PowerShell, which accepted another command.
   - `exit` in root PowerShell ended the root process/session.

8. **Routing proof**
   - `where.exe git` classified to the finite runspace/result path.
   - bare `python` classified to the ConPTY PowerShell terminal path.
   - `python script.py` classified to the finite path, proving that simple argument-sensitive rules are feasible.

### Failures Encountered and Resolved

- The first ConPTY launch inherited the parent process's redirected stdout instead of using the pseudoconsole output pipe. This reproduced a Windows standard-handle duplication edge case documented by Microsoft Terminal maintainers.
- Setting `STARTF_USESTDHANDLES` while leaving `hStdInput`, `hStdOutput`, and `hStdError` null forced the child to establish standard I/O through ConPTY. After this change, every ConPTY check passed.
- ConPTY pipe handles created by `CreatePipe` are synchronous. Constructing `FileStream` with `isAsync: true` failed. The working implementation uses synchronous handles while servicing input and output on separate tasks/threads as Microsoft recommends.

### Lifecycle / ConPTY Constraints

- ConPTY communication channels must be serviced independently to avoid full-buffer deadlocks.
- Output must continue to be drained through teardown; `ClosePseudoConsole` can emit a final frame.
- `ClosePseudoConsole` terminates attached console clients. Product shutdown should still follow the specified graceful-first policy before using pseudoconsole closure as final teardown.
- A terminal renderer must interpret VT/ANSI sequences; a plain text output control is not sufficient. The spike captures raw VT output and intentionally does not attempt to become a production renderer.

### Unexpected Interactivity Findings

Observed finite-path behavior depends on the host environment:

- In the headless/WPF-like automated path, the unknown native helper saw redirected stdin, received EOF immediately, and exited with a failure code. It had no usable terminal input channel.
- In a manual console-hosted path, the helper saw non-redirected stdin but the Files-style command surface could not reliably deliver input to it; it waited until its self-timeout. Input sent during that wait was later consumed by the parent test UI.

What can be detected reliably:

- executable/argument matches in a deterministic registry before process creation,
- explicit user choice before process creation,
- command completion, output/error streams, and native exit code afterward,
- final runspace provider/location after a PowerShell command completes.

What cannot be detected reliably:

- whether an arbitrary unknown executable will later request terminal input,
- whether a quiet/running process is waiting for input versus doing legitimate finite work,
- whether argument combinations become interactive without tool-specific knowledge.

Promotion finding:

- ConPTY association is supplied to `CreateProcessW` through `STARTUPINFOEX` and `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE` at process creation time.
- No documented supported API attaches an already-running finite-path native process to a newly created ConPTY session.
- A running process therefore cannot realistically be promoted in place. Routing must happen before process creation, or the command must be stopped/allowed to fail and then launched again as a fresh process in a terminal.

Recommended fallback for architecture review:

- route known interactive invocations before creation,
- give finite-path native commands no synthetic interactive stdin,
- when an unknown command fails/hangs in a way the user identifies as interactive, offer an explicit fresh `Run in terminal` relaunch,
- do not claim that the already-running process or its state was promoted.

### Architecture Review

The core proposed architecture is validated:

```text
Files command bar → persistent PowerShell runspace → finite result
terminal tab      → ConPTY → root PowerShell → interactive child
```

No fundamental replacement architecture is recommended. Production implementation should carry forward these evidence-based constraints:

1. Use `STARTF_USESTDHANDLES` with null standard handles when creating the ConPTY root process, especially from a GUI or redirected host.
2. Drain ConPTY input/output independently and through teardown.
3. Route interactive tools before process creation; fallback is fresh relaunch, not live promotion.
4. Detect runspace provider after every command and immediately restore the Files filesystem location if the result is non-filesystem.
5. Treat non-filesystem terminal delegation as creation of a new root PowerShell initialized to the detected provider location; do not imply that the in-process runspace itself moved into ConPTY.

## Files Changed This Session

Initial project-development documents created:
- `AGENTS.md`
- `CLAUDE.md`
- `HANDOFF.md`
- `PROJECT-SETUP.md`

Master specifications updated with the official Filekin product name.

Spike session additions:

- `spikes/ShellTerminalSpike/ShellTerminalSpike.csproj`
- `spikes/ShellTerminalSpike/Program.cs`
- `spikes/ShellTerminalSpike/PowerShellRunspaceBackend.cs`
- `spikes/ShellTerminalSpike/ConPtySession.cs`
- `spikes/ShellTerminalSpike/CommandRouting.cs`
- `spikes/ShellTerminalSpike/SpikeRunner.cs`
- `spikes/ShellTerminalSpike/TestUi.cs`
- `spikes/ShellTerminalSpike/README.md`
- `spikes/ShellTerminalSpike/artifacts/latest-results.json`
- `spikes/Directory.Build.props` (keeps the frozen disposable spike outside production analyzer policy)
- `.tools/dotnet/` (workspace-local .NET SDK 10.0.400 required because the machine had runtimes but no SDK)
- `.tools/dotnet-install.ps1` (official Microsoft installer used for the local SDK)
- 2026-08-25 — a machine-wide .NET SDK 10.0.400 (10.0.4xx GA band, matching `global.json` `latestPatch`) was also installed into `C:\Program Files\dotnet` via the official installer (elevated), so the plain `dotnet` on PATH now builds/tests the solution directly — the gitignored `.tools/dotnet/` bootstrap remains valid but is no longer required on this machine.
- `HANDOFF.md`

Production scaffold additions/updates:

- Initialized the Git repository on branch `main` (no commit created).
- `Filekin.sln`
- `global.json`
- `Directory.Build.props`
- `.editorconfig`
- `.gitignore`
- `LICENSE`
- `README.md`
- `CONTRIBUTING.md`
- `SECURITY.md`
- `src/Filekin.App/`
- `src/Filekin.Core/`
- `src/Filekin.Infrastructure.Windows/`
- `tests/Filekin.Core.Tests/`
- `ARCHITECTURE.md`
- `DECISIONS.md`
- `ENGINEERING-GUARDRAILS.md`
- `FEATURES.md`
- `UX-DESIGN.md`
- `HANDOFF.md`

First production shell-boundary additions/updates:

- `src/Filekin.Core/Shell/IShellBackend.cs`
- `src/Filekin.Core/Shell/ShellExecutionResult.cs`
- `src/Filekin.Core/Shell/ShellLocation.cs`
- `src/Filekin.Core/Shell/ShellTerminalLaunchRequest.cs`
- `src/Filekin.Infrastructure.Windows/Shell/PowerShellRunspaceBackend.cs`
- `src/Filekin.Infrastructure.Windows/Filekin.Infrastructure.Windows.csproj`
- `tests/Filekin.Infrastructure.Windows.Tests/`
- `Filekin.sln`
- `README.md`
- `HANDOFF.md`

Public GitHub repository setup:

- Created and pushed public `mefisme/filekin` with `main` as the default branch.
- `.github/CODEOWNERS`
- `.github/PULL_REQUEST_TEMPLATE.md`
- `.github/dependabot.yml`
- `.github/workflows/ci.yml`
- `README.md` CI badge
- `HANDOFF.md`

Branch governance + terminal-host boundary session:

- Created the active `main` repository ruleset via the GitHub REST API (no file in the repo; the ruleset lives on GitHub).
- Platform-neutral terminal contracts in `Filekin.Core`:
  - `src/Filekin.Core/Terminal/TerminalSize.cs`
  - `src/Filekin.Core/Terminal/TerminalOutputEventArgs.cs`
  - `src/Filekin.Core/Terminal/TerminalExitEventArgs.cs`
  - `src/Filekin.Core/Terminal/TerminalSessionRequest.cs`
  - `src/Filekin.Core/Terminal/ITerminalSession.cs`
  - `src/Filekin.Core/Terminal/ITerminalHost.cs`
- ConPTY terminal-host service in `Filekin.Infrastructure.Windows`:
  - `src/Filekin.Infrastructure.Windows/Terminal/Interop/ConPtyInterop.cs` (LibraryImport P/Invoke + blittable structs)
  - `src/Filekin.Infrastructure.Windows/Terminal/PowerShellExecutableLocator.cs`
  - `src/Filekin.Infrastructure.Windows/Terminal/ConPtyTerminalSession.cs`
  - `src/Filekin.Infrastructure.Windows/Terminal/ConPtyTerminalHost.cs`
  - `src/Filekin.Infrastructure.Windows/Filekin.Infrastructure.Windows.csproj` (`AllowUnsafeBlocks` for LibraryImport marshalling)
- Tests:
  - `tests/Filekin.Infrastructure.Windows.Tests/Terminal/ConPtyTerminalSessionTests.cs`
- `HANDOFF.md`

Command-router session:

- Command routing in `Filekin.Core/Commands/`:
  - `CommandRoute.cs`, `CommandClassification.cs`
  - `IInteractiveCommandRegistry.cs`, `InteractiveCommandRegistry.cs`
  - `ICommandClassifier.cs`, `CommandClassifier.cs`
  - `CommandRouterResult.cs`, `CommandRouter.cs`
- Tests:
  - `tests/Filekin.Core.Tests/Commands/CommandClassifierTests.cs`
  - `tests/Filekin.Core.Tests/Commands/CommandRouterTests.cs`
- `HANDOFF.md`

Maximized-window work-area fix:

- `src/Filekin.App/Views/MainWindow.xaml.cs`
- `src/Filekin.Infrastructure.Windows/Windowing/MaximizedWindowBounds.cs`
- `HANDOFF.md`

## Unresolved Engineering Questions

The spike resolved the feasibility questions above. The owner confirmed the two resulting decisions on 2026-08-25.

## Handoff Template

Agents should update the sections below before stopping meaningful work.

### Last Agent
Claude Code — 2026-08-25.

### Work Completed
Wired the Files hierarchy to the real filesystem (HANDOFF.md "next seam" step 1), preserving the validated `MainWindow` visual tokens/composition. New platform-neutral Core pieces under `Filekin.Core.FileSystem`: `DirectoryEntry`, `IDirectoryLister` + `FileSystemDirectoryLister` (one-level enumeration over ordinary .NET APIs, skips items it cannot stat), `FileTypeCode` (deterministic extension→terminal type-code map, not AI classification), and `FileListingSort` (directories always group first; the active column sorts within each group; re-sort reverses direction; case-insensitive ordinal name tie-break). New App view models: `ObservableObject` (hand-rolled `INotifyPropertyChanged`, no MVVM dependency added), `FileRowViewModel` (immutable display row), `PathSegmentViewModel` (clickable crumb), `FileLauncher` (GUI-open via Windows association), and a rebuilt `ShellViewModel` that owns the current location, listing, sort, and selection, enumerates off the UI thread, and exposes `BuildReferenceContext()` for the future command bar. `MainWindow` now has a live clickable path bar, keyboard-accessible sortable column headers (Buttons with `AutomationProperties.Name` + active-column caret), a virtualizing recycling Files list, double-click/Enter to open, Backspace to go up, selection→status count, and a real free-space status. Caption buttons also got accessible names. The command row, tabs, and sidebar Locations/Surfaces remain static preview.

Previous Codex entry:
Fixed the custom-chrome window's maximize-only taskbar overlap. `MainWindow` now handles `WM_GETMINMAXINFO` after its native source is initialized, and `MaximizedWindowBounds` sizes and positions the window to the nearest monitor's `MONITORINFO.rcWork` instead of the full monitor rectangle. This preserves Windows taskbar space on any edge and supports non-primary monitors whose virtual-screen coordinates may be negative. The existing maximized content inset remains in place for the invisible `WindowChrome` resize border.

Recovered and completed Claude Code's interrupted first WPF Files-shell design pass. Preserved Claude's uncommitted dark-theme tokens, custom control styles, static `ShellViewModel`, `MainWindow`, startup wiring, and the six new visual/interaction decisions in `DECISIONS.md`. Replaced fragile private-use glyph literals with ASCII C# `\uE922` / `\uE923` escapes (XAML glyphs use numeric XML references), repaired invalid XML comments, made sample status properties analyzer-compliant instance properties, separated Segoe MDL2 icon glyphs from normal Settings/About labels, and implemented the confirmed Esc-to-collapse output behavior with focus returning to `FilesList`. Normalized all changed files to the repository's CRLF policy.

Used Windows app-control visual QA on the running WPF build. Verified the collapsed and expanded command-output layouts, `View` → `Collapse`, Esc collapse plus Files-list focus restoration, Settings/About text rendering, and maximize/restore glyph swapping. The current shell is explicitly a static visual preview; no fake sample element is recorded as production behavior.

Three pieces this session. (1) Applied the owner-confirmed `main` branch governance as an active GitHub repository ruleset. (2) Implemented the production terminal-host boundary: platform-neutral terminal contracts in `Filekin.Core` (`ITerminalHost`, `ITerminalSession`, `TerminalSessionRequest`, size/output/exit types) and a ConPTY-backed implementation in `Filekin.Infrastructure.Windows` (`ConPtyTerminalHost`, `ConPtyTerminalSession`, `PowerShellExecutableLocator`, LibraryImport interop) — PowerShell is the root process; input, raw-VT output, resize, exit notification, and teardown sit behind the boundary; re-verified against the current Microsoft ConPTY documentation. (3) Implemented the `Filekin.Core.Commands` command router: a deterministic classifier + built-in interactive registry, and a router that dispatches app `/` commands, finite runspace commands, and known-interactive terminal launches, and consumes provider-delegation terminal launches. No terminal renderer or WPF surface was added.

Also surfaced a specification conflict about the terminal root process (shell-as-root vs. tool-as-root) — see the new entry under **Product Questions Requiring Owner Decision**.

Follow-up work later on 2026-08-25: reconciled the DECISIONS.md tool-as-root entries as superseded by shell-as-root; installed a machine-wide .NET SDK 10.0.400 so the plain `dotnet` command builds locally; and investigated a CI-only failure in the resize test (root cause and final resolution recorded under **Known Problems** — the resize test now asserts the boundary contract instead of the child's `RawUI`).

Later still on 2026-08-25: implemented the **`/` application-command dispatch** subsystem (`Filekin.Core.Commands.App` + the four core file-operation commands over a new `IFileSystemOperations` port, with `WindowsFileSystemOperations` providing System.IO copy/move and Recycle Bin delete via `SHFileOperationW`). Incorporated the owner's updated UX/decisions/guardrails specs and recorded the owner's **`@` disambiguation** decision (known command-bar references win over PowerShell splatting).

Finally on 2026-08-25: implemented the **`@` reference resolver** (`Filekin.Core.Commands.References` — `ReferenceResolver`/`IReferenceResolver`, `ReferenceContext`, `ReferenceResolution`, `INamedLocationResolver`) with light-touch line resolution that rewrites only recognized `@thisfolder`/`@selection`/named-location tokens (with optional `\subpath`) into PowerShell-quoted paths and passes native `@` syntax through untouched, plus `WindowsKnownFolderLocations` for the built-in known-folder references (`@desktop`, `@documents`, `@downloads` via `SHGetKnownFolderPath`, `@pictures`, `@music`, `@videos`, `@home`). Owner reconfirmed keeping `@` as the reference sigil.

### Tests / Validation
- 2026-08-25 Files-hierarchy wiring: Release build 0 warnings / 0 errors; **88/88** tests passed (64 `Filekin.Core.Tests` — the prior 50 plus 7 `FileTypeCode`, 5 `FileListingSort`, 2 `FileSystemDirectoryLister`; 24 Windows infrastructure); `dotnet format --verify-no-changes` and `git diff --check` clean.
- 2026-08-25 Files-hierarchy wiring: live Windows visual QA via `PrintWindow` capture of the running Release build. Confirmed the real home-folder listing (DIR codes, trailing `/`, real dates), colored clickable path segments, directories-first ordering, the active-column sort caret, item count, and real free-space status. Drove the MODIFIED header twice through UI Automation and confirmed the caret moved to MODIFIED, reversed to descending, and the rows reordered — proving the header-click → view-model → re-sort path end to end.
- 2026-08-25 maximize fix: full Release build passed with 0 warnings and 0 errors; all 74 tests passed (50 Core, 24 Windows infrastructure); formatting verification passed.
- 2026-08-25 maximize fix: live Windows visual QA confirmed the maximized window used the 1536×912 work area at origin 0,0, leaving the taskbar region outside the window. The status bar, file view, command result row, and expanded output remained fully visible in both collapsed and expanded-output states.
- 2026-08-25 WPF recovery: full Release `dotnet build Filekin.sln -c Release --no-restore --disable-build-servers` passed with 0 warnings and 0 errors.
- 2026-08-25 WPF recovery: full Release test suite passed 74/74 (50 Core, 24 Windows infrastructure).
- 2026-08-25 WPF recovery: `dotnet format Filekin.sln --verify-no-changes --no-restore` passed after CRLF normalization; `git diff --check` passed.
- 2026-08-25 WPF recovery: live Windows visual QA passed for normal/expanded output, View/Collapse, Esc collapse and focus restoration, Settings/About label rendering, and maximize/restore glyph switching.
- Production Release build (`Filekin.sln`): passed, 0 warnings, 0 errors.
- Production tests: passed, 74/74 (50 `Filekin.Core.Tests` — 2 product-identity, 10 classifier, 4 router, 6 app-command parser, 4 app-command dispatcher, 11 file-operation commands, 13 reference resolver; 24 `Filekin.Infrastructure.Windows.Tests` — 5 runspace integration, 5 ConPTY terminal-host integration, 5 Windows filesystem operations, 9 Windows known-folder locations).
- Production `dotnet format Filekin.sln --verify-no-changes --no-restore`: passed (exit 0).
- Terminal-host coverage: executable resolution, input→output round-trip through ConPTY, `Resize` accepted by the live pseudoconsole with the session still usable afterward (child `RawUI` reflection is environment-dependent — see the ConPTY resize note under **Known Problems**), one-shot startup command runs with the `-NoExit` prompt remaining, and `exit` ends the root process while raising `Exited`.
- App-command coverage (`Filekin.Core.Tests`, in-memory fakes): parser tokenization (single/double quotes, empty quotes, bare `/`, case-folding); dispatcher unknown-command / bare-`/` / duplicate-registration; and the four file-operation commands — relative-path resolution against the current location, copy-into-directory naming, no-overwrite refusal, missing-source/target errors, argument cardinality, rename rejecting a path as the new name, delete routing to Recycle, and refusal on a non-filesystem location. Windows filesystem-operations coverage (real temp dir): `GetKind`, file copy, recursive directory copy, move, and Recycle Bin delete removing the file from its path.
- Router coverage (in-memory fakes, no real PowerShell/ConPTY): `/` → app command (nothing executed), finite command → runspace execution, known-interactive command → terminal start with launch command/location/title and no runspace execution, and provider-delegation finite result → terminal started for the delegated launch. Classifier coverage: `/` app command, ordinary finite, empty input, always-interactive tools, argument-sensitive `python` vs `python script.py`, and path/extension normalization.
- Branch ruleset verified via the GitHub API: PR review count 1, code-owner review false, unattributed-changes extra approval false, required check `Build, test, and format (Windows)` bound to the GitHub Actions app, deletion/non-fast-forward blocked, owner admin bypass present.

### Known Problems
- The Files listing, path bar, sorting, navigation, and selection are now real. Still static preview: the command row (prompt path, `git status` text, and `✓ Completed · 14 lines` result are hardcoded — note it currently shows `D:\GitHub\filekin` while the live Files path bar shows the real location, so they visibly diverge until step 2), the tab strip, and the sidebar Locations/Surfaces. Do not present those as finished behavior.
- The command bar is not yet an input control; `ShellViewModel.BuildReferenceContext()` exists and returns the live folder+selection, but nothing feeds it into `ReferenceResolver`/`CommandRouter` yet (step 2).
- Selection is not preserved across a re-sort (the listing is rebuilt); navigation clears selection by design. Preserving selection across a header re-sort is a minor refinement if wanted.
- The initial Files location is the user's home folder (`SpecialFolder.UserProfile`). A final startup-location policy (last folder, a default Location, a drive) is unspecified and not yet decided.
- `FileLauncher.Open` swallows launch failures (no association / shell refusal) silently to avoid crashing the shell; a user-visible error path belongs with the command-execution work, not the listing.
- Settings/About, tab close/add, and the `/places` / `/drives` surfaces are still visual composition only. Do not infer missing behavior from the mockup or present it as finished.
- `ConPtyTerminalSession` builds the root command line as `"<pwsh>" -NoLogo -NoExit -Command "Set-Location …; <CommandText>"`. The startup `CommandText` is appended verbatim; commands containing embedded double quotes are out of scope for v1 (known interactive tools are simple tokens). A dedicated argument/quoting model is future work.
- Auto-launching the interactive tool via `-Command` differs slightly from the spike, which launched the child by typing it at the prompt after a readiness marker. The `-Command` path is validated for PowerShell and a benign startup command; it should still be exercised against a real TUI (claude/codex) once a terminal surface exists.
- The output boundary emits raw VT/ANSI bytes only. No terminal renderer, keyboard protocol, scrollback, or accessibility layer exists yet (intentionally out of this task).
- The command classifier tokenizes with a plain whitespace split (matching the spike). It is not quote-aware, so an executable path containing spaces is not parsed as a single token for classification. The raw input is still what the shell/terminal executes; only the interactive-vs-finite decision uses the naive split.
- `InteractiveCommandRegistry` is the minimal built-in v1 set (claude, codex, pwsh, powershell, cmd, ssh; `python`/`python3` interactive only with no args). Broadening the list is deliberately deferred; the registry is isolated from routing so it can grow independently.
- `CommandRouter` builds a basic `tool · folder` tab title. Final title/casing/rename behavior is a UI-layer concern and is not settled.
- The finite shell result contract still captures success/error streams as completed string collections; streaming output, other PowerShell streams, native exit status, and result presentation remain unimplemented.
- `Microsoft.PowerShell.SDK` brings a substantial runtime dependency graph; publishing/trimming/self-contained packaging behavior still needs production validation.
- 2026-08-25 — **ConPTY resize propagation is environment-dependent.** Hard evidence from a diagnostic build on the GitHub-hosted CI runner: after `session.Resize(120×40)` and polling `RawUI` for ~10s, the hosted PowerShell reported `win=80x24;buf=80x24` — the child's window/buffer size did **not** change, even though the native `ResizePseudoConsole` call **succeeded** (`Resize` did not throw; the test reached its assertion). On an interactive desktop the child does observe the resize (width→120 within ~1s). Root cause is the headless runner's ConPTY/console host not delivering the size change to pwsh's `RawUI`, not our Coord mapping (verified correct: `X=Columns, Y=Rows`). Because child-`RawUI` observation cannot be asserted reliably across environments, the earlier width-polling assertion was wrong to require it; `ResizeIsAcceptedAndTheSessionStaysUsable` now asserts only the boundary contract this type owns — the resize is accepted by the live pseudoconsole and the session keeps working afterward. End-to-end resize was already validated on a real desktop by the spike (criterion F). If a production feature ever needs guaranteed child-visible resize, investigate the headless-runner ConPTY delivery (candidate: conhost/OpenConsole under a non-interactive session) rather than re-adding a flaky `RawUI` assertion. (Superseded the earlier "RESOLVED via width polling" note, which passed locally but still failed on CI.)

### Recommended Next Step
The non-UI command-bar core is now complete: the shell runspace, the terminal host, the command router, the `/` application-command dispatch (`Filekin.Core.Commands.App` + four file-operation commands over `IFileSystemOperations`), and the **`@` reference resolver** (`Filekin.Core.Commands.References` + `WindowsKnownFolderLocations`). Two wiring/seam notes for whoever builds the command bar:

- **Resolver wiring is not done yet** (by design): `ReferenceResolver.ResolveLine` is meant to run on raw command-bar input *before* `CommandRouter`/`AppCommandParser`, but it needs a live `ReferenceContext` (current folder + current selection), which only exists once the Files UI does. Wire it at the command-bar entry point: `resolver.ResolveLine(input, context)` → feed the resolved line into the existing router/dispatcher. Do **not** apply it to independent terminal-tab input (CLAUDE.md invariant).
- **Batch `@selection` into file-operation commands**: a multi-item `@selection` expands to several quoted path tokens, so `/copy @selection @dest` becomes `/copy 'a' 'b' 'dest'` (>2 args). The current `/copy`/`/move`/`/delete` handle the documented single source→destination grammar; add N-source batch handling (DECISIONS.md batch philosophy) when wiring selection, or route batches as repeated single invocations.

The Files hierarchy is now a real, asynchronous, virtualized view model that owns the current location and selection and exposes it as `ReferenceContext` (via `ShellViewModel.BuildReferenceContext()`), with keyboard-accessible column-header sorting. 

Exact next step (step 2): replace the static command row with a real single-line input control. On Enter, run `ReferenceResolver.ResolveLine(input, ShellViewModel.BuildReferenceContext())` first, then feed the resolved line into `CommandRouter` + `AppCommandDispatcher`; keep the existing collapsed/expanded finite-output behavior (`View`/`Collapse`, Esc collapses). Do **not** apply `/`/`@` preprocessing to independent terminal-tab input (CLAUDE.md invariant). After a filesystem-mutating command succeeds, re-list the current folder so the hierarchy reflects it. When a `cd`/`Set-Location` changes the runspace filesystem location, drive `ShellViewModel.NavigateToAsync` so Files and the command bar stay in one context (UX-DESIGN.md). The VT-interpreting terminal renderer over `ConPtyTerminalSession.OutputReceived` remains the subsequent terminal-surface task. User-defined Locations plug into the resolver through the same `INamedLocationResolver` port as `WindowsKnownFolderLocations`.

## Evidence / Documentation Sources

Record authoritative sources here when they materially affect implementation or architectural conclusions.

- [Windows PowerShell Host Quickstart](https://learn.microsoft.com/en-us/powershell/scripting/developer/hosting/windows-powershell-host-quickstart?view=powershell-7.6) — official hosted PowerShell SDK/runspace entry point.
- [Creating Runspaces](https://learn.microsoft.com/en-us/powershell/scripting/developer/hosting/creating-runspaces?view=powershell-7.6) — official runspace hosting model.
- [RunspaceFactory.CreateRunspace](https://learn.microsoft.com/en-us/dotnet/api/system.management.automation.runspaces.runspacefactory.createrunspace?view=powershellsdk-7.6.0) — official API surface.
- [Get-Location](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.management/get-location?view=powershell-7.6) — official note that each runspace has its own current directory and that it differs from `Environment.CurrentDirectory`.
- [about_Providers](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_providers?view=powershell-7.6) and [about_Registry_Provider](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_registry_provider?view=powershell-7.6) — provider identity and `HKLM:` semantics.
- [Creating a Pseudoconsole session](https://learn.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session) — official pipe, `STARTUPINFOEX`, process creation, resize, independent drain, and teardown guidance.
- [CreatePseudoConsole](https://learn.microsoft.com/en-us/windows/console/createpseudoconsole), [ResizePseudoConsole](https://learn.microsoft.com/en-us/windows/console/resizepseudoconsole), and [ClosePseudoConsole](https://learn.microsoft.com/en-us/windows/console/closepseudoconsole) — official ConPTY API contracts.
- [Microsoft Terminal MiniTerm C# sample](https://github.com/microsoft/terminal/tree/main/samples/ConPTY/MiniTerm) — Microsoft-owned reference implementation.
- [Microsoft Terminal discussion: redirected parent stdio](https://github.com/microsoft/terminal/discussions/15814) — maintainer explanation and reproduced `STARTF_USESTDHANDLES` workaround for redirected hosts.
- [Microsoft.PowerShell.SDK 7.6.5](https://www.nuget.org/packages/Microsoft.PowerShell.SDK/) — current stable hosting package used by the spike.
- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy) — .NET 10 is the current active LTS release; the production scaffold targets .NET 10.
- [WPF documentation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/) and [What's new in WPF for .NET 10](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100) — official production UI framework baseline.
- [WM_GETMINMAXINFO](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-getminmaxinfo), [MonitorFromWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-monitorfromwindow), and [MONITORINFO](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-monitorinfo) — the custom-chrome window overrides native maximize bounds with the nearest monitor's taskbar-excluding work area. This is required because a `WindowStyle=None` WPF window can otherwise maximize over the taskbar.
- [GNU GPLv3 license text](https://www.gnu.org/licenses/gpl-3.0.txt) — canonical source for the repository `LICENSE`.
- [PowerShell.InvokeAsync](https://learn.microsoft.com/en-us/dotnet/api/system.management.automation.powershell.invokeasync?view=powershellsdk-7.6.0) and [PowerShell.StopAsync](https://learn.microsoft.com/en-us/dotnet/api/system.management.automation.powershell.stopasync?view=powershellsdk-7.6.0) — supported asynchronous invocation/cancellation APIs informing the production boundary.
- [Runspace.SessionStateProxy](https://learn.microsoft.com/en-us/dotnet/api/system.management.automation.runspaces.runspace.sessionstateproxy?view=powershellsdk-7.6.0), [PathIntrinsics](https://learn.microsoft.com/en-us/dotnet/api/system.management.automation.pathintrinsics?view=powershellsdk-7.6.0), and [PathInfo](https://learn.microsoft.com/en-us/dotnet/api/system.management.automation.pathinfo?view=powershellsdk-7.6.0) — direct runspace location inspection and provider/path identity without an extra `Get-Location` pipeline.
- [Source-generated P/Invoke (LibraryImport)](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation) and [SYSLIB1062](https://learn.microsoft.com/en-us/dotnet/fundamentals/syslib-diagnostics/syslib1062) — the ConPTY interop uses `LibraryImport`, which requires `AllowUnsafeBlocks=true` for its generated marshalling; enabled on `Filekin.Infrastructure.Windows` only. The 2026-08-25 re-fetch of "Creating a Pseudoconsole session" confirmed the pipe/`STARTUPINFOEX`/independent-drain/teardown order the production session implements.

## Product Questions Requiring Owner Decision

Record genuinely unspecified user-visible/product/architecture decisions here rather than silently choosing them.

- **Hosted terminal PowerShell profile — decided 2026-08-25.** Default is **load the profile** (`TerminalSessionRequest.LoadProfile = true`), so a hosted tab behaves like the user's real shell; new users are unaffected because a fresh PowerShell has no profile. It becomes a **user setting** (load vs. skip) when the settings system exists, with load remaining the default; a "skip profile" toggle serves users who want a clean, fast, can't-break shell. No code change needed now — the flag already exists. Tests pin `LoadProfile = false` for determinism.
- **Command-bar `@` vs. PowerShell's own `@` — RESOLVED 2026-08-25.** In the Files command bar, a token matching a known workspace reference (`@thisfolder`, `@selection`, a user Location) is always resolved as that reference — even when it would also be valid PowerShell splatting (for example `@selection` read as splatting `$selection`). Only tokens matching no known reference pass through untouched to the shell. A user needing splatting for a colliding variable name uses an independent terminal tab, which gets no `/`/`@` preprocessing. Recorded in DECISIONS.md ("Known Command-Bar References Win Over PowerShell Splatting"). This unblocks the `@` reference resolver.
- **Does the command-bar runspace load the user's PowerShell profile? — open.** Terminal tabs load the profile (decided above), but the persistent command-bar runspace currently does not (it uses `InitialSessionState.CreateDefault2()`, which does not run `$PROFILE`). Decide whether the command bar should reflect the user's profile aliases/functions, or intentionally stay a clean, predictable session. Note that not loading it also reduces the chance of a profile-defined command colliding with `/`/`@` handling.
- **Terminal root process: shell-as-root vs. tool-as-root — RESOLVED 2026-08-25.** `DECISIONS.md` had two stale entries ("Proposed — App-Owned Interactive Terminal Sessions" and "2026-08-24 — Interactive Tool Is the Primary Hosted Process") saying the launched tool is the terminal's primary process. That contradicted `ARCHITECTURE.md`, `ENGINEERING-GUARDRAILS.md`, and the CLAUDE.md invariants, which require **PowerShell as the root process** (tool runs as a child; prompt returns when the tool exits; tab closes when the root shell exits) — the model the shipped `ConPtyTerminalSession` implements. The owner confirmed shell-as-root; both `DECISIONS.md` entries are now marked **Superseded on 2026-08-25** and kept for history. Follow-up: the adjacent "Proposed — Preserve Completed and Failed Terminal Output" section still reflects the tool-as-root worldview (an inactive tab preserving output) and should be revisited against `ARCHITECTURE.md`'s "do not leave behind an exited terminal tab" rule when the terminal renderer/UI is built.

Confirmed by the owner on 2026-08-25:

- Unknown interactive fallback is a one-time fresh **Run in terminal** relaunch. There is no live promotion and no persistent user-defined routing rule in v1.
- Non-filesystem provider delegation creates a fresh ConPTY-backed PowerShell at the requested provider path. Files retains/restores its filesystem runspace location, and arbitrary runspace state is not transferred.
