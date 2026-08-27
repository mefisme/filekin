# Features

## Status Key

- **Confirmed** — currently considered fundamental to the product.
- **Proposed** — worth exploring but not committed.
- **Rejected** — deliberately excluded unless later reconsidered.

## Confirmed

### Shared GUI / Terminal Location

The visual filesystem and shell operate from the same current directory.

### Real Shell

Use a real Windows shell rather than creating a completely fake command environment.

### Semi-Terminal Filesystem UI

The main filesystem interface should share the visual language of a modern terminal rather than traditional File Explorer.

### `/where`

Discover locations and resources related to an application/tool and make those results navigable.

### `/unzip`

Extract one or more ZIP archives without unnecessary outer-directory duplication.

Normal extraction always creates exactly one new folder in the destination: an archive wrapper is
reused, while loose contents receive a folder named after the archive. The preview can explicitly
remove that folder. The grammar is:

```text
/unzip [-noroot] [-skip] [-overwrite] [-y] <archive...> [destination]
```

The destination may be a path, `@thisfolder`, or a saved `@Location`, and need not exist yet. Preview
is the default; `-y` skips it. Existing files are skipped by default, while `-overwrite` recycles the
original before replacement. Each archive is planned and reported independently so one failure does
not block unrelated archives. Version one opens ZIP only and gives recognized unsupported archive
formats a specific error.

The completed operation offers session-scoped Undo from its result line. Undo removes only paths
Filekin wrote and restores originals recycled during replacement.

After extraction starts, Back/Esc dismisses only the archive surface. Work continues in the
background with a persistent command-bar status row exposing View and explicit Stop actions.

### `/zip`

Create a ZIP archive from one or more files or folders:

```text
/zip <item...> [name.zip]
```

`/zip` has no switches. Its default preview controls whether a single source keeps its outer folder
and whether an existing archive is replaced. The shared archive settings can disable previews and
choose Skip or Overwrite as the default collision behavior. A running compression remains visible
and controllable from the command bar after its archive surface is dismissed.

### GUI Selection References

Allow visually selected files to become convenient command targets such as `@selection`.

### Visual Command Results

Commands can use the main UI to display interactive results rather than being restricted to streams of terminal text.

### Unified Reference Syntax

`@` is the single workspace reference syntax for addressable objects such as the current folder, selection, and user-defined Locations.

### Minimal Command Grammar

`/` invokes application actions, `@` references workspace objects, and all other command syntax remains the responsibility of the real shell.


### Minimal Built-In References

Confirmed built-in references:

```text
@thisfolder
@parent
@selection
```

User-assigned Locations automatically become additional `@name` references.

`@last` and redundant aliases are intentionally excluded for simplicity and readability.

### Persistent Process Tabs

Known interactive tools, known long-running processes, and explicit user launches open as persistent terminal tabs. Finite commands remain in the compact command area. Persistent user-defined interactive routing rules are excluded from v1.

If an unknown finite-path command proves to require terminal interaction, Filekin may offer **Run in terminal**, which launches it again as a fresh terminal process rather than migrating the existing process.

The offer appears once, two seconds after a command whose executable is a concrete Windows console target is still running. `Y` stops the runspace invocation and starts the same command again in a hosted terminal tab; `N` or `Esc` leaves it running with an `Esc to stop` status. PowerShell cmdlets and functions are never offered, because they do not resolve to a console image.

### Shell Commands with Workspace References

The Files command bar can resolve `@` references inside ordinary shell commands before execution.

Hosted terminal tabs remain native and are not preprocessed by the application command language.

### Predictable Terminal Shutdown

Running terminal tabs confirm before closing, request graceful process termination first, and force termination only when necessary. Closing the application uses one consolidated warning for all active hosted sessions.

### Workspace Restoration Without Live Process Persistence

Version one may remember lightweight workspace and terminal launch context, but does not keep live terminal processes running across application restarts.

#
### Confirmed Terminal Lifecycle

- Interactive application is the primary hosted process.
- Attached child processes belong to the hosted session boundary.
- Completed sessions remain open with preserved output.
- Failed sessions remain open with preserved output and status.
- Duplicate sessions are allowed.
- Tabs use `TOOL · launch-context` naming.
- External terminals are externally owned.
- Launch-folder changes do not automatically terminate sessions.
- Sleep/hibernate does not count as app exit.

### `/undo`

Version-one command that reverses the most recent safely undoable app-owned filesystem operation.

### `/history`

Version-one visual operation-history view showing what the application changed, when it changed it, and whether each operation remains reversible.

### Command-Bar Recall

Up/Down arrows navigate previously entered Files command-bar commands exactly as typed. This is separate from `/history`.

### Persistent Operation History

`/history` survives app restarts as an informational record. Undo/Restore actions are available only for operations from the current application session.

### Automatic History Retention

Proposed: history should prune itself automatically using a reasonable age and/or entry-count limit, with optional advanced retention settings.

### Rolling 50-Operation History

Expected v1 behavior: retain the most recent 50 app-owned operations automatically. One bulk action counts as one operation regardless of how many files it affects.

No retention controls are required in v1. An exceptional Clear History action may live in Settings.

### Safe Undo Collision Handling

Undo never silently overwrites. Conflicts offer Replace, Keep Both, Skip, or Cancel Undo, plus Apply to All for bulk operations where appropriate. Partial undo results are recorded accurately.

### Windows-Native Normal Delete

Normal app deletion respects Windows Recycle Bin behavior/settings where supported instead of creating a separate app-specific trash system.

### Recycle Bin Workspace View

Recycle Bin is a first-class virtual Files location. Users can browse recycled items, select them with `@selection`, and use appropriate Windows-native actions such as Restore without interacting with the raw `$Recycle.Bin` filesystem structure.

### Virtual Files Locations

The Files workspace architecture can represent non-folder locations where useful, while clearly distinguishing them from physical filesystem paths.

### Narrow Undo Scope

Version-one undo is expected for simple direct app-owned mutations such as move and rename, plus Windows-native delete/restore cases where reliable.

`/tidy` may appear in `/history` but is not undoable. Copy is not guaranteed undoable in v1.
`/unzip` is undoable within the current session because its plan records every path written and every
original recycled during replacement; this supersedes the earlier non-undoable extraction direction.

### Complex-Operation Preview

If `/tidy` is integrated, preview/confirmation is the preferred safety mechanism for its folder-organization changes rather than transactional rollback.

### Deterministic Command Pipeline

The Files command bar distinguishes application `/` commands from ordinary shell input, resolves known workspace `@` references, and deterministically routes known interactive tools into hosted terminal tabs.

### Shell-Compatible Workspace References

Only recognized workspace `@` references are resolved in ordinary shell input. Unknown `@` syntax generally passes through untouched to preserve real shell compatibility.

### Structured Application Commands

Slash commands execute as application-owned behavior rather than PowerShell translations, enabling clearer validation, history, and undo where supported.

### Compact Command Result Indicator

The one-line Files command bar reports the most recent command's status and exposes View/Undo/Open actions where appropriate. The most recent result remains inspectable until the next command is actually executed.

### Workspace Result Views

Larger command output and rich view commands use a closeable main-workspace view rather than expanding the command bar. Closing the view restores the prior Files hierarchy state.

### Single Last-Output Model

Version one retains only the most recent finite-command output for immediate inspection. Its View affordance remains available while the user types/edits the next command and is replaced only when that next command is executed.

There is no multi-result shell-output buffer or persistent shell transcript.

### Human-Readable Files Views

Rich commands open shallow temporary Files workspace views labeled in plain English, such as `Files · History`, `Files · Where — python`, and `Files · Disk`.

The underlying folder state is preserved for simple Back navigation. Temporary rich views do not recursively stack in v1.

### Stable Filesystem Selection Semantics

`@selection` always means the selected filesystem item(s) in the underlying Files context. Rich views do not create competing selection semantics.

History entries use explicit Details/Undo/Restore controls. Where/Disk results use explicit Open/Go to navigation actions when they need to lead back into the filesystem.

### Rich-View Command Context

The command bar remains usable while a rich view is open. `@thisfolder` and `@selection` continue to resolve against the preserved underlying Files state.

### Strong Keyboard Navigation

Core Files and rich-view workflows support both mouse and keyboard operation. Rich views use conventional Arrow/Tab/Enter/Esc navigation and focus explicit controls without redefining `@selection`.

### Distinct Focus and Selection States

Filesystem selection, rich-view control focus, and command-bar focus are visually and behaviorally distinct.

### Space-to-Command

Pressing Space from a neutral Files or rich-view surface focuses the command bar immediately. Normal Space behavior is preserved in editable fields and controls that use it.

### `/run`

`/run` provides a simple app-native way to launch files and executable targets while preserving normal shell execution for power users.

Relative targets resolve from the current Files location:

```text
/run tool.exe
```

References can be used explicitly or composed with child paths:

```text
/run @selection
/run @projects\tool.exe
/run @thisfolder\tools\helper.exe
```

A relative target is looked for in the visible Files folder first, then through the ordinary Windows `PATH` and `PATHEXT` lookup, so a PATH-installed entry point runs by its bare name. `/run` never enumerates or crawls installed applications.

Where the target runs is decided from file metadata before the process is created:

- console programs and `.bat`, `.cmd`, `.com`, `.ps1`, and `.py` files start in a hosted Filekin terminal tab;
- GUI applications, shortcuts, and associated documents launch independently through Windows;
- a folder is refused with a clear message, because Files owns folder navigation.

Arguments are supported for a single target. `/run @selection` may launch several targets, and arguments are refused in that case because they cannot be attributed.

`/run` is the only launch command; there is no `/open`. `/ext` remains separate: it launches an **external** terminal or an explicitly independent external process.

### Uninterrupted Shell Pathing

Raw path and shell syntax retains normal PowerShell behavior. The app adds convenience through `/` actions and `@` references rather than redefining native path semantics.

### Pluggable Shell Architecture

The Files command bar is designed around a shell-backend adapter so additional shells can be supported later without redesigning the workspace language.

Version one ships with PowerShell as the guaranteed backend. `/` actions and `@` references remain app-owned and shell-independent.

### Per-Tab Files Navigation History

Each Files tab maintains its own Back/Forward filesystem-location history.

Rich views are excluded from that history. Back/Esc dismisses a rich view, Forward never restores it, and Up remains parent-directory navigation only.

### Windows-Familiar File Interaction

Single click selects and double-click/Enter uses the Windows-defined/default Open behavior.

### Compact Context Menu

The primary right-click menu stays deliberately small:

```text
Open
Rename
Copy
Cut
Copy Path
Delete
Properties
```

The app avoids reproducing the full Explorer context menu. Advanced capability remains readily available through keyboard shortcuts and the command bar.

### Reference Autocomplete

Readable references such as `@thisfolder` remain canonical while autocomplete makes them fast to enter. Pressing Tab on `@` or a partial token surfaces built-in and user-defined Location references with their resolved destinations.

### Stable Multi-Selection Semantics

`@selection` always represents the complete filesystem selection.

Commands validate cardinality and target type instead of changing what `@selection` means.

Examples include multi-target `/run` and `/info`, single-query `/where`, context-only `/history`, and type-restricted `/unzip`.

### Core File Operation Commands

The Files command bar supports direct app-owned file manipulation:

```text
/copy   <source> <destination>
/move   <source> <destination>
/rename <target> <new-name>
/delete <target>
```

`/copy` is immediate filesystem copy, not clipboard copy. `/delete` respects Windows Recycle Bin behavior where supported. `/paste` is not required because clipboard actions remain available through standard shortcuts.

### `/info`

A rich filesystem inspection command for files, folders, and selections.

```text
/info
/info @selection
/info @thisfolder
```

Bare `/info` describes the current selection, or the visible folder when nothing is selected.

Core information includes type, path, size, created/modified dates, plus relevant type-specific metadata:

- executables: architecture, product, version, and the **Company** name written inside the file — never called "Publisher", because Filekin does not verify signatures;
- images: pixel dimensions;
- audio and video: duration;
- text: encoding, with line count on demand;
- shortcuts: target, arguments, and start-in folder, shown but never edited.

Type-specific metadata is read through the Windows Property System rather than per-format parsers, so a format Windows understands is a format Filekin can describe.

Folders and multi-selections show aggregate size and item counts. The scan runs off the UI thread, updates the rows while it works, never follows junctions or symlinks, reports when a folder refused access instead of hiding it, and stops when the view closes.

Expensive metadata is on demand: SHA-256 and line count each wait for an explicit action. Native Windows Properties remains available for permissions, signatures, and other deep system details on a single target.

### `/places`

A deliberately short temporary rich view for the common Windows folders Desktop, Documents, Downloads, Pictures, Music, and Videos, when they resolve, followed by cloud-storage sync roots registered for the current Windows user.

The user-profile/Home folder is intentionally not a Place. Cloud entries use the provider/account name and path supplied through Windows rather than a hardcoded vendor list or guessed folder names. A provider mounted as a drive belongs in `/drives` instead.

It keeps these system destinations quickly accessible without permanently filling the personalized Locations sidebar.

### `/drives`

A temporary rich view of assigned filesystem drives/volumes. Each row provides the root, volume label, drive type, free/total space, and a restrained usage bar when capacity is available. Assigned drives that are disconnected or have no media remain visible but disabled with a concise status.

Places and available drives are pure navigation targets: a single click or Enter navigates the current Files tab to the target. Unavailable drive rows do not navigate.

## Proposed Terminal Lifecycle Details

- Preserve completed and failed terminal output until the user closes the tab.
- Treat interactive applications as the primary hosted process rather than automatically falling back to a hidden shell.
- Allow duplicate sessions.
- Name tabs from tool + launch context.
- Treat externally launched terminals as externally owned.
- Do not kill sessions merely because their original launch folder changes.
- Treat child processes as part of the hosted session boundary where Windows process semantics allow it.

## Proposed

### Terminal Application Tabs

Interactive CLI applications can open in persistent terminal tabs rather than taking over the compact filesystem command area.

Sessions inherit the current filesystem location.

### Terminal Panes

Allow terminal tabs to split into multiple persistent terminal sessions, particularly useful for simultaneous tools such as coding agents and development servers.

### Preferred External Terminal

Allow users to choose whether interactive terminal applications open embedded or in their preferred external terminal.

External terminals should launch at the current filesystem location.

### Contextual Terminal Session Names

Label terminal sessions according to both process/tool and filesystem context, for example:

```text
CODEX · MyApp
CLAUDE · Website
DEV SERVER · API
```

### Filesystem Activity Indicators

Directories may display active terminal applications associated with them and provide direct navigation back to those sessions.



### Agent Relay Mailbox

A small file-based channel that lets two coding agents in different terminal tabs trade work without a person moving text between them.

The mailbox is an app-owned file in the workspace, for example:

```text
.filekin\relay.json
```

An agent completes a stretch of work, writes a handoff note and a `to` field, then stops. Filekin watches the file, resolves `to` to a terminal tab, and injects a short continuation prompt into that tab.

Turn-taking is explicit and recorded. Filekin does not guess that an agent is finished by watching its output, because hosted TUI programs redraw their screens continuously.

The mailbox is cooperative. Agents must agree to write it. Filekin owns delivery, tab resolution, and the visible turn state. It does not own the agents' internal behavior.

### Agent Turn Indicator

The ACTIVE section and terminal tab names show which agent holds the turn, which agent waits, and when the last handoff happened.

### Agent Budget Watch

Filekin tracks how much of an agent's rate-limit window is consumed and starts a handoff before that window ends.

The goal is continuous work across the combined windows of two agents. If each agent has its own five-hour window, an automatic relay near the end of each window gives approximately ten hours of unattended progress on one task.

Two budget sources, in order of preference:

1. **Self-reported.** The agent writes its own remaining budget into the relay mailbox. This is reliable and independent of tool versions.
2. **Screen-read.** Filekin owns the ConPTY cell grid, so it can send a tool command such as `/usage` into the hosted tab and parse the rendered result. This needs no agent cooperation, but it depends on the tool's output format and must fail quietly when that format changes.

When the consumed percentage crosses a user-set threshold, Filekin asks the active agent to stop cleanly, write its handoff, and pass the turn to the configured partner. Filekin does not terminate the process to force a handoff, because a forced stop produces no usable handoff.

Thresholds, the partner agent, and whether the relay runs at all are explicit user settings. Automatic relay is off by default.

### Filekin MCP Server

Filekin can expose its workspace to external agents through an MCP (Model Context Protocol) server, so an agent can read and act on the workspace instead of only running inside a terminal tab.

Candidate surface:

```text
current Files location
@selection and user-defined Locations
/where, /info, /drives, and /places results
terminal tab list and status
send input to a named terminal tab
```

This is a real security boundary. It needs explicit opt-in, a visible indicator of connected clients, and a scoped allow list for every capability that writes to disk or into a terminal tab. Read-only capability ships before write capability.

The MCP server is independent of the relay mailbox. The mailbox handles agent-to-agent turn-taking. MCP handles agent-to-Filekin control. Either can exist without the other.

### `/tidy` Integration

Integrate or expose an existing file-organization utility.

### Folder Sizes

Show directory sizes directly and allow quick size analysis.

### Disk Analysis

A command/view for understanding where disk space is being consumed.

### Duplicate Detection

Find actual duplicate files safely.

### Recent Locations

Treat navigation history as a useful first-class feature rather than requiring everything to be permanently pinned.

### Archive Preview

Inspect archive structure before extraction.

### Command Palette

Allow users to discover actions visually and see the command that corresponds to each action.

### Dual Pane

Optional second filesystem pane for transfer/comparison workflows.

### Drag-to-Reference

Dragging a file into the command area could produce a short reference rather than requiring a long escaped path.

### Explain Item

Explain unfamiliar files, folders, extensions, caches, system items, and development artifacts.

### Destructive Action Context

Before deleting certain items, show useful information such as size, whether it is regeneratable, and what may depend on it.

### Git Awareness

Show useful repository state without attempting to become an IDE.

### Extensible Slash Commands

Allow additional utilities to be installed and exposed through `/command` syntax.

## Rejected / Avoid

### Traditional Explorer Clone

Do not build File Explorer with cosmetic changes and a terminal bolted underneath.

### Fake Shell

Do not require users to learn an entirely proprietary replacement for PowerShell.

### AI for Every Operation

Routine filesystem actions should remain deterministic.

### Hacker-Novelty Styling

The interface should not depend on retro CRT/Matrix aesthetics.

## Newly Confirmed Navigation Features

### User-Assigned Locations
The sidebar primarily contains locations explicitly assigned by the user.

The sidebar `+` adds a Location. Existing entries can be edited or removed. Removing a Location removes only the saved pointer, never its folder.

Keyboard users manage the same collection through:

```text
/location add projects @thisfolder
/location set projects D:\Work\NewProjects
/location rename projects client-work
/location remove client-work
```

`set` changes only the saved destination of an existing Location.

### Location Aliases
Assigned locations may have short names and command references such as `@projects`.

### Transient Navigation Commands
Use `/recent`, `/drives`, and `/places` instead of permanent default sidebar sections.

### Sparse Active Sessions
A compact ACTIVE section may show running terminal applications associated with filesystem locations.

## Confirmed Product Systems

### Filesystem
- Visual navigation with full mouse interaction.
- Sparse user-assigned Locations.
- Location aliases and context references.
- Selection references such as `@selection` and `@thisfolder`.
- Direct folder-size visibility/analysis.

### Command Layer
- Real shell access remains available for power users.
- `/` application commands coexist with normal shell commands.
- `@` references provide human-readable filesystem shorthand.
- Slash-command discovery and autocomplete are confirmed.
- Visual command results are supported for application commands.
- The command language should remain small and composable.

### Terminal Workspace
- Persistent interactive CLI tabs.
- Split terminal panes.
- Preferred external-terminal support.
- Contextual session names such as `CODEX · MyApp`.
- Active process/session awareness tied to filesystem locations.

### Utilities
- `/where` for application/tool locations and related resources.
- `/unzip` with redundant-root handling and safe extraction preview where needed.
- `/tidy` integration.
- `/disk` for visual disk/folder usage analysis.
- `/recent` for transient recent-location navigation.
- `/places` for standard Windows locations.
- `/drives` for connected drive navigation.
- Archive preview as part of safe extraction workflows.

### Safety and Recovery
- Collision handling for filesystem operations.
- Operation previews when an action is ambiguous or potentially destructive.
- Undo for supported filesystem operations.
- Operation history sufficient to understand and reverse supported recent actions.

### Intelligence
- AI-assisted filesystem interpretation is confirmed as a capability where interpretation adds value.
- AI is not required for deterministic filesystem operations.
- The exact interface for AI interpretation remains undecided.

## Still Proposed / Unresolved

- Dual-pane **file browsing**. Terminal split panes are confirmed; a second file-browser pane is not.
- Arbitrary numbered drag references such as `@1` and `@2`.
- Git-aware file metadata/integration.
- Exact AI commands such as `/explain`.
- Deep plugin/extension architecture for third-party slash commands.
- Exact syntax for context references beyond the confirmed `/location add|set|rename|remove` management command.
- Whether the agent relay mailbox and the Filekin MCP server belong in version one at all.
- Whether `/usage`-style screen reading is dependable enough to drive an automatic handoff, or whether a self-reported budget is required.
- The relay file format, its location, and whether it is per-workspace or per-application.

### Focused Command/Reference Completion

Autocomplete is intentionally limited to app-owned `/` commands and known `@` references. `/hi` + Tab can complete `/history`; `@thi` + Tab can complete `@thisfolder`.

Typing alone does not open a list. Tab requests completion; an ambiguous prefix opens a compact described suggestion overlay, while a unique match completes directly.

Ordinary shell input keeps shell-native completion behavior. Version one does not add custom Tab cycling through current-folder files.

### `/recent` — Not Version One

No `/recent` command ships in v1. Existing navigation and discovery mechanisms should be used first; a recent-work feature may be reconsidered later if real usage shows a gap.

### `/disk` — Not Version One

No `/disk` command ships in v1. `/drives` provides drive capacity/free-space information, while `/info` provides target/folder/selection size inspection.

Whole-drive storage analysis is deferred.

### `/interactive` — Not Version One

No `/interactive` command ships in v1.

Interactive CLI detection and routing remain built into the terminal/session architecture so users can launch known tools naturally without managing process classifications themselves.

Version one does not store user-defined interactive routing rules. An unknown command that proves interactive may be relaunched once through **Run in terminal** without creating a saved rule.

### Fast Tidy Execution

`/tidy` executes immediately without a mandatory confirmation step.

```text
/tidy @downloads
→ organize
→ ✓ Tidied 47 files · 2 skipped    View
```

Safety comes from conservative organization rules rather than an extra click. The optional rich result explains what happened afterward.

### Partial-Success Batch Operations

Batch commands continue processing independent valid targets when other targets encounter conflicts.

```text
9 moved
3 need attention
```

Successful work remains completed. Back/Esc from the conflict view skips unresolved targets rather than reversing completed work.

The final compact result remains inspectable through `View`.

### File Collision Handling

Explicit `/copy` and `/move` operations resolve destination-name collisions with:

```text
Replace
Keep Both
Skip
```

`Keep Both` creates a safe unique name automatically. Batch operations can apply a chosen action to remaining compatible collisions.

`/tidy` remains interruption-free: collisions are skipped and reported rather than prompting during cleanup.

### Privilege Handling

The app and PowerShell run with standard privileges by default.

App-owned operations that encounter protected targets can offer `Retry as administrator` through normal Windows UAC without stopping unrelated batch work.

Advanced settings may allow power users to start an explicitly elevated PowerShell session. A persistent Admin indicator makes that state visible.

Slash commands retain app-owned safety semantics regardless of shell privilege.

### Locked and Read-Only Files

Locked/in-use targets become `Retry` / `Skip` attention items. Files does not force-unlock them or kill owning processes.

Read-only files work normally for non-modifying operations. When an app-owned action genuinely needs to modify, replace, or delete a read-only target, the user gets `Continue` / `Skip`.

Network authentication, advanced ACLs, and Windows security remain owned by Windows rather than being recreated inside Files.

### Intelligent Task Delegation

Short filesystem operations stay lightweight in the command bar.

Long-running copy, move, unzip, tidy, or exceptionally large delete operations may automatically receive a dedicated task tab with progress, controls, and accumulated conflicts.

Inspection/search commands continue updating their rich views, while long-running `/run` processes use terminal/process tabs.

Users do not need to choose a background mode manually.

### Desktop Technology

Version one uses C# + modern .NET + WPF.

WPF provides the application foundation but does not determine the visual design. The product uses a custom modern terminal/developer-tool aesthetic rather than stock WPF styling.

Potentially expensive filesystem/process work runs asynchronously or in background services, and large file views use virtualization to keep interaction responsive.

### Windows-Native Reliability Under a Custom Interface

The product uses .NET for ordinary filesystem work and selective Windows APIs for operating-system behavior such as Recycle Bin, file associations, known folders, UAC, and Windows Properties.

None of that dictates the visual design. Files keeps its custom terminal-leaning WPF interface.

### Engineering Guardrails

Implementation must avoid speculative features, generic AI-style dashboards, unnecessary abstractions, dependency bloat, swallowed errors, and fake-complete functionality.

The goal is a small, reliable Windows application that implements the agreed behavior directly.

### Files-Synchronized Command Bar

The Files command bar always runs in the current Files location.

Navigating Files changes the command bar's shell working directory so the visible folder and shell context stay aligned.

### Independent Terminal Tabs

Launching `powershell` or another recognized interactive shell/tool creates a hosted terminal tab that starts from the current Files location and then keeps its own independent session and working directory.

### Persistent PowerShell Command-Bar Session

The Files command bar uses a persistent PowerShell runspace so ordinary PowerShell state can persist between commands.

Filesystem `cd` / `Set-Location` commands can move the visual Files location, while Files navigation keeps the runspace location synchronized.

### Real Terminal Tabs Through ConPTY

Interactive shells and CLI/TUI programs use independent ConPTY-backed terminal tabs rather than the finite command-bar output model.

Non-filesystem PowerShell-provider navigation delegates to a fresh ConPTY-backed PowerShell terminal initialized at the requested provider location. The Files runspace stays at its prior filesystem location and does not transfer arbitrary session state into the terminal.

### Files/Shell Location Lockstep

The Files hierarchy and its command-bar shell context always stay synchronized to the same filesystem location.

PowerShell navigation to non-filesystem providers such as `HKLM:\` is delegated into an independent terminal tab rather than breaking that synchronization.

### Shell-Owned Terminal Tabs

Terminal tabs use PowerShell as the root shell and launch interactive tools inside it.

A tool launched from Files starts in the current Files directory. After the terminal opens, it is independent.

When the tool exits, the PowerShell prompt remains. When PowerShell itself exits, the terminal tab closes.

Tool-created tabs use intent-oriented names such as `Claude · App` or `Codex · Website`.

### Workspace Surface System

Files uses three clear surface families: the filesystem hierarchy, temporary command-driven rich views, and persistent task tabs.

Rich views and task tabs share a visual language and reusable presentation primitives, while the filesystem hierarchy remains visually distinct. Task tabs mirror rich-view styling but retain their own persistent operation lifecycle.

### Inspectable Settings and Locations

User preferences and saved Locations live in a readable `settings.json` under the application's named AppData folder.

Advanced users can inspect, edit, copy, and back up this file.

### Settings Surface

Settings opens as a rich view over the preserved Files workspace, from either the sidebar footer entry or the `/settings` command. A category rail holds one panel each:

```text
Appearance   theme and accent colour
Startup      Open Files at launch
Terminal     which programs open in a terminal tab
Archives     preview and existing-file behavior
Advanced     the readable settings file itself
```

Every choice is applied and written immediately. There is no Save button and no unsaved state; a write that fails reports the reason inline and leaves the previous value in force.

### Theme and Accent

Filekin offers **Dark**, **Light**, and **Follow system**. Dark is the default. Follow system takes light or dark from the Windows app-mode preference and follows it as that preference changes.

A theme changes colour and nothing else — never a font, spacing, or layout. This includes a hosted terminal, whose ground and default text follow the theme so a terminal tab is never a dark panel inside a light window.

The accent colour is user-selectable: **Blue** (default), Teal, Green, Orange, Pink, and Purple. Each has a shade tuned for a dark ground and one for a light ground. The accent colours the spark, the directory names in the listing, and the terminal caret. It never replaces the semantic status colours, which stay reserved for success, warning, and failure.

### User-Registered Interactive Programs

The built-in interactive rules cover AI coding agents, explicit shell launches, SSH, and the Python REPL. Settings lets the user add their own program names — `vim`, `htop`, an in-house tool — so those open in a terminal tab instead of running as a single command.

User rules add to the built-in ones and can never remove one. Built-in rules are listed so the user can see what is already covered.

### Startup Files Location

Filekin opens the Files workspace at the current user's profile folder by default. A setting can instead select any saved `@Location` or an explicitly chosen filesystem folder. Selecting a saved Location keeps startup aligned when that Location's path changes.

If the configured target is missing or temporarily unavailable, Filekin opens Home for that launch, reports a small non-blocking notice, and preserves the preference for a later launch. This setting controls Filekin only; it does not edit PowerShell profiles or change the startup behavior of external shells.

Operation history and undo metadata use a small embedded SQLite database for reliable transactional storage.

### Native Tidy Engine

`/tidy` is rebuilt directly inside the C# application rather than calling the old standalone utility.

The new engine keeps only the confirmed Files behavior: deterministic organization of loose files in a selected folder.

Legacy Desktop icon rearrangement is intentionally excluded.

### Installer and Portable Releases

Version one ships in two forms:

```text
Traditional Windows installer
Portable ZIP
```

Both are self-contained and do not require users to install .NET separately.

### User-Controlled Updates

The app may notify users about newer versions, but users decide whether to update now or later.

Installed builds can update through a newer installer. Portable builds can download/open the newer portable release.

Microsoft Store distribution and paid code signing are not required for v1.

## Product Name

These features belong to **Filekin**, a keyboard-first Windows file manager + terminal.

References to `Files` in this specification describe Filekin's visual filesystem workspace, not the product name.
