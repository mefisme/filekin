# Architecture

## Status

Living system-design document.

This document defines the major architectural boundaries of the application without prematurely locking the project into a specific programming language, UI framework, terminal library, or packaging technology.

The purpose is to make clear **what each part of the system owns**, how the major subsystems communicate, and which questions still require product or technical decisions.

## Architectural Goals

The architecture should preserve the product principles already defined in `PRODUCT.md`:

- `/` means a built-in workspace action.
- `@` means a workspace reference.
- Everything else remains real shell input.
- The interface teaches its language through use.
- GUI navigation and terminal state describe the same filesystem context.
- Filesystem operations should be deterministic.
- AI should assist interpretation rather than become a required execution layer.
- The application should remain simple even as capability grows.

A major architectural objective is to prevent the project from turning into one monolithic component that directly manipulates UI, files, terminal processes, and command parsing at the same time.

## High-Level System

```text
APPLICATION SHELL
├─ Filesystem View
├─ Locations Sidebar
├─ Command Bar
├─ Visual Command Results
└─ Terminal Tabs / Panes

COMMAND LAYER
├─ Input Classifier
├─ Slash Command Router
├─ @ Reference Resolver
└─ Real Shell Passthrough

FILESYSTEM SERVICE
├─ Navigation
├─ File Operations
├─ Directory Watching
├─ Search / Metadata
├─ Archive Operations
└─ Undo / Operation Journal

TERMINAL SERVICE
├─ Shell Sessions
├─ Interactive CLI Sessions
├─ Tabs / Panes
├─ Process Lifecycle
└─ External Terminal Launch

UTILITY MODULES
├─ /where
├─ /unzip
├─ /tidy
├─ /disk
├─ /recent
├─ /places
└─ /drives

INTELLIGENCE LAYER
└─ Optional interpretation and assistance
```

## 1. Application Shell

The Application Shell owns the visible workspace and the relationship between visual state and system state.

It should not directly implement filesystem mutation logic or shell execution.

### Responsibilities

- Render the current filesystem location.
- Render user-assigned Locations.
- Render selection state.
- Render the compact command bar.
- Render visual results for application commands.
- Host persistent terminal tabs and panes.
- Show active-session indicators.
- Coordinate navigation between filesystem views and terminal sessions.
- Expose user actions to the appropriate underlying service.

### Principle

The UI should ask services to perform work rather than performing low-level filesystem or process operations itself.

## 2. Command Layer

The Command Layer is the boundary between what the user types and what the application actually executes.

Its primary job is to classify input and route it correctly.

### Initial Input Classes

```text
/command ...     → built-in workspace command
shell command    → real shell
interactive CLI  → real shell session that may become a persistent terminal tab
```

`@references` may appear inside either application commands or shell commands.

Example:

```text
/unzip @selection @thisfolder
git -C @projects status
```

### Responsibilities

- Detect slash commands.
- Resolve `@` references.
- Preserve quoting and path safety.
- Pass ordinary commands to the real shell.
- Determine when a command requires a persistent interactive terminal session.
- Return structured results for built-in commands when visual rendering is appropriate.

### Interactive CLI Routing — Confirmed Direction

Known interactive applications should open **immediately** in persistent terminal tabs rather than first taking over the compact command area.

Interactive routing uses a predictable registry model rather than aggressive runtime guessing.

Resolution order:

```text
1. Built-in known-interactive rules
2. Explicit user launch choice
3. Otherwise execute normally in the compact shell
```

The built-in registry should cover common interactive CLI/TUI applications. The exact initial list should be researched before implementation and maintained independently from the core routing logic.

Version one does not include persistent user-defined interactive routing rules or an `/interactive` command. That capability may be reconsidered later.

#### Rules May Consider Arguments

An executable name alone is not always sufficient.

For example:

```text
python
```

may be interactive, while:

```text
python script.py
```

is normally a finite shell command.

Similarly, a CLI may have both interactive and print/noninteractive invocation forms.

The registry should therefore be capable of representing simple argument-sensitive rules without becoming a general scripting language.

Conceptually:

```text
python
  interactive when: no script or command supplied

tool
  interactive by default
  noninteractive when: known print/output-only flags
```

#### Unknown Commands

Unknown commands should default to normal shell execution.

The application should not attempt to classify every process using unreliable heuristics.

If an unknown command proves to require terminal behavior, Filekin may offer **Run in terminal** after the finite attempt ends or is stopped. The action launches the command again as a fresh process inside a new ConPTY-backed PowerShell terminal. The already-running process is not migrated or promoted.

#### Architectural Principle

> Interactive behavior should be predictable and correctable.

The built-in registry provides deterministic v1 routing for known interactive tools, including supported CLI AI agents. Unknown commands retain a clear finite default and a fresh terminal-relaunch escape hatch.

## 3. @ Reference Resolver

The reference resolver converts workspace shorthand into concrete filesystem or selection context.

Potential initial references:

```text
@thisfolder
@selection
@parent
@projects
@downloads
```

User-defined Location aliases should become references automatically.

### Responsibilities

- Resolve aliases to canonical paths.
- Expand selection references safely.
- Support references inside slash commands.
- Support references inside shell commands where resolution is unambiguous.
- Quote or escape paths correctly for the active shell.
- Reject ambiguous or invalid references with a clear explanation.

### Important Constraint

The reference system should remain small and understandable.

Avoid creating a second programming language.

## 4. Filesystem Service

The Filesystem Service owns deterministic interaction with the filesystem.

The UI and utility commands should use this service rather than manipulating files independently.

### Responsibilities

- Navigate directories.
- Enumerate files and folders.
- Copy.
- Move.
- Rename.
- Delete through appropriate Windows behavior.
- Create files/directories where supported.
- Gather metadata.
- Calculate folder sizes.
- Watch directories for outside changes.
- Coordinate archive extraction.
- Record reversible operations in the operation journal.

### Principle

Filesystem state may change outside the application at any time.

The visual view must therefore observe the real filesystem rather than assume that only this application modifies it.

## 5. Undo and Operation Journal

File operations performed by the application should be recorded where practical.

Potential operations:

- Move
- Rename
- Tidy
- Extraction
- User-triggered bulk operations
- Some delete operations when routed through the Recycle Bin

### Responsibilities

- Record what changed.
- Store enough information to reverse supported operations.
- Expose operation history.
- Clearly mark non-reversible operations.
- Prevent `/undo` from pretending it can safely reverse an operation when it cannot.

### Open Questions

- How long should history persist?
- Does history survive application restart?
- How are collisions handled during undo?
- Should shell commands be journaled, or only application-owned operations?

Current direction: only operations the application itself owns should be guaranteed by the undo system.

## 6. Terminal Service

The Terminal Service owns shell processes and interactive CLI sessions.

### Responsibilities

- Start a real Windows shell.
- Maintain working directory state.
- Host persistent terminal sessions.
- Support terminal tabs.
- Support split terminal panes.
- Track running processes.
- Label sessions contextually.
- Open a preferred external terminal.
- Decide what happens when a user closes a tab containing a running process.

Example session labels:

```text
CODEX · MyApp
CLAUDE · Website
DEV SERVER · API
POWERSHELL · Downloads
```

### Architectural Constraint

A proper embedded terminal is not equivalent to displaying process stdout in a text box.

Interactive applications may require:

- Pseudoterminal support.
- ANSI rendering.
- Unicode handling.
- Resizing.
- Keyboard sequences.
- Full-screen terminal applications.
- Process groups and child processes.

The terminal subsystem should therefore remain isolated behind a well-defined interface.

## 7. Utility Modules

Built-in slash commands should ideally behave like modules using shared application services.

Example:

```text
/where
/unzip
/tidy
/disk
/recent
/places
/drives
```

A command should not bypass shared filesystem, terminal, safety, or undo systems simply because it is implemented as a utility.

### Potential Command Contract

A utility may declare:

- Command name.
- Arguments.
- Description.
- Whether it mutates files.
- Whether it supports preview.
- Whether it supports undo.
- Whether it produces structured visual output.
- Whether it needs filesystem, terminal, or intelligence services.

This may later become the basis for extensible slash commands.

## 8. Visual Command Results

Built-in commands may return structured data rather than plain terminal text.

Examples:

- `/where python` returns categorized locations.
- `/disk` returns disk-usage data.
- `/recent` returns navigable recent paths.
- `/unzip` may return an extraction preview.

The Application Shell can render these results interactively.

### Principle

Shell output remains authentic shell output.

Only application-owned commands are expected to provide custom visual representations.

## 9. Intelligence Layer

AI assistance should remain optional and isolated from core filesystem execution.

Potential uses:

- Interpret fuzzy `/where` requests.
- Explain unfamiliar files or folders.
- Suggest readable rename patterns.
- Help identify likely application-related locations.
- Translate a natural-language request into an existing deterministic command.

### Constraint

AI should not be the only layer capable of:

- Moving files.
- Deleting files.
- Extracting archives.
- Resolving filesystem collisions.
- Running the undo journal.
- Managing shell processes.

The deterministic service owns execution.

## 10. Windows Integration Layer

The application will eventually require Windows-specific integration.

Potential areas:

- Recycle Bin behavior.
- File associations.
- Open-with behavior.
- Drag and drop.
- External terminal discovery.
- Shell/process APIs.
- Known Windows folders.
- PATH and registry inspection for `/where`.
- Pseudoterminal support.
- Explorer fallback.
- Elevation when explicitly required.

Exact APIs and framework choices remain unresolved.

## 11. Performance Boundaries

Some operations must be asynchronous and cancellable.

Examples:

- Folder-size calculation.
- Deep `/where` scans.
- Disk analysis.
- Duplicate detection.
- Large directory enumeration.
- Archive inspection.
- Large filesystem searches.

These operations must not block the UI thread.

The architecture should support:

- Progress reporting.
- Cancellation.
- Partial results where useful.
- Caching where safe.
- Invalidating cached data when filesystem state changes.

## 12. Security and Safety Boundaries

The application combines a file manager and real shell, so execution boundaries must remain explicit.

Important areas include:

- Archive path traversal.
- Dangerous overwrite behavior.
- Shell argument escaping.
- `@` reference expansion.
- Privilege elevation.
- Symbolic links and junctions.
- Reparse points.
- Network paths.
- Running untrusted executables.
- Undo assumptions.

The application should not imply that arbitrary shell commands are safe merely because they were entered through this workspace.

## 13. Extension Direction

Long term, slash commands may become extensible.

Possible model:

```text
Command Module
├─ metadata
├─ argument schema
├─ execution handler
├─ visual result schema
└─ declared capabilities
```

This is an architectural goal, not yet a confirmed implementation model.

The first version may keep commands built in while preserving boundaries that allow later extraction into modules.

## Architecture Questions to Resolve

The next discussions should address these one at a time:

1. **Command routing and interactive CLI detection**
2. `@` reference semantics
3. Terminal session lifecycle
4. Undo/history behavior
5. Filesystem watching and synchronization
6. Slash-command module architecture
7. Windows integration boundaries
8. Performance and background operations
9. Security/elevation behavior
10. Technology stack

## Resolved Topic 1: Command Routing and Interactive CLI Detection

The initial routing model is now decided:

```text
/command ...         → application slash-command router
ordinary command     → real shell in compact command area
known interactive    → immediate persistent terminal tab
user-marked tool     → immediate persistent terminal tab
unknown command      → real shell by default
```

`@` references may be resolved before either application-command execution or shell execution.

Runtime heuristics are not the primary classification mechanism.

## Resolved Topic 2: `@` Reference Semantics

The next architecture discussion should define:

- The smallest useful built-in reference set.
- How user Location aliases become references.
- Whether references can represent one item versus many items.
- How `@selection` expands inside real shell commands.
- Quoting and escaping behavior.
- What happens when a reference is ambiguous or unavailable.
- Whether commands can declare which kinds of references they accept.

### Reference Semantics — Confirmed Foundation

Architecturally:

```text
/ = ACTION
@ = REFERENCE
everything else = REAL SHELL
```

`@` identifies an addressable object already known to the workspace. It is not intrinsically a filesystem-folder sigil.

Initial reference categories may include:

- Current filesystem location: `@thisfolder`
- Parent directory: `@parent`
- Current selected item or items: `@selection`
- User-assigned Location aliases: `@projects`, `@downloads`, etc.

Future workspace objects may also be referenceable if doing so remains intuitive, but `@` must not execute actions. Execution belongs to `/` commands or the underlying shell.

Example composition:

```text
/move @selection @projects
/unzip @selection @thisfolder
git -C @projects status
```

#### Syntax Constraint

No additional special-character namespace should be introduced unless `/`, `@`, and the real shell fundamentally cannot express a necessary capability.

This is an architectural guardrail against accidentally creating a proprietary programming language.

#### Naming Constraint

Avoid multiple built-in synonyms for the same reference. For example, prefer one clear `@thisfolder` reference rather than simultaneously supporting `@here`, `@cwd`, `@current`, and `@folder`.

The exact complete built-in reference set remains to be decided.

### Built-In Reference Set — Confirmed

The initial built-in reference set is intentionally minimal:

```text
@thisfolder
@parent
@selection
```

Definitions:

- `@thisfolder` resolves to the current filesystem location represented by the workspace.
- `@parent` resolves to the parent directory of the current filesystem location.
- `@selection` resolves to the current visible selection and may contain one or multiple filesystem items.

If `@selection` is empty, resolution fails explicitly with a readable message. The resolver must not infer another target.

User-defined Locations automatically create additional references using their assigned names.

Examples:

```text
@projects
@downloads
@archive
```

`@last` is deliberately excluded from the initial architecture because "last" is ambiguous across command, result, selection, source, destination, and navigation history.

Built-in aliases/synonyms should also be avoided. Prefer one readable term such as `@thisfolder` over multiple equivalents such as `@here`, `@cwd`, `@current`, and `@folder`.

#### Reference Design Principle

> Add a new built-in reference only when a real workflow cannot remain simple and readable without it.

## Current Topic 3: Terminal Session Lifecycle

Next resolve:

- What creates a persistent terminal tab.
- Whether each tab keeps its own working directory.
- What closing a tab does to a running process.
- What closing the app does to active terminal sessions.
- Whether sessions restore after restart.
- Whether completed terminal tabs stay open or auto-close.
- How external-terminal launches are tracked, if at all.

### Topic 3A — What Creates a Persistent Terminal Tab — Confirmed

A persistent terminal tab represents an ongoing process/session, not merely terminal output.

Create a persistent terminal tab when a command matches any of these conditions:

1. A known interactive-tool rule.
2. A known long-running-process rule.
3. An explicit user request to run the command in a terminal tab.
4. A user accepts **Run in terminal** after an unknown finite command proves interactive; this is a fresh launch.

Examples that normally create tabs:

```text
codex
claude
ssh user@host
python
node
pwsh
cmd
npm run dev
vite
```

Exact built-in rules are maintained by the interactive registry and may be argument-sensitive.

Finite commands remain in the compact command area:

```text
git status
python script.py
npm install
dir
Get-ChildItem
```

A finite command does not become a persistent tab merely because it produces a large amount of output.

Unknown commands default to normal shell execution. If one proves interactive, Filekin may offer a fresh **Run in terminal** relaunch; it does not migrate the existing process and does not save a persistent routing rule in v1.

Explicit shell launches such as `pwsh` or `cmd` create terminal tabs because the user has deliberately requested a standalone shell session. The application's normal compact command area may itself maintain shell context without automatically creating a visible terminal tab.

#### Principle

> A terminal tab is a persistent process container, not an expanded output window.

### Topic 3B — Files Command Bar vs Hosted Terminal Tabs — Confirmed

The application's `/` and `@` language belongs to the **Files workspace command bar**.

The Files command bar may preprocess input before handing ordinary commands to the real shell.

Examples:

```text
python @selection
git -C @projects status
/unzip @selection @thisfolder
```

In shell commands, the application resolves `@` references first, then passes the resulting command to the real shell.

Example:

```text
python @selection
```

may resolve to:

```text
python "D:\Projects\test.py"
```

Hosted terminal tabs for Codex, Claude Code, SSH, Python REPLs, shells, TUIs, and similar applications are different:

```text
FILES TAB
  application command layer
    ├─ / actions
    ├─ @ references
    └─ real shell passthrough

HOSTED TERMINAL TAB
  raw terminal/PTY session
    └─ interactive process owns its own input behavior
```

The application should not silently inject or reinterpret `/` and `@` syntax inside third-party interactive terminal sessions.

Each persistent terminal tab keeps its own independent process state and working directory after launch.

The Files workspace and terminal tabs are connected by workspace navigation and labeling, but are not coupled.

#### Principle

> Enhance shell commands in the Files command bar; preserve native behavior inside hosted terminal applications.

### Topic 3C — Closing Tabs, Process Termination, and App Exit — Confirmed

Closing a persistent terminal tab means ending the live process/session hosted by that tab.

If the process is still running, the application must confirm before closing the tab.

User-facing behavior:

```text
Process is still running.

Closing this tab will end the live session.

[Close Session]   [Cancel]
```

If the process has already completed, the tab may close immediately without an unnecessary confirmation.

#### Graceful Termination First

Closing a tab must not be architecturally defined as an immediate hard process kill.

The Terminal Service should:

```text
1. Request graceful termination
2. Allow the process a short opportunity to exit cleanly
3. Detect whether it has exited
4. Fall back to forced termination only when necessary
```

The exact Windows/PTY signaling implementation is a technology-level decision for the Terminal Service.

#### Closing the Entire Application

If hosted terminal processes are still running when the user closes the application, show one consolidated confirmation rather than prompting separately for every tab.

Example:

```text
3 terminal sessions are still running:

● Codex · MyApp
● Claude · Website
● Server · Website

Closing the workspace will end these live processes.

[Close All and Exit]   [Cancel]
```

After confirmation, the Terminal Service should request graceful termination for active sessions and force termination only where necessary.

#### Session Persistence

Live terminal processes do **not** survive application restart in version one.

The application may persist workspace metadata such as:

- User Locations.
- Last Files workspace location.
- Previous terminal tool identity.
- Previous terminal launch directory.
- Other lightweight workspace layout information.

If previous terminal tools are surfaced after restart, restarting one creates a **new process**. The application must not imply that the old live process survived.

Whether a tool such as Codex, Claude Code, or another CLI can resume its own prior application-level session is that tool's responsibility, not the workspace's.

#### Principles

> Persist workspace context, not live processes.

> Version one should prefer obvious behavior over clever persistence.

### Topic 3D — Remaining Terminal Lifecycle Defaults — Proposed

These defaults capture the current direction but remain open to refinement where noted.

#### App-Owned Interactive Session

Current proposed direction: a persistent interactive-tool tab hosts the launched interactive application as the primary session process rather than keeping an underlying shell waiting after it exits.

Conceptually:

```text
TERMINAL TAB
└─ Codex / Claude / SSH / REPL / other interactive process
```

When that primary process exits, the tab becomes inactive and preserves its terminal output. It does not silently turn into a generic shell.

A plain shell can still be launched explicitly as its own terminal tab.

**Reason:** This keeps the Files command bar's enhanced `/` and `@` language clearly separated from raw hosted terminal applications and avoids a terminal tab unexpectedly changing identity after its primary tool exits.

#### Process Completion

If the hosted primary process exits normally on its own, the terminal tab remains open so the user can inspect and copy its previous output.

The tab becomes visibly inactive/completed.

Closing an already-completed tab requires only one normal close action and no process-termination confirmation.

**Still to resolve:** exact completed-state indicator and whether the tab title changes.

#### Process Crash / Non-Zero Exit

If the hosted process crashes or exits with an error/non-zero status, preserve the terminal tab and its output.

Show a concise failure/exit status in the tab or terminal surface. Do not automatically remove the evidence the user may need for troubleshooting.

A process failure should not automatically trigger a modal application error unless the terminal-hosting subsystem itself failed.

**Still to resolve:** exact visual treatment of error state.

#### Child Processes

A hosted terminal session may create child processes.

Current proposed direction: the Terminal Service owns the hosted process tree/session boundary. When the user confirms closing a running tab, shutdown should apply to the hosted session as a whole rather than intentionally leaving child processes orphaned.

Graceful shutdown remains the first attempt, with forced termination as fallback when necessary.

**Needs technical validation:** Windows process groups, ConPTY behavior, and tools that intentionally detach background processes require implementation research before this can become a strict guarantee.

#### Duplicate Sessions

Allow multiple sessions for the same tool and launch location.

Example:

```text
CODEX · MyApp
CODEX · MyApp · 2
```

Do not silently reuse or focus an existing session merely because the executable and launch directory match.

**Reason:** Two sessions may have different conversations, tasks, environments, or purposes.

#### Tab Naming

Initial tab names should be derived from the launched tool plus its launch context.

Examples:

```text
CODEX · MyApp
CLAUDE · Website
SSH · server-name
POWERSHELL · Downloads
```

Duplicate names may receive a simple numeric suffix.

Current proposed direction: the label describes why/where the session was launched rather than continuously following every working-directory change that may happen inside the hosted process.

**Still to resolve:** user renaming, title truncation, and whether applications may supply their own preferred title.

#### External Terminals

A terminal launched into the user's preferred external terminal application is not owned as a hosted terminal session by this workspace.

Current proposed direction:

- The workspace launches the external terminal in the requested directory/context.
- The external terminal owns its own process lifecycle afterward.
- Closing this workspace does not terminate that external terminal.
- The workspace should not imply that it can restore, reconnect, or manage the external terminal session.

**Needs technical validation:** exactly how much launch/process metadata can or should be retained depends on the selected external terminal and Windows launch APIs.

#### Folder Rename or Deletion During a Session

Current proposed direction: renaming, moving, or deleting the folder from which a terminal session was launched should not automatically terminate that session.

The launch-context label may become stale. The workspace may indicate that the original path no longer exists, but should not attempt to force the hosted application into another directory.

#### Sleep and Hibernate

Current proposed direction: Windows sleep or hibernate is not application exit. Hosted sessions should remain attached if Windows preserves the application and process state.

No special session-persistence system should be introduced solely for sleep/hibernate in version one.

### Topic 3D — Remaining Terminal Lifecycle Rules — Confirmed

Version-one terminal lifecycle is now defined as follows.

#### Primary Hosted Process

A persistent interactive-tool tab hosts the launched interactive application as its primary process.

Conceptually:

```text
TERMINAL TAB
└─ Codex / Claude / SSH / REPL / other interactive process
```

The tab does not silently fall through to a hidden underlying shell when the primary process exits.

A plain shell may still be launched explicitly as its own terminal tab.

#### Child Processes

Attached child processes are considered part of the hosted terminal session.

When the user closes the tab, the Terminal Service attempts graceful shutdown of the primary process and attached children, then force-terminates attached leftovers only when necessary.

The application should not promise to terminate intentionally detached background processes that no longer belong to the hosted session boundary.

#### Process Completion

A normally completed process leaves its terminal tab open so output remains available.

The tab becomes visibly completed.

Closing a completed tab requires one normal close action and no termination confirmation.

#### Process Failure

A crashed process or non-zero exit also leaves its terminal tab open.

The tab becomes visibly failed and should show the exit code/status when available.

Do not show a modal application error unless the terminal-hosting subsystem itself failed.

#### Session States

Use a simple conceptual state model:

```text
● running
○ completed
! failed
```

Exact iconography and styling may be refined during UI implementation, but the three-state behavior is confirmed.

#### Duplicate Sessions

Multiple sessions using the same tool and launch location are allowed.

Example:

```text
CODEX · MyApp
CODEX · MyApp · 2
```

The application must not silently reuse an existing session merely because tool and launch directory match.

#### Tab Naming

Default terminal-tab names use:

```text
TOOL · launch-context
```

Examples:

```text
CODEX · MyApp
CLAUDE · Website
SSH · server
POWERSHELL · Downloads
```

The title describes why/where the session was launched. It should not continuously change in response to internal working-directory changes.

Manual tab renaming is not required for version one.

#### External Terminals

Externally launched terminals are not hosted sessions owned by this application.

The workspace:

- launches the preferred external terminal in the requested directory/context,
- does not terminate it when this application closes,
- does not present it as a managed hosted session,
- does not claim it can restore or reconnect to it.

#### Launch-Folder Changes

Renaming, moving, or deleting the original launch folder does not automatically terminate a running terminal session.

The stored launch-context label may become stale; the workspace may indicate that the original path no longer exists.

#### Sleep and Hibernate

Sleep and hibernate do not count as application exit.

No special persistence system is required for version one. If Windows preserves the app and process state, the hosted sessions remain attached.

#### Terminal Lifecycle Summary

```text
launch interactive tool
        ↓
persistent hosted tab
        ↓
running
   ┌────┴────┐
normal     failure
  ↓           ↓
completed    failed
   └────┬────┘
        ↓
user closes tab
```

If the user closes while still running:

```text
confirm
  ↓
graceful shutdown
  ↓
force attached leftovers only if required
  ↓
close tab
```

## Current Topic 4: Undo and Operation History

Next resolve:

- Which application-owned file operations are undoable.
- Whether undo history survives app restart.
- How long history is retained.
- How collisions are handled during undo.
- Whether delete uses the Recycle Bin.
- How bulk commands such as `/tidy` and `/unzip` journal their changes.
- Whether shell commands participate in undo.

### Topic 4A — Undo Ownership, `/undo`, `/history`, and Command Recall — Confirmed

Only filesystem mutations owned and executed by this application participate in the guaranteed undo/operation-history system.

Examples may include:

```text
/move
/rename
/tidy
/unzip
GUI rename/move/delete actions
drag-and-drop operations performed by the app
```

Ordinary shell commands do not participate in guaranteed undo because the application cannot reliably infer or reverse arbitrary side effects.

Examples:

```text
Remove-Item ...
git clean ...
python cleanup.py
```

#### `/undo`

`/undo` is a version-one application command.

In version one it should remain intentionally simple:

```text
/undo
```

It reverses the most recent app-owned operation that is still safely undoable.

Do not require advanced syntax such as `/undo 3`, `@last`, or force flags in version one.

#### `/history`

`/history` is a version-one application command.

It provides a visual bird's-eye view of app-owned filesystem operations and their current reversibility.

Conceptual result:

```text
OPERATION HISTORY

12:42  Moved 8 files → @projects       [Undo]
12:38  Renamed photo.png → cover.png   [Undo]
12:31  Extracted archive.zip           [Undo]
12:20  Tidied Downloads                [Undo]
11:55  Deleted notes.txt               [Restore]
11:31  Copied 14 files → @archive       —
```

The exact visual design remains a UX implementation detail, but the history view should make it clear:

- what the app changed,
- when it changed it,
- whether it is still reversible,
- and what action is available.

#### Command Recall Is Separate

The Files command bar maintains command-entry history separately from filesystem operation history.

Up/Down arrows should navigate previously entered commands, including both slash commands and shell commands, using the original text the user entered.

Example command recall:

```text
/where python
git status
/unzip @selection @projects
python app.py
```

This is not the same as `/history`.

#### Architectural Separation

```text
UP / DOWN
→ command-entry recall

/history
→ app-owned filesystem operation journal

/undo
→ reverse most recent safely undoable app-owned operation
```

#### Principle

> Command history remembers what the user typed. Operation history remembers what the application changed.

### Topic 4B — Persistent History vs Session-Scoped Undo — Confirmed

Operation history persists across application restarts.

Undoability does not.

The operation journal therefore has two conceptual scopes:

```text
CURRENT SESSION
  app-owned operations may be undoable

PREVIOUS SESSIONS
  informational history only
```

When the application restarts, previously recorded operations remain visible in `/history`, but their Undo/Restore actions are removed.

This avoids pretending that filesystem state from a prior session is still safe to reverse after external changes may have occurred.

#### Principle

> Persist the record of what happened; do not persist the promise that it can still be undone.

#### Retention Direction — Proposed

History retention should be automatic and require no routine user maintenance.

The application should choose a reasonable default retention policy and clean up old history automatically.

Advanced settings may later allow users to choose a retention window by age or entry count, but users should not be required to manage the journal manually.

Potential models include:

```text
retain last N days
retain last N entries
retain whichever limit is reached first
```

Exact defaults remain to be decided.

### Topic 4C — Operation History Retention — Expected Version-One Behavior

The expected version-one retention model is a small rolling operation journal rather than a time-based audit log.

Expected default:

```text
retain the most recent 50 app-owned operations
```

When operation 51 is recorded, the oldest retained operation rolls off automatically.

An **operation** means one user-level action, not one affected filesystem item.

Example:

```text
/tidy @downloads
```

moving 83 files creates one history entry whose details may describe the 83 affected files.

Likewise, moving 42 selected files in one action creates one operation-history entry.

#### Version-One Simplicity

Do not require users to configure retention duration or entry count in version one.

History cleanup is automatic.

A Clear History action may exist in Settings for exceptional privacy/troubleshooting needs, but clearing history is not routine maintenance.

#### Status

The 50-operation limit is the current product expectation, not an irreversible architectural constraint. It may be tuned later based on real usage without changing the journal model.

### Topic 4D — Undo Collision Handling — Confirmed

Undo must never silently overwrite an existing filesystem item.

If reversing an operation encounters a destination collision, pause and ask the user how to resolve it.

Available conflict actions should include:

```text
Replace
Keep Both
Skip
Cancel Undo
```

For bulk operations, provide an Apply to All option where appropriate.

`Replace` must never be the default-selected conflict action.

If an undo completes only partially because items were skipped or could not be restored, `/history` must record the result accurately rather than presenting the original operation as fully reversed.

#### Principle

> Undo should not create new data loss while attempting to reverse an earlier action.

### Topic 4E — Normal Delete Behavior — Confirmed Direction

Normal deletion performed by the application should respect the user's existing Windows Recycle Bin behavior/settings rather than inventing a separate application trash system.

The application should use the standard Windows deletion path appropriate for Recycle Bin behavior where supported.

The workspace should not maintain its own hidden copy of deleted files solely to implement undo.

If Windows or the target filesystem cannot provide recoverable deletion for a particular item, the application must not imply that the deletion is safely undoable.

Permanent-delete behavior, if exposed at all, should remain distinct from normal deletion and should not be the default version-one workflow.

### Virtual Workspace Locations — Confirmed

The Files workspace may represent both physical filesystem locations and application-provided virtual locations.

Conceptually:

```text
PHYSICAL LOCATION
D:\Projects
C:\Users\...\Downloads

VIRTUAL LOCATION
Recycle Bin
```

A virtual location is presented as a first-class Files workspace destination without pretending that it is an ordinary user-facing filesystem path.

#### Recycle Bin

Recycle Bin is a confirmed virtual workspace location.

The application should not expose the raw Windows `$Recycle.Bin` storage structure as the normal user model.

Users should instead see a readable `Recycle Bin` location that uses Windows-native Recycle Bin behavior underneath.

Within the Recycle Bin view:

- users can browse recycled items,
- `@selection` refers to the currently selected recycled item or items,
- appropriate app-owned actions such as Restore may operate on the selection,
- destructive or restore behavior should use Windows-native semantics rather than raw manipulation of Recycle Bin implementation files.

The Recycle Bin does not need to appear permanently in the left sidebar if the user has not chosen to pin/assign it there. It may be reachable through the application's location/navigation mechanisms and can participate in the same user-controlled Locations philosophy as other useful destinations.

#### Reference Semantics in Virtual Locations

`@selection` remains contextual and refers to the visible selected objects in the current Files view.

`@thisfolder` is naturally filesystem-oriented and may not be meaningful for every future virtual location. Commands should validate whether a reference type is valid for the current location rather than manufacturing a fake physical path.

#### Principle

> Present Windows concepts in the form users understand; do not expose implementation details merely because they exist on disk.

### Topic 4F — Narrow Undo Scope and Complex Operations — Confirmed

Version-one undo is intentionally narrow.

Undo should focus on simple, directly reversible filesystem mutations owned by the application rather than attempting to become a universal filesystem rollback system.

Expected undoable operations:

```text
move
rename
Windows-native restore/delete cases only where recoverability is reliable
```

Operations are not automatically undoable merely because the application initiated them.

#### `/unzip`

**Superseded on 2026-08-27:** `/unzip` is part of the session-scoped undo system. The extraction plan
already identifies every path Filekin writes, and overwritten originals go to the Recycle Bin first.
Undo therefore deletes only Filekin-created files/folders and then restores replaced originals. The
older direction below is retained for decision history.

`/unzip` is not part of the undo system.

Extraction leaves the original archive intact, and implementing transactional rollback for extracted files would add bookkeeping and collision complexity without enough version-one value.

The operation may still appear in `/history` as informational activity.

#### `/tidy`

`/tidy` is not part of the undo system.

The existing Tidy concept is a separate organizer application. If represented in this workspace, the intended workspace form takes a folder hierarchy/reference as its target.

Example:

```text
/tidy @thisfolder
```

Its implementation/integration remains a separate product decision and may require rebuilding rather than simply embedding the existing application.

Because Tidy may perform many organizational changes across a hierarchy, version one should not attempt to journal a complete reversible transaction for it.

If Tidy is integrated, a preview/confirmation model is the preferred safety mechanism rather than universal rollback.

`/tidy` may still be recorded in `/history` even when it is not undoable.

#### Copy

Copy is not guaranteed to participate in version-one undo. Removing a copied destination later can become unsafe if that copy has subsequently changed.

#### Principle

> Undo is for simple direct reversals. Preview is the preferred safety mechanism for complex transformative operations.

History and undo intentionally have different scopes: an operation may be recorded without being undoable.

### Topic 5A — Files Command Execution Pipeline — Confirmed Direction

The Files command bar uses a small deterministic execution pipeline.

```text
INPUT
 │
 ├─ begins with /
 │    ↓
 │  slash-command parser
 │    ↓
 │  resolve supported @ references
 │    ↓
 │  app-owned execution
 │
 └─ everything else
      ↓
    resolve known workspace @ references
      ↓
    interactive-tool routing check
      ↓
   ┌───────────────┬────────────────┐
   │ interactive   │ finite         │
   ↓               ↓
hosted terminal   Files command area
tab
```

#### Slash Commands Are App-Owned

Input beginning with `/` is interpreted as an application command.

Examples:

```text
/go D:\Client Work
/where python
/history
/undo
/unzip @selection @thisfolder
```

Application commands should execute through structured application handlers rather than being translated into PowerShell commands internally.

A command may declare additional names that reach the same handler. The command registry registers a
command under its primary name and each declared alias, and rejects a collision between any two of
them at construction, so an alias can never silently shadow another command. The registry records the
name the user actually typed, and a handler that echoes its own name — usage and failure lines — uses
that typed name rather than its primary name. Aliases exist only where the product has confirmed that
several words name one operation; they are not a general synonym mechanism.

This preserves:

- predictable application behavior,
- structured validation/errors,
- operation-history ownership,
- undo support where explicitly provided,
- Windows-native behavior where appropriate.

`/go` is a structured navigation command with its own narrow parser. It consumes the entire remainder
of the line as one folder target rather than using the shared whitespace tokenizer. The parser strips
optional matching outer quotes, expands one recognized workspace reference, resolves relative paths
against the visible Files folder, and returns one absolute target. The application validates that the
target is an existing directory and then uses the normal Files navigation pipeline; it does not send
`Set-Location` through PowerShell. Later command-bar execution synchronizes the persistent runspace to
the new visible Files folder through the existing location contract.

#### Ordinary Shell Input

Input not beginning with `/` is ordinary shell input.

Examples:

```text
git status
Get-Process
python @selection
```

Known workspace references may be resolved before execution, then the remaining shell expression is passed through to the configured shell.

PowerShell is the expected Windows default shell direction, but the application should not implement its own PowerShell grammar.

#### Do Not Hand-Roll a PowerShell Parser

The application should not attempt to understand or reproduce complete PowerShell syntax such as pipelines, redirection, subexpressions, arrays, or other shell grammar merely to execute commands.

If deeper shell-aware integration is ever required, prefer a mature parser/integration mechanism rather than a custom partial PowerShell parser.

#### `@` Compatibility With Real Shell Syntax

The application must not assume every `@word` belongs to the workspace language.

Only recognized workspace reference tokens are candidates for resolution.

Examples of recognized application references may include:

```text
@selection
@thisfolder
@parent
@projects
```

An unknown `@something` appearing in ordinary shell input should generally pass through untouched so legitimate shell syntax is not broken.

Slash-command handlers may be stricter because their argument grammar belongs to the application.

#### Multi-Item References

When a known reference resolves to multiple filesystem items in shell input, expand it as multiple safely quoted shell arguments.

Conceptually:

```text
tool @selection
```

may become:

```text
tool "file one.txt" "file two.txt" "file three.txt"
```

The application resolves the reference but does not attempt to decide whether the target shell command semantically supports those arguments.

#### Interactive Tool Routing

Known interactive tools may route directly into hosted terminal tabs.

Routing should remain deterministic.

AI must not be the authority deciding where a command executes.

Heuristics may later provide advisory/fallback behavior for unknown interactive tools, but should not silently override deterministic rules.

The built-in interactive-tool registry remains the primary mechanism for known tools. Version one does not include persistent user-defined additions; an unknown command may instead use the one-time fresh **Run in terminal** fallback.

#### Errors

Application-owned commands should return concise structured errors and useful correction guidance.

Shell commands should preserve authentic shell output/errors.

Examples:

```text
@selection is empty.

/unzip needs an archive.
Try: /unzip @selection @thisfolder
```

#### Principles

> Enhance the shell; do not reimplement it.

> Routing should be deterministic before it is clever.

> Application commands are owned by the application. Shell commands remain shell commands.

### Topic 5B — Command Results and Workspace Views — Confirmed Direction

The Files command bar remains a single-line control. It does not expand into a persistent terminal pane over the file hierarchy.

#### Principle

> The command bar reports. The workspace explains.

The command bar invokes actions and reports concise status. Larger output or interactive application views use the main workspace surface or a dedicated terminal tab.

#### Result Indicator

After a finite command completes, the command bar may show a compact result indicator such as:

```text
✓ Completed · 6 lines        [View]
! Failed · exit code 1       [View]
✓ Moved 12 files             [Undo]
✓ Extracted 34 files         [View]
```

The result indicator for the most recently executed command remains available until the next command is actually executed. Merely typing or editing the next command does not discard the previous result.

The normal prompt remains a one-line command surface.

#### Workspace Result Views

Selecting `View` opens the command's larger output in the main Files workspace area without expanding the command bar.

Closing the result view returns to the prior file-hierarchy view/state.

Commands whose purpose is explicitly to show a view may open that workspace view immediately.

Examples:

```text
/history
/where
/disk
```

Finite shell commands may remain collapsed by default and expose `[View]` when output exists.

#### Three Behavioral Classes

```text
ACTION COMMAND
→ update files/state
→ compact command-bar result

VIEW COMMAND
→ open closeable interactive workspace view

INTERACTIVE APPLICATION
→ open hosted terminal tab
```

A finite shell command is generally an action/result command: its output can be inspected through a workspace result view without creating a new tab.

#### Result Retention in Operation History — Proposed Boundary

`/history` remains primarily the journal of app-owned operations, not a permanent transcript of every shell command.

Where useful, history entries may expose details/results produced by app-owned operations. Arbitrary shell stdout/stderr should not automatically be persisted in `/history` because that would turn operation history into a shell transcript and could retain large or sensitive output.

Version one does not maintain a multi-result shell-output buffer. The most recent finite command result remains available for immediate inspection until the next command is executed, then it may be discarded.

Arbitrary shell stdout/stderr is not persisted across application restart.

### Topic 5C — Finite Command Output Lifetime — Confirmed

Version one keeps exactly one immediately inspectable finite-command result: the result of the most recently executed command.

```text
execute command
      ↓
result/status available
      ↓
[View] remains available
      ↓
user may type/edit next command
      ↓
previous [View] still available
      ↓
next command is executed
      ↓
previous finite-command output may be discarded
```

There is no timer-based expiration.

There is no multi-command recent-output buffer in version one.

There is no persistent shell-output transcript.

Shell stdout/stderr does not become part of `/history` and is not expected to survive application restart.

#### `/history` Boundary

`/history` remains the persistent journal of app-owned operations.

App-owned operation entries may include useful structured details about what the application changed, including details for non-undoable app operations such as `/unzip`.

Arbitrary shell command output is excluded.

#### Command Recall Boundary

Up/Down command recall may still reproduce a previously typed command so the user can execute it again, but command recall does not preserve that command's old output.

#### Principle

> The last result remains available until the user actually executes something else.

### Topic 5D — Files Workspace View Grammar — Confirmed

Rich informational commands are temporary views of the Files workspace rather than separate modal windows or automatically-created tabs.

The Files tab retains one persistent underlying Files location/state and may show one temporary rich view at a time.

Examples:

```text
Files
Files · History
Files · Where — python
Files · Disk
Files · Recycle Bin
Files · Command Output
```

The command syntax and interface language intentionally differ:

```text
/history       → Files · History
/where python  → Files · Where — python
/disk          → Files · Disk
```

#### Principle

> Commands use symbols. The interface answers in English.

`/` remains action/command syntax and `@` remains reference syntax. The resulting interface uses readable English names rather than forcing command notation into every UI label.

#### Navigation

A temporary rich view preserves the underlying Files location, selection, scroll position, and other reasonable view state.

Simple Back navigation returns to that preserved Files state.

Version one should keep temporary rich-view navigation shallow:

```text
persistent Files location
        ↓
zero or one temporary rich view
```

Temporary rich views do not need to stack recursively. Invoking another rich view can replace the current temporary view while preserving the same underlying Files state.

Interactive terminal applications remain separate terminal tabs and are not part of this temporary-view model.

#### Open Visual UX Question

A rich view temporarily occupies the main Files surface, which means the file hierarchy is not simultaneously visible. This is accepted as the current clean default, but contextual workflows may require carefully designed ways to reference or return to the underlying files without turning the interface into a permanent split-pane layout.

### Topic 5E — Rich Views Do Not Own Filesystem Selection — Confirmed

Rich Files workspace views are interactive result/control surfaces, but they do not automatically become filesystem-selection surfaces.

#### Core Boundary

> Rich views contain controls and results. Files contains filesystem selection.

`@selection` therefore has one stable meaning:

> the selected filesystem item or items in the underlying Files context.

Opening a rich view does not redefine `@selection`.

The underlying Files location, selection, scroll position, and other reasonable state remain preserved while a temporary rich view is displayed.

#### History

`Files · History` does not expose selectable history rows as `@selection` targets.

History entries provide explicit controls appropriate to the operation:

```text
Moved 4 files → src/       [Details] [Undo]
Renamed config.old         [Details] [Undo]
Extracted archive.zip      [Details]
```

Users interact with the actions rather than selecting the history record as though it were a file.

#### Where

`Files · Where — python` may expose result-specific actions:

```text
C:\Python313\python.exe       [Open] [Go to]
C:\Tools\Python\python.exe    [Open] [Go to]
```

A Where result is not automatically a Files selection.

`Go to` can return/navigate to the real Files hierarchy, reveal the target filesystem item, and establish a normal Files selection there.

#### Disk

`Files · Disk` may expose navigation controls:

```text
Projects       18.2 GB       [Open]
Downloads       9.7 GB       [Open]
```

Opening/navigating to a filesystem location returns the user to normal Files semantics rather than redefining rich-view rows as selections.

#### Command Bar While a Rich View Is Open

The Files command bar remains usable while a temporary rich view is displayed.

Filesystem references continue to resolve against the preserved underlying Files context.

For example, `@thisfolder` continues to refer to the underlying Files location, and `@selection` continues to refer to the preserved Files selection.

This lets rich views behave as temporary lenses over Files rather than separate contexts with competing reference semantics.

#### Visual Context

A rich view may show lightweight context such as the underlying Files location when useful, but the application should not add permanent split panes merely to keep the hierarchy visible.

Back returns immediately to the preserved Files state.

A dedicated Peek Files mechanism is not required for version one unless real use demonstrates a need.

#### Principle

> A clickable result is not automatically a selection.

This keeps the `@` language simple and prevents rich views from changing the meaning of established filesystem references.

### Topic 5F — Strong Keyboard Support and Rich-View Focus — Confirmed Direction

Strong keyboard support is a product-level requirement, not an optional accessibility layer added after the mouse experience.

The application intentionally combines a clickable filesystem with terminal-style control. Core workflows therefore need to remain practical with either mouse or keyboard.

#### Three Distinct Concepts

The architecture must keep these concepts separate:

```text
FILESYSTEM SELECTION
→ actual selected filesystem item(s)
→ referenced by @selection

UI FOCUS
→ the currently keyboard-operable control

COMMAND FOCUS
→ focus in the bottom Files command bar
```

UI focus must never silently redefine filesystem selection.

#### Rich Views Are Keyboard and Mouse Operable

Everything actionable in a rich workspace view should be reachable and operable by keyboard as well as mouse.

Rich views should not become mouse-only surfaces merely because their rows are not filesystem selections.

Typical baseline navigation:

```text
↑ / ↓   move among primary actions/results
Tab     move among available controls
Enter   activate focused control
Esc     return to underlying Files view
```

Exact shortcuts may vary where a view genuinely requires different behavior, but conventional keys are preferred over introducing a custom TUI/Vim-style key language in version one.

#### Focus the Action, Not a Fake Selection

To visually reinforce the difference between rich-view interaction and filesystem selection, keyboard focus should land on actionable controls rather than turning an entire rich-view row into a selection state.

Example:

```text
Files · History

Moved 4 files → src/       [Details] [Undo]
Renamed config.old         [Details] [Undo]
Extracted archive.zip      [Details]
```

Arrow-key paging/navigation may move focus between the row's primary action buttons. Tab can move among additional controls.

Likewise:

```text
Files · Where — python

C:\Python313\python.exe       [Go to] [Open]
C:\Tools\Python\python.exe    [Go to] [Open]
```

Keyboard focus can move through `[Go to]` or other explicit actions without making the path itself an `@selection` target.

#### Visual Language

The UI should make the states visibly different:

```text
file highlight
→ filesystem selection

button/control focus indicator
→ keyboard focus

command-bar caret
→ command focus
```

This reduces ambiguity while preserving the hybrid mouse + terminal model.

#### Opening a Rich View

When a rich view is invoked, keyboard focus should enter the view at a sensible actionable control so the view is immediately usable without a mouse.

The command bar remains available as part of the workspace and should have a consistent keyboard path for returning focus to it. The exact global command-focus shortcut remains to be selected.

#### Principle

> Everything clickable in a rich view should also be keyboard operable, without turning rich-view focus into filesystem selection.

### Topic 5G — Space-to-Command Focus — Confirmed

Space is the primary fast-focus shortcut for the Files command bar when the user is on a neutral workspace surface.

```text
neutral Files/rich-view surface
        ↓
      Space
        ↓
command bar receives focus
```

#### Conflict Rule

Space is not a destructive global override. It only redirects focus when the currently focused context does not legitimately consume Space.

```text
editable/text field focused
→ Space behaves normally

button/checkbox/control that uses Space focused
→ Space behaves normally

command bar focused
→ Space is typed normally

neutral Files/rich-view surface
→ focus command bar
```

This preserves normal keyboard semantics while making command entry exceptionally fast.

#### Principle

> From any neutral workspace surface, press Space and type.

The shortcut reinforces the command bar as an always-nearby control surface without requiring a modifier-key chord.

### Topic 5H — `/run` and Relative App-Command Targets — Confirmed Direction

`/run` is the simple app-owned execution command.

It gives users an explicit, readable execution path without requiring them to learn PowerShell invocation syntax such as `.\` or `&`.

Examples:

```text
/run tool.exe
/run scripts\build.py
/run @selection
/run @thisfolder\tool.exe
/run @projects\tool.exe
/run "C:\Program Files\Tool\tool.exe"
```

#### Relative Resolution

Relative targets supplied to app-owned commands resolve against the current underlying Files location.

Therefore, if Files is currently showing:

```text
D:\Projects\MyApp
```

then:

```text
/run tool.exe
```

resolves to:

```text
D:\Projects\MyApp\tool.exe
```

Users do not need to write `@thisfolder` for the common case.

The explicit form remains valid when clarity or composition is useful:

```text
/run @thisfolder\tool.exe
```

This establishes a broader app-command rule:

> Relative targets in app-owned commands resolve against the current Files location.

That rule may also simplify other app commands where appropriate:

```text
/unzip archive.zip
/rename notes.txt notes-old.txt
```

#### References and Paths Compose

References can be combined with a child path:

```text
/run @projects\build.exe
/run @thisfolder\tools\helper.exe
```

The reference resolves first, then the relative child path is joined/resolved safely.

#### No Implicit Whole-System Search

`/run tool.exe` does not search the entire computer merely because `tool.exe` is absent from the current Files location.

**Implemented, 2026-08-26:** the lookup order is the visible Files folder, then the ordinary Windows `PATH`/`PATHEXT` search — the same list Windows itself consults, which is not a whole-system search. A target that resolves nowhere is still handed to Windows shell execution and its failure is reported inline; no discovery command is suggested (DECISIONS.md, 2026-08-26 — "`/run` Resolves the Visible Folder First, Then `PATH`").

This keeps `/run` deterministic.

#### Relationship to PowerShell

`/run` does not remove or replace native shell execution.

Power users may continue using normal shell syntax:

```powershell
.\tool.exe
& "C:\Program Files\Tool\tool.exe"
python script.py
```

The app-owned form is the simpler workspace language:

```text
/run tool.exe
```

#### Target Types

The initial intent of `/run` is to ask Windows or an explicitly configured runtime/handler to launch a target.

Executable files are the clearest case.

Scripts and associated file types may be supported where Windows/configured runtime semantics are predictable.

Directories should normally be navigated rather than treated as executable targets.

**Implemented, 2026-08-26:** a directory target is refused with a clear message rather than silently opening Explorer, and `/run @selection` launches every selected target in order, reporting how many started and naming each failure. Arguments are refused with a multi-target selection because they cannot be attributed to one of them. Batch confirmation for very large selections is not implemented.

#### Principle

> `/run` is explicit about the action while allowing the target to stay simple.

### Topic 5I — Raw Paths Preserve Shell Semantics — Confirmed

The workspace convenience language does not redefine raw PowerShell path behavior.

The boundary is:

```text
/   → app-owned action/command
@   → workspace reference
raw shell/path syntax → PowerShell
```

Examples:

```text
@projects
→ workspace reference behavior

/run tool.exe
→ app-owned action with a relative target resolved from current Files context

cd C:\Projects
→ normal PowerShell navigation

cd ..
→ normal PowerShell navigation

.\tool.exe
→ normal PowerShell execution

& "C:\Program Files\Tool\tool.exe"
→ normal PowerShell execution
```

A bare raw path is not given a new Files-specific meaning merely because it looks like a directory or file path. The application should not silently reinterpret PowerShell path syntax as navigation, reveal/select, or execution.

Users who want the simpler workspace language use references and actions. Power users retain uninterrupted shell pathing and invocation semantics.

#### Principle

> References and actions simplify the workspace; raw paths remain the shell's language.

This follows the broader architecture rule:

> Enhance the shell; do not redefine it.

### Topic 5J — Pluggable Shell Backends — Confirmed Architecture

The Files command bar is architected against a shell-backend/adapter boundary rather than hard-wired directly to one shell implementation.

Version one ships with PowerShell as the guaranteed command-bar shell.

Conceptually:

```text
FILES COMMAND BAR
       ↓
workspace language layer
  / actions
  @ references
       ↓
selected Shell Adapter
       ↓
PowerShell in v1
```

The workspace language remains app-owned and independent of the shell backend.

Potential adapter responsibilities include:

```text
start/manage shell
execute finite shell input
set/track working directory
quote/escape resolved arguments
translate filesystem paths where required
return stdout/stderr
return exit status
identify backend capabilities
```

Future adapters could support shells such as PowerShell 7, Command Prompt, Git Bash, WSL-backed shells, or other explicitly configured shells without redesigning the Files command bar.

Supporting a shell requires more than changing the executable. Quoting rules, path representation, environment semantics, startup behavior, and working-directory behavior may differ.

In particular, Windows paths may require translation for non-Windows-native shells such as WSL.

#### Version-One Scope

```text
architecture: pluggable
shipping backend: PowerShell
guaranteed behavior: PowerShell
other shells: future capability, not v1 requirement
```

The application should not silently switch shell backends based on folder/project context.

If multiple backends are supported later, shell choice should be explicit and predictable.

#### Principle

> Design the command bar around a shell boundary; do not make the entire workspace a PowerShell implementation.

### Topic 5K — Files Navigation History vs. Rich Views — Confirmed

Each Files tab owns its own filesystem navigation history.

Rich views are command-driven temporary views and are not entries in the Back/Forward filesystem navigation stack.

#### Back

Back follows a visible-state-first rule:

```text
if rich view is open
→ dismiss rich view and restore underlying Files state

otherwise
→ navigate to previous filesystem location
```

Example:

```text
Files · History
   ↓ Back
Files · src
   ↓ Back
Files · Projects
```

Back dismissing a rich view does not add that view to Forward history.

#### Forward

Forward operates only on filesystem navigation history.

```text
/history
→ Files · History

Back
→ Files

Forward
→ filesystem forward location, if one exists
```

Forward does not reopen History, Where, Disk, command output, or other temporary rich views.

#### Up

Up is a filesystem operation only:

```text
Up
→ parent directory of the current underlying Files location
```

Rich-view dismissal remains the job of Back or Esc. Up does not create special rich-view semantics.

#### Esc

Esc may dismiss the active rich view and restore the preserved underlying Files state.

This reinforces that rich views are temporary command results/lenses rather than navigable pages.

#### Separation

```text
FILESYSTEM NAVIGATION
Back / Forward / Up
→ locations

RICH VIEWS
/history /where /disk / result views
→ command-driven temporary surfaces
→ dismissed with Back or Esc
→ never restored by Forward
```

#### Principle

> Rich views are invoked, not visited.

### Topic 5L — Windows-Familiar Open Behavior and Minimal Context Menus — Confirmed Direction

GUI file interaction should remain familiar to Windows users while avoiding Windows Explorer's menu accumulation.

#### GUI Open Behavior

```text
single click
→ select

double-click / Enter
→ invoke the Windows-defined/default Open behavior for the item

folder Open
→ navigate into folder

executable Open
→ launch according to Windows behavior

right-click
→ compact app-owned context menu
```

The application should not invent broad file-type-specific double-click semantics when Windows already has an established default action/association.

This creates a useful distinction:

> GUI Open respects Windows. `/run` expresses explicit execution intent.

For executable files, Open and `/run` may converge. For documents/scripts, they may intentionally differ according to Windows associations and configured runtime behavior.

#### Minimal Context Menu

The application uses its own intentionally small context menu rather than reproducing the full Windows Explorer context menu as the primary interaction surface.

Initial universal menu direction:

```text
Open
Rename
────────
Copy
Cut
Copy Path
Delete
────────
Properties
```

Exact ordering/polish may evolve, but the menu should remain shallow and compact.

Avoid growing file-type-specific menu trees merely because additional actions exist.

The command bar carries the long tail of capability.

#### Interaction Hierarchy

```text
FASTEST
keyboard shortcuts

DIRECT
double-click / compact context menu

POWERFUL
command bar

RARE/ADVANCED
available without bloating primary menus
```

Common operations should have direct interactions and familiar keyboard shortcuts where appropriate, including F2, Delete, Ctrl+C/X/V, Enter, and the established Space-to-command behavior.

Submenus should be avoided unless a future feature has a strong demonstrated need.

#### Principle

> Do not bury capability in menus. Give common actions direct interactions and let the command bar carry the long tail.

And:

> The context menu handles obvious manipulation; the command bar handles capability.

### Topic 5M — Keep `@thisfolder`; Use Autocomplete for Speed — Confirmed

`@thisfolder` remains the canonical built-in reference for the current Files location.

Although it is longer than alternatives such as `@here`, its meaning is significantly clearer in commands that involve source/destination relationships.

Examples:

```text
/unzip archive.zip @thisfolder
/info @thisfolder
/run @thisfolder\tool.exe
```

The language should optimize for readability when seen, while autocomplete handles typing speed.

#### Autocomplete as the Speed Layer

Typing a partial reference should surface matching known references:

```text
@t
→ @thisfolder
```

The user accepts a suggestion with Tab. Enter remains command execution and never silently completes.

Typing `@` may expose the small reference vocabulary:

```text
@selection
@thisfolder
@parent
@projects
```

User-defined Locations participate in the same completion system.

This avoids introducing shorter but less precise aliases such as:

```text
@here
@cwd
@folder
@current
```

#### Principle

> Readable when seen. Fast when typed.

The language stays self-explanatory while autocomplete removes the cost of longer canonical names.

### Topic 5N — Command-Bar Completion Boundary — Confirmed

Autocomplete remains deliberately narrow. The application provides discovery/completion only for the language it owns:

```text
/... → app-command discovery/completion
@... → known workspace-reference discovery/completion
```

Examples: `/hi` + Tab → `/history`; `@thi` + Tab → `@thisfolder`.

Ordinary shell input remains owned by the selected shell backend and should retain that shell's native completion behavior as closely as practical. Version one does not add a separate filesystem-target cycling/completion system for app commands.

When the cursor is actively completing a recognized `/` command token or recognized `@` reference token, app completion owns Tab. Otherwise Tab is left to the selected shell's normal completion semantics.

Typing alone does not open suggestion UI. Tab requests app completion: one match completes directly;
multiple matches extend their shared prefix and open a compact described overlay. While that overlay
is open, Tab accepts the highlight, Up/Down browses, Esc dismisses without changing the draft, and
Enter submits/executes rather than silently completing. When it is closed, Up/Down retains command
history recall.

PowerShell already uses `@` syntax, so the app must not claim every arbitrary `@word`. Known workspace references may be resolved by the app; unknown/non-reference `@` syntax remains shell input.

> We autocomplete what we invented. The shell completes what it owns.

### Topic 5O — `@selection` Cardinality and Command Validation — Confirmed

`@selection` always resolves to the complete current filesystem selection.

It never silently means only the first selected item.

Commands declare what target cardinality and target types they accept.

Conceptually:

```text
REFERENCE
@selection
→ complete selected item set

COMMAND
→ validates whether that set is valid input
```

#### Command Categories

Examples:

```text
MULTI-TARGET FRIENDLY
/run @selection
/info @selection
/move @selection @projects

SINGLE-TARGET / SINGLE-QUERY
/where python

CONTEXT-ONLY
/history
/disk
/places

TYPE-RESTRICTED
/unzip @selection
→ valid only when selected targets satisfy archive requirements
```

`/where` is not treated as a generic multi-selection command. Its normal input is one query/app/tool name such as:

```text
/where python
/where codex
/where "Visual Studio Code"
```

If a command receives an invalid number or type of targets, it should fail clearly or ask for resolution rather than changing the meaning of the reference.

#### `/run` and Multiple Targets

`/run @selection` may launch multiple selected targets.

For unusually large selections, the app may require confirmation before launching all targets.

The exact confirmation threshold is a UX/implementation detail and does not change the command semantics.

#### Principle

> References describe what is selected. Commands decide whether that input is valid.

And:

> References do not guess; commands validate.

### Topic 5P — Core App-Owned File Operation Commands — Confirmed

The command bar includes a small set of direct filesystem-manipulation commands so keyboard-driven users can operate on files and folders without leaving the command surface.

Confirmed core verbs:

```text
/copy
/move
/rename
/delete
```

These are app-owned operations, not PowerShell aliases.

#### `/copy`

Immediate copy from source to destination:

```text
/copy @selection @projects
/copy @projects\build.exe @thisfolder
```

`/copy` requires a destination. It does not mean "copy to clipboard."

Clipboard copy remains the familiar `Ctrl+C` GUI/keyboard behavior.

#### `/move`

Immediate move from source to destination:

```text
/move @selection @projects
/move @selection @thisfolder
```

The command may accept multi-selection through `@selection`.

#### `/rename`

Rename a target to a new name:

```text
/rename @selection README.md
```

`F2` remains the fastest GUI/keyboard path for ordinary single-item rename.

The command exists so rename remains available from the command-driven filesystem vocabulary. Advanced bulk rename syntax is not required for version one.

#### `/delete`

Delete a target through the app-owned Windows-native delete path:

```text
/delete @selection
```

Normal delete behavior follows the user's Windows Recycle Bin semantics/settings where supported.

`/delete` is not shorthand for permanent deletion.

#### Command Grammar

```text
/copy   <source> <destination>
/move   <source> <destination>
/rename <target> <new-name>
/delete <target>
```

References compose naturally with these commands.

#### Clipboard Boundary

Do not add `/paste` merely to mirror clipboard behavior.

```text
Ctrl+C / Ctrl+X / Ctrl+V
→ clipboard workflow

/copy source destination
/move source destination
→ immediate app-owned filesystem action
```

#### Operation Journal

These commands may participate in app-owned `/history` and `/undo` according to the previously defined narrow undo rules.

`/move` and `/rename` are expected undo candidates.

`/delete` uses Windows-native recoverability where available.

`/copy` is not guaranteed undoable in version one.

#### Principle

> The command bar should be able to operate the filesystem, not just launch utilities.

### Topic 5Q — `/where` and `/find` Are Distinct Rich-View Commands — Confirmed

`/where` and `/find` both remain in the command vocabulary because they answer different questions.

#### `/where`

`/where` is for discovering the related filesystem footprint of a program, tool, executable, or installed application.

Examples:

```text
/where python
/where codex
/where "Visual Studio Code"
```

It may inspect relevant sources such as executable locations, PATH entries, common installation locations, user-level application folders, configuration/data locations, and other program-related paths where appropriate.

Its result is a rich view:

```text
Files · Where — python
```

The goal is not merely to find a filename. The goal is to answer:

> Where does this program/tool live on this system?

#### `/find`

`/find` is filesystem search.

By default, it searches within the current underlying Files location.

Examples:

```text
/find config.json
/find *.md
/find README
```

A search scope may also be passed explicitly using a reference/path:

```text
/find config.json @projects
/find *.png @thisfolder
```

The current Files location remains the implicit scope when no explicit scope is supplied.

Its result is also a rich view:

```text
Files · Find — config.json
```

The goal is:

> Find matching files/folders in this filesystem scope.

#### Rich-View Behavior

Both commands use the established temporary Files rich-view model:

- they do not become Back/Forward navigation-history entries,
- Back/Esc returns to the underlying Files state,
- results use explicit actions such as Open or Go to,
- rich-view rows do not redefine `@selection`,
- keyboard and mouse interaction are both supported.

#### Principle

> `/where` discovers a program's footprint. `/find` searches a filesystem scope.

### Topic 5R — `/info` Rich Inspection — Confirmed for Version One

`/info` is the app-owned rich inspection command for filesystem targets.

It is intentionally more useful and focused than simply recreating the Windows Properties dialog.

#### Core Forms

```text
/info @selection
/info @thisfolder
/info @projects
/info path\to\item
```

`/info` accepts a single item, a folder, or multiple selected items.

**Implemented, 2026-08-27:** bare `/info` describes the current selection, or the visible folder when
nothing is selected (DECISIONS.md, 2026-08-27).

#### Single-Item Information

Always-useful fields should be prioritized:

```text
Name
Type / extension
Full path
Size
Created
Modified
```

The full path should be easy to copy.

Additional fields appear only when relevant to the target type.

Examples:

**Implemented, 2026-08-27:** "publisher" below is shipped as **Company**. The name inside a file is a
claim, not a verified signer, and Filekin does not check Authenticode signatures in v1 — that stays
with Windows Properties (DECISIONS.md, 2026-08-27).

```text
Executable
→ architecture, version, publisher when available

Image
→ dimensions, format

Audio/video
→ duration, format and useful media metadata

Text/code
→ encoding, line count when practical

Folder
→ total size, file count, folder count
```

Do not render meaningless empty metadata fields.

#### Folder Aggregation

A folder is treated as an aggregate target.

Example:

```text
/info @thisfolder
```

may show:

```text
Size       2.84 GB
Files      1,482
Folders    96
Modified   Aug 24, 2026
Path       D:\Projects\My Project
```

Recursive folder-size/count calculation must not freeze the Files workspace.

For large trees, open the rich view immediately and calculate asynchronously:

```text
Size       Calculating…
Files      18,420…
Folders    1,203…
```

Results update progressively or when calculation completes.

#### Multi-Selection Aggregation

`/info @selection` summarizes the complete selection rather than displaying a stack of individual property sheets.

Useful aggregate fields include:

```text
item count
file count
folder count
total size
common location when applicable
oldest/newest or useful modified-time summary where appropriate
```

Example:

```text
37 items
Total Size   684 MB
Files        31
Folders      6
Location     D:\Projects\My Project
```

#### Optional / On-Demand Information

Expensive or uncommon information should not be calculated merely because Info opened.

Example:

```text
Checksum / SHA-256
→ [Calculate]
```

This avoids hashing very large files unnecessarily.

#### Windows Properties Escape Hatch

The Info rich view may provide:

```text
[Windows Properties]
```

for deep operating-system functionality such as advanced permissions, compatibility, signatures, ACLs, and other native property pages.

The app does not need to recreate those systems.

#### Rich-View Semantics

`/info` follows the established rich-view model:

- temporary command-driven Files surface,
- Back/Esc returns to underlying Files,
- not added to Forward navigation history,
- mouse and keyboard accessible,
- rich-view rows do not redefine `@selection`.

#### Principle

> `/info` answers what is useful to know about this filesystem target right now.

### Topic 5S — `/places` and `/drives` System Navigation Rich Views — Confirmed for Version One

`/places` and `/drives` are confirmed app-owned navigation/discovery commands.

They exist because the permanent Locations sidebar is intentionally personalized rather than filled with every Windows/system destination.

The separation is:

```text
Locations sidebar
→ user/project-specific saved locations

/places
→ standard Windows/user folders

/drives
→ available filesystem volumes/drives
```

#### `/places`

`/places` opens a deliberately short temporary rich Files view containing the most common Windows folders plus registered cloud-storage sync roots.

The fixed common entries, in order, are:

```text
Desktop
Documents
Downloads
Pictures
Music
Videos
```

Home/user profile is intentionally not a Place. Only common folders that actually resolve on the current system should be shown.

After the common entries, Filekin lists cloud sync roots registered for the current Windows user. Discovery uses the Windows storage-provider sync-root registration, including registered legacy sync roots, and consumes the provider/account display name and filesystem path supplied by Windows. Do not infer cloud services from installed processes, hardcode vendor folder names, or scan the user profile. Multiple registered accounts remain separate entries. Exact duplicate paths are shown once. A cloud service exposed as a mounted drive belongs in `/drives`.

The purpose is quick access to standard Windows destinations without permanently cluttering the sidebar.

Example:

```text
/places
→ Files · Places
```

Each place is an actionable navigation target. Choosing one navigates the underlying Files tab to that location and closes/replaces the temporary rich view as appropriate.

#### `/drives`

`/drives` opens a temporary rich Files view of assigned filesystem drives/volumes.

Example:

```text
/drives
→ Files · Drives
```

Each row provides concise identifying and capacity information when available:

```text
ROOT   LABEL       TYPE        SPACE
C:\    Windows     Local       218 GB free of 476 GB
D:\    Projects    Local       640 GB free of 1.8 TB
E:\    Backup      USB         1.2 TB free of 2 TB
Z:\    Team        Network     Unavailable
```

Capacity may also be represented by a restrained usage bar. Assigned removable, optical, or network drives that are disconnected or have no media remain visible but disabled with a concise `Unavailable` or `No media` state. Enumeration must not block the UI while trying to wake an unavailable device or network mapping.

The view should prioritize quick identification and navigation rather than becoming a disk-management utility.

Places and available drives are pure navigation actions rather than selection surfaces. Single-click or Enter navigates the current Files tab to the target/root and dismisses the rich view. Unavailable drive rows do not navigate.

#### Relationship to `/disk`

`/drives` answers:

> What drives/volumes can I go to?

`/disk` may answer:

> How is storage being used?

Do not overload `/drives` with deep storage analysis if `/disk` remains a separate command.

#### Rich-View Semantics

Both commands follow the established rich-view model:

- command-driven temporary Files surfaces,
- not Back/Forward navigation-history entries,
- Back/Esc dismisses the rich view,
- keyboard and mouse accessible,
- explicit navigation actions,
- rich-view focus does not redefine filesystem `@selection`.

#### Principle

> Keep personal locations persistent; summon system locations when needed.

### Topic 5T — `/recent` Excluded From Version One — Confirmed

`/recent` is intentionally not part of the v1 command vocabulary.

The existing navigation model is expected to make returning to useful locations sufficiently fast through Files tabs, per-tab Back/Forward navigation, personalized Locations, `/places`, `/drives`, `/find`, and other direct navigation mechanisms.

Adding `/recent` would require defining and maintaining ambiguous activity semantics such as recently opened files, visited folders, modified items, or operated-on items. Operation history is already owned by `/history`.

The architecture may revisit a Recent/workspace-resumption feature after real usage demonstrates a concrete need, but v1 should not collect or expose recent activity merely because traditional file explorers do.

> Do not add navigation history surfaces until the existing navigation model proves insufficient.

### Topic 5U — `/disk` Excluded From Version One — Confirmed

`/disk` is intentionally excluded from the v1 command vocabulary and is not replaced by `/space`, `/storage`, or another alias at this time.

Its originally proposed responsibilities are sufficiently covered for v1 by two clearer features:

```text
/drives
→ discover/navigate drives and see concise capacity/free-space information

/info <target>
→ inspect file, folder, or selection size and metadata
```

Whole-drive recursive storage-consumption analysis may be useful later, but it introduces substantial scanning, permissions, junction/symlink, progress, cancellation, and performance concerns. It also lacks a short command name whose meaning is sufficiently self-evident.

If real usage demonstrates a need for storage-consumption analysis, design that workflow separately rather than preserving `/disk` as a speculative command.

> A useful capability does not automatically deserve a v1 slash command.

### Topic 5V — `/interactive` Excluded From Version One — Confirmed

`/interactive` is not part of the v1 user-facing slash-command vocabulary.

The capability it represented remains part of the terminal architecture.

The application should identify and handle known interactive tools through the terminal-session routing/registry layer without requiring the user to understand or manage that process model.

Examples of interactive or long-running tools include:

```text
codex
claude
python
ssh
npm run dev
```

Known tools may route directly into hosted terminal tabs according to deterministic registry rules.

User-facing command syntax should not be required simply to tell the application that a tool is interactive.

#### Manual Overrides

A future advanced configuration mechanism may allow users to register or override unknown interactive tools if built-in routing is insufficient.

Persistent user-defined routing rules are explicitly excluded from v1.

#### Unknown Interactive Fallback

An unknown command initially follows the finite runspace path. If it proves to need terminal interaction, Filekin may offer **Run in terminal**.

Accepting the action starts the command again in a fresh ConPTY-backed terminal session. Filekin does not attach or migrate the already-running finite-path process into ConPTY, and accepting the action does not create a persistent user rule.

#### Principle

> Interactive-process support is infrastructure, not user-facing command vocabulary.

### Topic 5W — `/tidy` Folder Organization — Confirmed for Version One

`/tidy` is a confirmed v1 app-owned command for users who need help organizing messy folders.

Its scope is filesystem organization only. Desktop icon positioning/resorting from the earlier standalone utility is intentionally not part of the Files implementation.

#### Core Behavior

`/tidy` accepts a folder location and organizes loose files directly inside that folder into predictable category folders based primarily on known file types.

Examples:

```text
/tidy @desktop
/tidy @downloads
/tidy @thisfolder
/tidy D:\MessyFolder
```

Desktop and Downloads are ordinary target locations rather than special Tidy modes. Standard locations may be reached/discovered through `/places` and then referenced normally.

Typical deterministic categories may include:

```text
Installers
Audio
Documents
Photos
Videos
Archives
```

The exact extension/category mapping belongs in the implementation specification and may evolve without changing the command contract.

#### Conservative Scope

Version one should:

- organize loose files in the specified folder,
- leave existing subfolder organization alone,
- avoid recursively redesigning a hierarchy,
- leave unknown/unclassified file types in place,
- never silently overwrite a conflicting destination file,
- report skipped/conflicting/unclassified items clearly.

The command should be deterministic and understandable rather than using opaque AI classification for ordinary file placement.

#### Result Surface

After execution, the command bar may show a compact result such as:

```text
✓ Tidied 47 files                         View
```

`View` may open a `Files · Tidy` rich result summarizing categories, moved items, skipped items, conflicts, and unchanged items.

#### Confirmation Policy — Intentionally Unresolved

A mandatory pre-execution confirmation/preview is **not** confirmed as part of `/tidy`.

The product still needs to decide whether normal `/tidy <folder>` execution should:

1. execute immediately and report the result, or
2. show a proposed organization plan requiring confirmation.

Do not implement a mandatory confirmation merely because Tidy can move many files. This question should be evaluated against the product's broader command safety model and the predictability of the deterministic rules.

#### Undo Boundary

As previously decided, `/tidy` is not required to participate in `/undo` in v1. Reversing a potentially large inferred organization operation would materially increase undo complexity.

#### Principle

> `/tidy` organizes loose files in a specified folder; it does not redesign an existing hierarchy.

### Topic 5X — `/tidy` Executes Immediately — Confirmed

Normal `/tidy` execution does **not** require a pre-execution preview or confirmation in v1.

The command itself is an explicit user instruction:

```text
/tidy @downloads
/tidy @desktop
/tidy @thisfolder
```

Pressing Enter should begin the deterministic organization operation immediately.

The safety model comes from conservative Tidy semantics rather than confirmation friction:

- loose files only,
- deterministic known-type categories,
- existing subfolders left alone,
- unknown/unclassified items left in place,
- no silent overwrite of conflicts,
- inaccessible/conflicting items skipped and reported.

After completion, the command bar shows a concise persistent result until the next command executes:

```text
✓ Tidied 47 files · 2 skipped             View
```

`View` opens the optional rich result for users who want the breakdown.

A confirmation is appropriate only if a future Tidy capability introduces materially different/destructive semantics; it is not part of normal v1 Tidy.

> Type the command. The mess gets organized.

### Topic 5Y — Partial Success and Conflict Isolation — Confirmed

Batch commands should make progress wherever they safely can rather than becoming all-or-nothing operations.

If a batch contains independent targets and some targets fail or require attention, unrelated valid targets continue.

Example:

```text
/move @selection @projects
```

For 12 targets:

```text
9 moved
3 need attention
```

The nine successful moves are committed immediately. The three unresolved targets are isolated into the command's active rich conflict/result view.

#### Leaving the Conflict View

Back/Esc while unresolved conflicts remain means:

```text
skip unresolved targets
close the rich view
keep completed work
```

It does **not** mean undo or rollback.

The UI should avoid labeling this action simply `Cancel`, because completed work may already exist.

Keyboard guidance may communicate:

```text
Esc  Skip remaining and close
```

After leaving, the command result settles into a completed partial-success state such as:

```text
⚠ Moved 9 of 12 · 3 skipped               View
```

`View` may reopen the completed result for inspection, but skipped conflicts are no longer an active resolution session unless the command explicitly supports retrying them later.

#### Conflict Isolation

A conflict on one target should not block unrelated targets.

Examples of attention states include:

```text
destination collision
file in use
permission/elevation required
invalid target type
unavailable path/device
```

The rich view may expose appropriate per-item actions such as Retry, Skip, Rename, Replace, or Retry as administrator where supported and safe.

#### Applicability

This is the default batch-operation philosophy for app-owned commands where targets can be processed independently, including `/copy`, `/move`, `/delete`, `/unzip`, `/tidy`, and multi-target `/run`.

Individual commands may specialize behavior where atomicity is technically or semantically required.

#### Principle

> Batch operations make progress wherever they safely can. Problems are isolated for attention rather than blocking unrelated work.

> Leaving a conflict view skips unresolved work; it does not reverse completed work.

### Topic 5Z — Destination Collision Resolution — Confirmed

Explicit app-owned transfer operations such as `/copy` and `/move` resolve destination-name collisions through the active rich conflict view.

For an incoming target whose name already exists at the destination, expose three user-intent actions:

```text
Replace
Keep Both
Skip
```

#### Replace

Use the incoming target at the destination name and replace the existing destination target.

Replacement is destructive to the existing destination item. Where technically supported, replacement should use the same Windows-native recoverability philosophy established for destructive file operations rather than silently performing an unnecessarily permanent deletion.

#### Keep Both

Preserve the existing destination item and place the incoming item under a safely generated unique name.

Example:

```text
invoice.pdf
invoice (2).pdf
```

The user should not need to enter a new name merely to express "keep both."

#### Skip

Leave the existing destination item unchanged and do not transfer the conflicting incoming target.

#### Apply to Remaining Conflicts

For batches with repeated destination collisions, the conflict UI may provide one compact control:

```text
[Replace] [Keep Both] [Skip]

☐ Apply choice to remaining conflicts
```

Do not expand this into six separate `Replace All` / `Keep Both All` / `Skip All` buttons.

"Remaining conflicts" means compatible destination-name collisions in the current operation. It should not blindly apply a collision decision to unrelated error classes such as permissions or files in use.

#### `/tidy` Specialization

`/tidy` does not interrupt its fast organization flow for destination collisions.

A Tidy collision is skipped safely and reported in the result:

```text
✓ Tidied 46 files · 1 skipped             View
```

The detailed rich result can explain the collision.

This reflects the difference in user intent:

```text
/copy, /move
→ explicit transfer; user may resolve collisions

/tidy
→ automatic organization; keep moving and report exceptions
```

#### Principle

> Explicit transfer conflicts ask what the user wants. Automatic organization skips what it cannot place safely.

### Topic 6A — Privilege and Elevation Model — Confirmed

The Files application and its default PowerShell backend run with standard user privileges.

Do not run the entire application elevated by default.

#### App-Owned Operations

When an app-owned command encounters targets requiring administrator permission, continue unrelated safe work and isolate privileged targets in the active conflict view.

Example:

```text
8 moved
2 need attention

config.json
Administrator permission required

[Retry as administrator] [Skip]
```

`Retry as administrator` uses the normal Windows UAC consent flow for the privileged operation. The application does not bypass UAC.

Back/Esc skips unresolved privileged targets without reversing completed work.

#### Advanced Elevated Shell Mode

Power users may opt into an advanced setting for the PowerShell backend:

```text
Settings → Command Bar → PowerShell

Privilege
● Standard
○ Elevated
```

Standard is the default.

Starting an Elevated PowerShell session must invoke normal Windows elevation/UAC behavior. After approval, raw commands executed within that shell have the privileges Windows grants that elevated shell and child processes may inherit those privileges according to normal Windows/PowerShell behavior.

The UI should expose a persistent, subtle elevated-state indicator such as:

```text
PowerShell · Admin
```

#### App Command / Raw Shell Boundary

An elevated shell does not silently change the semantics of app-owned slash commands.

```text
/delete @selection
→ app-owned deletion/recovery/conflict rules

Remove-Item ...
→ raw PowerShell behavior and privileges
```

App-owned operations may request elevation through their explicit operation path when required, but they retain their own safety semantics.

Do not implicitly route every slash command through an elevated PowerShell merely because the user's current shell backend is elevated.

#### Principle

> Safe app commands stay safe. Raw shell power stays raw shell power.

> Elevation is explicit and uses Windows UAC; it is never silently assumed.

### Topic 6B — Locked, Read-Only, Network, and Permission Boundaries — Confirmed

#### Locked / In-Use Files

App-owned commands do not forcibly unlock files and do not terminate an owning process merely to complete a filesystem operation.

Independent batch targets continue normally. The locked target becomes an attention item:

```text
project.db
File is in use

[Retry] [Skip]
```

If Windows can reliably identify the owning application, the UI may optionally name it, but process-owner detection is not a v1 requirement.

Back/Esc skips unresolved locked targets and keeps completed work.

#### Read-Only Files

The read-only attribute is not itself an error for operations that do not need to modify the protected content/target.

Examples that normally proceed:

```text
open
read
copy
/info
/find
```

Moving a read-only file should not automatically be treated as a conflict solely because the attribute exists, and its read-only state should be preserved where normal Windows filesystem behavior permits.

When an app-owned operation actually needs to modify, replace, or delete a read-only target and Windows requires the attribute to be handled, surface an attention item:

```text
settings.ini
File is read-only

[Continue] [Skip]
```

`Continue` explicitly authorizes the requested app-owned operation to handle the read-only attribute only as necessary to complete that operation.

Do not silently strip read-only state from a resulting file when doing so is unnecessary.

#### Network Shares

Network-share failures are ordinary attention states.

Examples include:

```text
share unavailable
network disconnected
authentication required/failed
access denied
```

The app does not build a parallel credential-management system. Authentication and credential handling remain Windows/network-provider responsibilities.

#### Protected/System Locations

Protected targets use the established permission/elevation model:

```text
[Retry as administrator] [Skip]
```

Normal Windows UAC/security boundaries remain authoritative.

App-owned commands do not invent bypasses around Windows protections.

#### ACL / Permission Editing

Advanced ACL ownership, inheritance, and permission editing are outside the Files v1 command surface.

Use native Windows Properties or raw PowerShell for those tasks.

#### Principle

> Files handles ordinary permission problems clearly; Windows remains the authority for security, credentials, and access control.

### Topic 6C — Intelligent Work Delegation and Task Tabs — Confirmed

The application chooses the surface best suited to an operation's lifetime and interaction model.

> Work goes to the surface best suited to its lifetime.

Users should not normally need flags such as `--background` or confirmation prompts merely to decide where work runs.

#### Short Filesystem Work

Fast operations remain associated with the command bar and its compact result model.

```text
/copy @selection @backup
→ Copying 38 files… 62%             View
→ ✓ Copied 38 files                 View
```

Archive extraction and compression begin on their shared preview surface but are owned by the Files
workspace after execution starts. Dismissing that surface detaches presentation only; it does not
cancel the operation or discard its plan. A command-bar task row remains visible with progress,
View, and Stop. Cancellation is an explicit Stop action. Only one archive operation runs at a time in
version one, and another `/zip` or `/unzip` request reports that existing task instead of replacing it.

#### Long-Running Filesystem Work

Substantial independent filesystem operations may be intelligently delegated to a dedicated task tab.

Candidates include:

```text
/copy
/move
/unzip
/tidy
exceptionally large /delete
```

Delegation may consider operation type, estimated bytes, item count, recursive scope, elapsed time, and whether the work benefits from persistent controls/conflict handling.

The exact threshold is implementation-tunable and should not be part of the public command contract.

Example:

```text
Files | Projects | Copying 184 GB…
```

A task tab is an operation surface, not a filesystem navigation location.

It may show:

```text
Copy

Projects → Backup

78%
142.6 GB / 184.1 GB
8,421 / 10,204 files
Current: assets\models\character.lwo

3 need attention

[Pause] [Cancel]
```

Unrelated safe work continues while conflicts accumulate according to the partial-success model.

The originating Files tab remains usable immediately.

#### Task Tab Lifecycle

While running, the tab communicates active state.

When complete, it transitions to a completed result state such as:

```text
✓ Copy · Backup
```

Do not automatically destroy the completed tab merely because work finished. The user may inspect the result and close the tab when finished.

A compact command-bar result may provide `View` to focus the associated task tab.

#### Inspection / Discovery Work

Long duration alone does not force a task tab.

Commands whose rich view is already the requested destination should remain there and update progressively:

```text
/info
/find
/where
/places
/drives
```

Example:

```text
/info @thisfolder

Size       Calculating…
Files      281,492…
Folders    19,284…
```

#### Process Work

`/run` follows the terminal/process routing architecture.

Long-running or interactive processes belong in the appropriate hosted terminal/process tab rather than a filesystem task tab.

#### Cancellation Semantics

A task-tab Cancel action stops remaining work; it does not imply rollback of already completed independent items unless a specific operation explicitly supports transactional rollback.

This matches the established partial-success philosophy.

#### Principle

```text
short filesystem work
→ command bar/result

long filesystem work
→ task tab when useful

inspection/discovery
→ updating rich view

interactive/long-running process
→ terminal/process tab
```

## Implementation Architecture — Desktop Framework

### WPF Selected for Version One

The Windows desktop application will use:

```text
C#
.NET
WPF
```

WPF is selected for its mature Windows desktop ecosystem, strong .NET integration, extensive documentation, and suitability for an application combining filesystem interaction, process/shell management, keyboard-heavy interaction, rich custom UI surfaces, and Windows-specific APIs.

The product is Windows-first. Cross-platform framework abstraction is not a v1 requirement.

### Framework Is Not the Visual Design

WPF is the application framework only.

**Do not use stock/default WPF visual styling as the product design.**

The product should have a deliberately designed modern terminal/developer-tool aesthetic consistent with the UX specifications: compact controls, strong keyboard interaction, modern tabs, rich Files surfaces, restrained chrome, clear state indicators, dark/light theme support, and command-bar-centric interaction.

Default WPF control appearances, generic enterprise-style templates, and visually dated stock layouts must not be treated as acceptable final UI merely because they are functional.

Custom styles/control templates and appropriate modern Windows/WPF resources may be used where they support the product design.

> WPF is the machinery underneath the interface, not the visual identity.

### Performance Rules

WPF and modern .NET are considered sufficient for the product's performance requirements.

Responsiveness depends primarily on implementation architecture.

Hard rule:

> Never block the UI thread with filesystem, recursive scanning, hashing, shell/process I/O, or other potentially long-running work.

Use asynchronous/background work where appropriate for:

```text
filesystem enumeration
recursive folder size/count calculation
/find
/tidy
copy/move/delete work
archive extraction
checksum calculation
PowerShell/process output
```

Large file/folder views must use UI virtualization so the application does not create/render controls for every item simultaneously.

Long-running work follows the previously defined task/rich-view/process delegation architecture.

### Future Portability Boundary

Although v1 uses WPF, keep core product logic separated from the WPF presentation layer where practical.

Framework-independent/core candidates include:

```text
command parsing
@ reference resolution
Tidy rules/engine
operation planning
history/journal models
task models
shell abstractions
command contracts
```

Windows-specific services should also be isolated behind clear service boundaries where practical.

This does not make the application cross-platform, but it reduces unnecessary coupling and makes a future UI/framework migration less destructive if that ever becomes a real product goal.

## Implementation Architecture — Filesystem and Windows Integration

### Hybrid Filesystem Approach — Confirmed

Use standard .NET filesystem APIs for ordinary filesystem work and selective Windows-native/Shell APIs only where Windows already owns important operating-system behavior.

Conceptually:

```text
CUSTOM WPF UI
      ↓
APP SERVICES
      ↓
.NET FILESYSTEM APIs
+ SELECTIVE WINDOWS APIs
```

#### .NET Filesystem APIs Own Ordinary File Work

Prefer standard modern .NET APIs for:

```text
enumerating files/folders
reading basic metadata
copy/move/rename
directory creation
file watchers
stream/file access
async/background filesystem work
```

Use the simplest reliable .NET implementation that satisfies the product behavior.

#### Windows APIs Own Windows-Specific Behavior

Use selective Windows-native/Shell APIs where needed for:

```text
Recycle Bin behavior
default file associations / Open behavior
known/special Windows folders
UAC/elevation
native Windows Properties
shortcuts and shell-specific metadata where required
Windows-specific file type/icon information where useful
network/share integration where Windows already provides the behavior
```

Do not reimplement these operating-system behaviors merely to avoid Windows APIs.

#### Do Not Use Windows Shell UI as the Product Interface

Windows APIs are infrastructure, not visual design.

Do not embed or recreate Explorer's UI as the main experience.

Avoid treating native Explorer navigation panes, giant native context menus, standard shell chrome, or Explorer-like layouts as the product design.

The user-facing experience remains:

```text
custom Files workspace
terminal-leaning command bar
custom tabs
custom rich views
compact context menus
strong keyboard interaction
custom visual identity
```

> Use Windows where Windows is the operating system. Do not let Windows Explorer define the interface.

### Engineering Guardrails

The implementation must optimize for a reliable, simple application that behaves predictably and is maintainable by humans and coding agents.

#### No Speculative Product Invention

When the specification is clear, implement the specification.

Do not add:

```text
unrequested AI features
extra slash commands
extra navigation systems
generic dashboard widgets
"smart" automation not in the docs
new settings simply because they seem useful
```

Product changes belong in the design/decision process before implementation.

#### No Generic AI-Generated UI

Avoid generic AI-designed visual patterns such as:

```text
oversized rounded cards everywhere
decorative gradients without purpose
random icon-heavy dashboards
bloated settings surfaces
unnecessary nested panels
large empty hero areas
generic SaaS/application chrome
```

The established visual target is restrained, terminal/developer-tool-like, fast, compact, and purpose-driven.

#### Prefer Boring, Reliable Code

For ordinary engineering problems, prefer standard platform/framework capabilities over custom abstraction layers.

Do not create unnecessary manager/service/pipeline layers merely to make the code appear architecturally sophisticated.

Examples:

```text
use Directory/File APIs when sufficient
use established async patterns
use Windows APIs for Windows-owned behavior
use clear service boundaries only where responsibilities are genuinely different
```

#### Avoid God Classes and Duplicate Logic

Keep major responsibilities separated, but do not over-fragment the codebase.

Likely meaningful boundaries include:

```text
CommandParser
ReferenceResolver
FileOperationService
ShellBackend
TabManager
TaskManager
HistoryJournal
TidyEngine
WindowsIntegrationService(s)
```

Do not duplicate file-operation, validation, conflict, or reference-resolution logic across views/commands.

#### No Fake Completion

Do not present placeholder, stubbed, mocked, or TODO-heavy behavior as complete.

If a feature is incomplete, keep it visibly incomplete in development and do not wire fake success states into the production path.

#### Error Integrity

Do not swallow exceptions merely to make the app appear stable.

Translate known errors into the product's attention/result model, and log/propagate unexpected failures appropriately for debugging.

#### Minimize Dependencies

Add third-party dependencies only when they materially improve correctness, maintainability, or a difficult integration.

Do not add libraries merely to save a small amount of straightforward code.

#### Stable Code Is Not Rewritten for Preference

Coding agents should not refactor or replace working, well-structured code simply because another pattern is fashionable or personally preferred.

Changes should have a concrete product, correctness, performance, or maintainability reason.

#### Principle

> Reliable and simple beats clever.

> When the specification is clear, implement the specification. Do not invent the product while coding it.

## Implementation Architecture — Files Command Bar vs. Terminal Tabs

### Files Command Bar Follows the Current Files Location — Confirmed

The Files command bar belongs to the Files workspace and uses the current Files location as its shell working directory.

Example:

```text
Files:
D:\Projects\Website\assets

Command bar:
PS D:\Projects\Website\assets> _
```

When the user navigates Files:

```text
D:\Projects\Website\assets
→ D:\Projects\Website\src
```

the Files command bar follows:

```text
PS D:\Projects\Website\src> _
```

The command bar does not maintain a hidden independent working directory.

#### Mental Model

> The Files command bar belongs to Files. Terminal tabs belong to themselves.

The command bar is not a separate terminal workspace embedded below Files. It is the command interface for the currently visible Files location, with PowerShell as the raw shell language in v1.

```text
FILES TAB
   │
current Files location
   │
COMMAND BAR
  ├─ / app commands
  └─ PowerShell input
       ↓
    runs in current Files location
```

#### Independent Shell Work Uses Terminal Tabs

If the user wants an independent PowerShell working directory/session, they can launch PowerShell from the command bar:

```text
powershell
```

Known interactive-shell routing opens a terminal tab:

```text
[Files] [PowerShell]
```

That terminal starts in the Files location from which it was launched, then owns its own persistent shell process and working directory.

Example:

```text
Files launch location:
D:\Projects\Website\src

PowerShell terminal:
PS D:\Projects\Website\src>
```

After launch, Files may navigate elsewhere without affecting that terminal tab.

Users may launch multiple PowerShell terminal tabs, each with independent process state, history, environment, and working directory.

The same terminal-tab independence applies to other hosted interactive processes/tools such as Codex, Claude, Python REPL, SSH, and future supported shells.

#### Reference Behavior

`@thisfolder` always refers to the current Files location.

Because the Files command bar also runs shell input from that location, common shell and app-command context remain visually aligned.

Independent terminal tabs do not implicitly follow `@thisfolder` after launch; they own their own session state.

#### Principle

> The visible Files location is the command bar's working context. Separate shell contexts require separate terminal tabs.

## Implementation Architecture — PowerShell Runspace and Terminal Hosting

### Files Command Bar Uses a Persistent PowerShell Runspace — Confirmed Direction

The Files command bar should use a persistent hosted PowerShell runspace rather than launching a fresh PowerShell process for every finite command.

The runspace preserves PowerShell session state such as:

```text
variables
aliases
functions
loaded modules
environment/session state
current PowerShell location
```

This allows ordinary command-bar PowerShell to behave like a continuous session while remaining integrated with the Files workspace.

Example:

```powershell
$x = "hello"
Write-Output $x
```

should preserve `$x` between command executions within the same Files command-bar session.

### Bidirectional Filesystem Location Synchronization

The current filesystem location of the Files command-bar runspace is synchronized with the visible Files location.

```text
Files navigation
→ update runspace filesystem location

PowerShell cd / Set-Location to filesystem path
→ update visible Files location
```

Examples:

```powershell
cd D:\Projects
Set-Location D:\Projects
cd ..
```

should visually navigate the current Files tab when they result in a filesystem-backed PowerShell location.

The application process-wide current directory should not be used as the primary synchronization mechanism. The runspace owns its own PowerShell location state.

### Non-Filesystem PowerShell Providers

PowerShell can navigate to providers that are not filesystem paths, such as the Registry.

Example:

```powershell
cd HKLM:\
```

The Files workspace must not pretend these locations are filesystem directories.

The requested provider context is delegated to a new ConPTY-backed PowerShell terminal initialized at that provider location.

The Files runspace remains/restores to its previous filesystem location. The new terminal is intentionally a fresh PowerShell session; arbitrary variables, functions, aliases, modules, or other in-process runspace state are not transferred.

Do not implement a Registry/provider browser inside Files merely to mirror PowerShell provider navigation.

### Interactive Programs Use ConPTY-Backed Terminal Tabs

A PowerShell runspace is appropriate for finite command-bar execution and persistent PowerShell state, but it is not a substitute for a real terminal surface.

Interactive programs that require terminal semantics belong in hosted terminal tabs backed by Windows ConPTY.

Examples include:

```text
powershell / pwsh
cmd
codex
claude
python REPL
ssh
other interactive TUIs/CLIs
```

Conceptually:

```text
Terminal Tab UI
      ↕
    ConPTY
      ↕
shell / interactive process
```

Terminal tabs own their own independent process lifetime, working directory, environment/session state, and terminal interaction after launch.

### Command Routing

The intended execution architecture is:

```text
FILES COMMAND BAR
       │
  Command Router
   ╱         ╲
  ╱           ╲
/ app       ordinary shell
commands       input
  │             │
Files Core   PowerShell Runspace
                │
                ├─ finite/non-interactive work
                │    → command-bar result / View
                │
                └─ known interactive work
                     → Terminal Tab
                          │
                        ConPTY
```

Known interactive tools should be routed deterministically using the existing interactive-tool registry/routing architecture.

### Prototype / Technical Spike Requirement

Before relying on this architecture throughout the product, implementation should include a focused technical spike that proves:

1. A persistent PowerShell runspace can execute the normal finite command-bar workloads.
2. Variables, aliases/modules, and session state persist as expected.
3. Filesystem `Set-Location` / `cd` can be detected and synchronized back into the visual Files view.
4. Files navigation can reliably set the runspace filesystem location.
5. Native finite commands such as `git status` behave correctly through the command-bar path.
6. Known interactive commands can be routed into a ConPTY terminal tab.
7. Unexpected interactive/process behavior does not leave the Files command bar permanently hung.

The spike is allowed to be disposable prototype code. Its purpose is to validate the integration boundary before broad implementation.

### Shell Backend Abstraction

Do not spread direct PowerShell SDK calls throughout the UI.

The Files command bar should communicate through the previously established shell-backend abstraction.

Version one implementation:

```text
ShellBackend
└── PowerShellRunspaceBackend
```

Future shell backends may use a different execution mechanism.

Terminal hosting should likewise be isolated behind a terminal/process service rather than embedded directly in view code.

### Principle

> Use the PowerShell runspace for Files-aware shell state; use ConPTY for real terminal sessions.

> Filesystem shell location can synchronize with Files. Terminal tabs remain independent.

### Topic 6D — Files/Command-Bar Context Must Never Diverge — Confirmed

The visible Files hierarchy and the Files command-bar shell context are always synchronized to the same filesystem-backed location.

This is a hard product rule:

> The Files view and its command bar must always follow each other.

The Files command bar is not allowed to enter a PowerShell location that the Files hierarchy cannot represent.

#### Filesystem Locations

Filesystem-backed PowerShell navigation remains bidirectionally synchronized:

```powershell
cd D:\Projects
Set-Location D:\Projects
cd ..
```

results in the visible Files hierarchy navigating to the resulting filesystem location.

Likewise, GUI navigation updates the command-bar runspace location.

#### Non-Filesystem PowerShell Providers

If command-bar PowerShell attempts to enter a non-filesystem provider, such as:

```powershell
cd HKLM:\
```

the Files command bar does not remain in that provider while the Files hierarchy stays elsewhere.

Instead, the requested location is delegated to a new independent ConPTY-backed PowerShell terminal initialized at that provider path.

Conceptually:

```text
Files command bar:
cd HKLM:\

        ↓

open PowerShell terminal tab
        ↓

terminal owns HKLM:\ context
Files remains in its filesystem location
```

The Files command-bar runspace remains synchronized with the visible Files filesystem location.

The terminal is a fresh PowerShell session. Filekin does not migrate the hosted command-bar runspace or copy arbitrary runspace variables, functions, aliases, modules, or other session state into it.

The application must not implement a fake Registry/provider hierarchy merely to keep the command inside Files.

#### General Rule

Any shell context that cannot be faithfully represented by the Files hierarchy belongs outside the Files command-bar context.

Examples may include non-filesystem PowerShell providers or other future shell states that do not map to a filesystem location.

Those contexts belong in an independent terminal tab.

#### Principle

> If Files cannot represent the shell location, the shell location does not belong in the Files command bar.

> Files and the Files command bar are one filesystem context; terminal tabs are where independent shell contexts live.

## Implementation Architecture — Terminal Tab Hosting and Lifecycle

### Terminal Tabs Host a Shell — Confirmed

A hosted terminal tab owns a real shell session rather than making the requested interactive tool itself the terminal's root process.

Version one:

```text
Terminal Tab
    ↓
ConPTY
    ↓
PowerShell
    ↓
interactive tool/process
```

Examples of interactive tools/processes include:

```text
Codex
Claude
Python REPL
SSH
other CLI/TUI programs
```

If the user launches an interactive tool from the Files command bar, the application creates a terminal tab whose PowerShell shell starts in the current Files location and then automatically invokes the requested tool.

Example:

```text
Files location:
D:\Projects\App

User enters:
claude

Result:
[Files] [Claude · App]

Internal session:
PowerShell at D:\Projects\App
└── claude
```

### Why the Shell Is the Root Process

The shell remains the stable owner of the terminal session.

When the launched tool exits, normal shell behavior resumes:

```text
Claude exits
↓
PS D:\Projects\App>
```

The user may then run another command in the same terminal tab.

Do not create a special replacement-shell mechanism after each interactive tool exits.

### Terminal Working Directory

A newly created terminal tab inherits the current Files filesystem location at launch.

After launch, the terminal tab becomes independent.

Subsequent Files navigation does not alter the terminal's working directory, and terminal `cd` commands do not alter Files.

```text
launch location is inherited once
↓
terminal owns its session thereafter
```

### Terminal Tab Naming

When a terminal tab is created specifically to launch an interactive tool, its visible title should initially communicate the user's intent rather than exposing the implementation detail that PowerShell is the root shell.

Examples:

```text
Claude · App
Codex · Website
SSH · server-name
Python · scripts
```

Exact title-update behavior may be refined later, but v1 should prefer the launched tool/context over a generic `PowerShell` label when a tool caused the tab to be created.

A terminal opened explicitly as PowerShell may simply use:

```text
PowerShell
```

### Child Process Exit

When the launched interactive child process exits, the terminal tab remains open because its PowerShell shell is still running.

This follows ordinary shell behavior.

### Root Shell Exit

When the underlying/root PowerShell process exits, the terminal session is over and the terminal tab closes.

Examples include:

```text
user enters exit
root shell terminates normally
root shell is otherwise ended
```

Do not leave behind a permanent "exited terminal" tab with Restart/Close controls.

If the root shell terminates unexpectedly, the tab still closes. The application may surface a brief non-blocking error/status indication when useful for diagnosing an abnormal exit.

### Terminal Input Semantics

Once inside a terminal tab, input is normal terminal/shell input.

The Files slash-command language and `@` reference language are properties of the Files command bar, not automatic rewrites of arbitrary input inside an independent terminal tab unless a future explicit feature says otherwise.

Do not intercept ordinary terminal keystrokes merely to make the terminal mimic the Files command bar.

Terminal control behavior such as Ctrl+C should be passed through according to normal ConPTY/shell semantics unless the application has an explicit reserved UI shortcut.

### Principle

> Terminal tabs are real shell sessions.

> Tools run inside the shell; when the tool exits, the shell remains. When the shell exits, the tab closes.

> Files provides the launch context once. The terminal owns itself after launch.

## Implementation Architecture — `/run` and the Terminal Fallback

### Parsing Happens Before the Shell Rewrite — Implemented

`/run` is a structured app command, so it must not be flattened into a shell string first. Ordinary
input goes through `ReferenceResolver.ResolveLine`, which rewrites `@` references into
PowerShell-quoted text; that would destroy the boundary between the target and its arguments and
would turn a multi-item `@selection` into one quoted list.

`RunInvocationParser` (`Filekin.Core.Commands.App.Run`) therefore runs **before** the rewrite. It
tokenizes with the app-command tokenizer, then expands each token through
`IReferenceResolver.ResolveToken`, a structured sibling of `ResolveLine` that returns real paths and
leaves a non-reference token alone. The first token yields the target list — which `@selection` may
expand to several — and the remaining tokens become process arguments. Arguments are refused when the
target expanded to more than one item, because there is no way to attribute them.

### Routing Is Resolved From Metadata, Off the UI Thread — Implemented

`WindowsRunTargetResolver` (`Filekin.Infrastructure.Windows.Commands`) turns one target into a
`RunTargetResolution`:

```text
target ─┬─ fully qualified path ──────────────► probe directly
        ├─ relative ─► visible Files folder ──► probe (with PATHEXT)
        └─ bare name ─► PATH directories ─────► probe (with PATHEXT)

resolution ─┬─ directory ────────────────────► RunTargetKind.Directory  (refused)
            ├─ registered interactive program ┐
            ├─ .bat .cmd .com .ps1 .py        ├► RunTargetKind.Terminal  (hosted ConPTY tab)
            └─ .exe with PE subsystem CUI     ┘
            └─ anything else ────────────────► RunTargetKind.External   (ShellExecute)
```

The PE subsystem is read with `System.Reflection.PortableExecutable.PEReader`, which is the same fact
Windows uses to decide whether an image needs a console. This is metadata, not a runtime heuristic:
the decision is complete before `CreateProcess`, which the spike proved is the only point at which a
pseudoconsole can be attached.

Probing walks `PATH` and opens files, so `CommandExecutor` performs the whole resolve-and-launch loop
inside `Task.Run`. A `ConPtyTerminalSession` buffers its output until the first renderer subscribes,
so starting a session off the UI thread loses none of the opening frame.

A terminal target is launched as `& 'target' 'arg' …` inside the tab's root PowerShell, single-quoted
with doubled inner quotes, so a path containing spaces or an apostrophe is passed intact.

### The Delayed Fallback Is a Watcher, Not a Promotion — Implemented

`ShellViewModel.OfferTerminalFallbackIfStillRunningAsync` sits beside the in-flight execution task; it
never touches the running process:

```text
Enter ─► CommandExecutor.ExecuteAsync(...)      (finite runspace, cancellable)
          │
          ├─ probe: is the executable a concrete console target?   (off the UI thread)
          │      no ─► nothing is ever offered
          │
          └─ yes ─► WhenAny(execution, Delay(2s, token))
                     ├─ execution finished  ─► nothing offered
                     ├─ cancellation asked  ─► nothing offered
                     └─ still running ──────► one Y/N confirm strip
                             ├─ Y ─► cancel the invocation, then start the
                             │        same command fresh in a hosted tab
                             └─ N/Esc ─► keep running, status "· Esc to stop"
```

`Y` sets a one-shot flag and cancels the shared `CancellationTokenSource`.
`PowerShellRunspaceBackend` translates the resulting `PipelineStoppedException` into
`OperationCanceledException`, which the exception filter
`catch (OperationCanceledException) when (_terminalFallbackAccepted)` uses to distinguish an accepted
fallback from a plain `Esc`. The relaunch itself is wrapped, because that catch block runs inside the
`async void` key handler that started the command: a launch failure there must become an inline error,
not an unhandled exception.

The delay observes the same token, so `Esc` ends the wait immediately rather than letting a prompt
appear seconds after the user stopped the command. The folder the command was typed in is captured
once and used for the execution *and* the relaunch, so navigating Files mid-command cannot move the
relaunch to a different directory.

## Implementation Architecture — `/info` Inspection

### Three Costs, Three Mechanisms — Implemented

The Info sheet separates what it can answer by what the answer costs:

```text
open the sheet  ─► metadata only, off the UI thread          IFileInspector
                     type, size, dates, dimensions, duration,
                     version, shortcut target, encoding

while it is open ─► recursive walk, streaming into its rows  IAggregateScanner
                     size, file count, folder count

only when asked  ─► whole-file reads                         FileChecksum
                     SHA-256, line count                     TextFileReader
```

`Filekin.Core.Inspection` owns the contracts and the result shape; `Filekin.Infrastructure.Windows.Inspection`
owns every Windows-specific reader. The App layer owns the rows and the sheet.

### Metadata Comes From One Windows API — Implemented

`WindowsFileInspector` reads type-specific rows through the Windows Property System rather than
per-format parsers:

```text
SHGetPropertyStoreFromParsingName ─► IPropertyStore ─┬─ System.Image.HorizontalSize / VerticalSize
                                                     ├─ System.Media.Duration        (100-ns units)
                                                     ├─ System.Software.ProductName
                                                     ├─ System.FileVersion
                                                     └─ System.Company

SHGetFileInfo (SHGFI_TYPENAME)  ─► the friendly type text Explorer shows
PEReader                        ─► executable architecture (already read by /run)
IShellLink + IPersistFile       ─► shortcut target, arguments, working directory
```

`IPropertyStore` is a source-generated `[GeneratedComInterface]`, reached through a `LibraryImport`
that marshals it with `ComInterfaceMarshaller<T>`. `PROPVARIANT` is declared as a deliberately
blittable struct — discriminator plus the first two union words, which covers every type read here —
and is always released through `PropVariantClear`. `SHFILEINFOW` uses fixed `char` buffers rather
than `ByValTStr`, because source-generated P/Invoke marshals only blittable types (SYSLIB1051), the
same rule that already forced a `Span<char>` on `SHLoadIndirectString`.

All of it was verified on a **thread-pool (MTA) thread** before adoption, because inspection never
runs on the UI thread and shell COM objects cannot be assumed to be apartment-free.

`IShellLink::Resolve` is never called: it can display UI and search the network for a missing target.
Only `GetPath(SLGP_RAWPATH)`, `GetArguments`, and `GetWorkingDirectory` are used, and no setter path
exists — Filekin reveals a shortcut, Windows Properties edits it.

The Properties escape hatch itself is `SHObjectProperties(hwnd, SHOP_FILEPATH, path, null)`, **not**
`ShellExecuteEx` with the `properties` verb. The verb resolves a path by file-system parsing, which
the user profile folder's properties handler rejects with `ERROR_CANCELLED` after showing the shell's
own "Unspecified error" box; it works for files, ordinary folders, `C:\Users`, and `C:\`, so the
single broken target is the one a file manager opens most (DECISIONS.md, 2026-08-27). The Filekin
window handle is passed as owner so the dialog stays with the app.

### The Scan Streams Into Its Rows — Implemented

```text
OpenInfoAsync ─► inspector (Task.Run) ─► rows built once
                                          │
                                          └─► StartInfoScan (Task.Run, own CTS)
                                                 │  every 250 ms
                                                 └─► dispatcher ─► mutate Size / Files / Folders
CloseInfo ─► cancel the CTS ─► the walk stops
```

`DirectoryAggregateScanner` walks an explicit stack rather than recursing, skips reparse points,
enumerates with `IgnoreInaccessible = false` so a refused folder is recorded instead of silently
dropped, and throttles progress to a timer.

The three live rows are **mutated in place**, never rebuilt. Rebuilding the collection on each tick
would discard the row the keyboard is on — the same defect the Places and Drives views had to fix —
and would do it four times a second.

Every published tick re-checks its own cancellation token on the dispatcher, so a scan for a sheet
the user has already left cannot write into the rows of the sheet that replaced it.

## Implementation Architecture — Workspace Surfaces

### Shared Host, Distinct Surface Types — Confirmed

Reuse workspace infrastructure without making Files, rich views, and task views visually or behaviorally identical.

```text
FilesWorkspaceHost
├── FileHierarchySurface
├── RichViewSurface
└── TaskSurface
```

Shared infrastructure may include surface hosting/switching, focus management, Back/Esc routing, command-bar integration where applicable, underlying Files-state preservation, loading/progress/error primitives, common typography/spacing tokens, status/result presentation, and lifecycle hooks.

WPF's reusable content/data templating supports this shared-host/distinct-presentation architecture.

### File Hierarchy Surface

The File Hierarchy Surface is the primary browsing/selecting filesystem experience and must remain visually unmistakable from command-driven rich views. Do not force rich-view content into filesystem row grammar merely to reuse code.

### Rich View Surface

Rich views are command-driven temporary surfaces inside the Files workspace. Examples include `/info`, `/find`, `/where`, `/history`, `/places`, `/drives`, conflict/attention views, and command-result views.

Rich views may share the workspace frame and common visual primitives, but their content is purpose-built for inspection, results, actions, and status rather than filesystem browsing. They preserve the underlying Files state and use established Back/Esc return behavior.

Examples: `Files · Info`, `Files · Find`, `Files · History`.

### Task Surface

Task tabs use the same visual design family as rich views. Reuse appropriate rich-view primitives for headings, metadata/value rows, progress, attention/error rows, actions, status language, spacing, and typography.

A task is not simply a rich view placed in another tab. Its lifecycle differs:

```text
Rich view
→ temporary command-driven surface inside Files
→ Back/Esc returns to underlying Files view

Task surface
→ persistent independent operation tab
→ continues while user works elsewhere
→ remains inspectable after completion until closed
```

Examples: `Copy · Backup`, `Tidy · Downloads`, `Unzip · Archive`.

### Visual Family, Not Visual Uniformity

Rich views and task tabs should clearly look related. They should not become identical templates with different text, nor should every feature invent a new visual language.

> Reuse the frame, primitives, and lifecycle infrastructure; preserve the identity of each surface type.

> Rich views and task tabs share a visual language, but not the same lifecycle.

## Implementation Architecture — Settings and Persistent State

### Hybrid Storage Model — Confirmed

Use different storage formats for different kinds of application state rather than forcing everything into one database or one configuration file.

```text
%AppData%\<AppName>\
├── settings.json
└── state.db
```

`<AppName>` is the actual product/application name. Do not use a generic `Files` directory unless `Files` becomes the final product name.

### `settings.json`

Human-readable JSON stores user-facing configuration and other small durable settings that advanced users may reasonably want to inspect, edit, copy, or back up.

Expected examples include:

```text
saved Locations
Location ordering
theme / appearance preferences
default shell/backend preference
advanced elevated-shell preference
small UI preferences
other simple user configuration
```

The implemented shape is:

```json
{
  "locations":   [ { "name": "projects", "path": "D:\\GitHub" } ],
  "theme":       "dark",
  "accent":      "blue",
  "openFilesAtLaunch": { "target": "home", "name": null, "path": null },
  "interactivePrograms": [ "vim" ],
  "archives": {
    "previewBeforeExtracting": true,
    "whenAFileExists": "skip"
  }
}
```

`theme` is `dark` (the default), `light`, or `system`. `accent` is a short lower-case name; the app
layer owns the set of accents it can draw and falls back to blue for one it does not recognise
**without rewriting the value**, so an accent written by a newer build survives an older one.
`openFilesAtLaunch.target` is `home`, `location`, or `folder`. `interactivePrograms` are executable
names, normalized to the same form the command classifier compares against (no directory, no
extension), that add to the built-in interactive rules and never remove one.

`archives.previewBeforeExtracting` controls the shared `/unzip` and `/zip` preview default.
`archives.whenAFileExists` is `skip` (the shipped default) or `overwrite`; command switches may
override the choice for one `/unzip` without rewriting Settings.

#### One Settings Owner

A single settings service holds the in-memory document. The Location catalog and the Settings surface
both read and mutate through it, and every mutation is a whole-file write of that one snapshot. Two
components each rebuilding the document from their own fields would silently discard the other's
half.

#### Theme Application

Both palettes define exactly the same resource keys, so applying a theme replaces one merged
dictionary and nothing else — no style is edited and no control template is rebuilt. The palette
dictionary is located by a sentinel key rather than by merge order. The accent is written above it,
directly into the application's own resource dictionary, so it survives a later theme swap.

A hosted terminal renders raw cells and never reads the resource dictionary, so its ground, default
text, caret, and selection are repainted explicitly when the theme or accent changes. Its sixteen
ANSI colours keep their standard meanings and are never accent-tinted.

`system` is resolved from the Windows app-mode preference each time the theme is applied, and
re-applied when Windows broadcasts an app-mode change; an explicit dark or light choice ignores that
broadcast.

The file should be intentionally understandable rather than generated as opaque framework serialization.

#### Advanced-User Principle

Advanced users may inspect, edit, and back up `settings.json`.

The application must therefore:

- use stable, descriptive property names,
- tolerate reasonable formatting/whitespace changes,
- validate loaded values,
- recover safely from invalid individual settings where possible,
- never place secrets/passwords/tokens into this file,
- avoid unnecessary generated metadata or framework-specific noise.

Unknown future fields should be handled gracefully where practical to support forward/backward compatibility.

#### Saved Location Management

One settings-backed Location catalog owns the ordered sidebar entries and implements command-bar named-reference resolution. The sidebar editor and `/location` command mutate that same catalog; they must not maintain separate copies.

```text
/location add <name> <path>
/location set <name> <path>
/location rename <name> <new-name>
/location remove <name>
```

`set` and the editor update configuration only. They do not move or rename the target folder. `remove` deletes only the saved pointer. Each mutation writes settings successfully before publishing the new in-memory resolver/sidebar snapshot, so a failed write cannot make the running UI disagree with durable configuration.

Successful app-owned `/move` and `/rename` results carry structured source/destination relocations.
The Location catalog applies the longest matching relocation to every saved path equal to or nested
beneath a moved source, then persists all rebased Locations in one settings write. Names, ordering,
unknown JSON fields, and Location-based startup preferences remain intact. `/copy` emits no relocation.

The filesystem and JSON settings store cannot share a native transaction. Filekin therefore uses a
compensating transaction: perform the filesystem move, durably rebase Locations, and, if persistence
fails, move the filesystem items back in reverse order. A failed compensation is surfaced as an
explicit inconsistent-state error naming the affected path; it must never be reported as success.

The synchronous filesystem port behind `/copy`, `/move`, `/rename`, and `/toss` is dispatched on a
worker thread. Recursive copies, shell Recycle Bin calls, network paths, and cross-volume work must
not block WPF's dispatcher while the richer task/progress architecture remains future work.

#### Startup Files Location

The startup-location preference controls the initial filesystem location of Filekin's Files workspace. Absence of the preference means the current user's profile folder. The setting may target either a saved Location by name or an explicit absolute filesystem path.

A saved-Location target is resolved through the same settings-backed catalog used by the sidebar and `@name` references, so changing that Location's path changes the next launch destination. Renaming a Location updates the startup reference as part of the same durable mutation. Removing the selected Location leaves no usable named target; Filekin falls back to Home at the next launch with a non-blocking notice. An unavailable explicit or saved path also falls back for that launch without erasing the preference, allowing removable/network targets to return later.

This preference is app-owned. Do not modify `$PROFILE`, inject a persistent `Set-Location`, or otherwise rewrite the startup behavior of PowerShell outside Filekin. The hosted Files runspace continues to adopt the visible Files location through its existing per-runspace synchronization, while each terminal tab continues to receive its launch context explicitly.

### `state.db` — SQLite

Use a small embedded SQLite database for state that benefits from reliable transactional writes and structured querying.

Expected examples include:

```text
persistent app-owned operation history
narrow undo metadata/state
operation/result records where appropriate
future persistent task/result metadata if justified
```

SQLite is embedded application storage, not a separate server or user-managed database.

The history model remains bounded according to the existing product decisions (approximately the most recent 100 relevant app-owned operation records unless later revised).

### Why Not Put Everything in SQLite?

Settings and Locations are intentionally inspectable and portable.

A readable JSON configuration is easier for advanced users to:

```text
understand
edit
back up
diff
restore
move between installations
```

SQLite is reserved for state where transactional reliability and structured records provide real value.

### Why Not Use the Windows Registry for Product Settings?

Do not use the Windows Registry as the primary store for ordinary application settings.

The product benefits more from transparent, portable configuration files than from hiding configuration inside Registry keys.

Registry access may still be used where Windows itself requires it for integration, but not as the default application configuration store.

### Write Safety and Recovery

`settings.json` writes should be atomic where practical (for example, write/validate temporary content and replace the prior file) so an interrupted write does not easily destroy the user's configuration.

On malformed configuration:

- preserve/recover the original file when practical,
- load safe defaults only for invalid/missing values,
- do not silently overwrite a user's damaged file before it can be inspected.

SQLite operations should use transactions for operation-history/undo changes that need consistency.

### Principle

> Human-facing configuration stays readable. Transactional application state stays reliable.

## Implementation Architecture — Tidy Integration

### Rebuild Tidy Cleanly Inside the Codebase — Confirmed

The existing standalone Tidy utility is not embedded, invoked as a subprocess, or treated as a runtime dependency of the new application.

Its behavior may be used as product/reference material, but version one implements a new internal `TidyEngine` in C#/.NET.

Conceptually:

```text
/tidy command
    ↓
TidyCommandHandler
    ↓
TidyEngine
    ↓
FileOperationService
```

### Scope of the New Tidy Engine

The new implementation includes only the Files v1 behavior already confirmed:

```text
organize loose files in a supplied folder
classify known file types into deterministic categories
leave existing subfolders alone
non-recursive by default
leave unknown/unclassified file types in place
never silently overwrite destination conflicts
skip/report conflicts
execute immediately without routine confirmation
produce compact result + optional rich result
```

Supported targets include:

```text
/tidy @desktop
/tidy @downloads
/tidy @thisfolder
/tidy <path>
```

Desktop is treated as an ordinary filesystem target.

### Explicitly Excluded Legacy Behavior

Do not carry forward the standalone utility's Desktop icon-layout automation.

Exclude:

```text
resorting desktop icons
placing folders on one side
placing application shortcuts on another side
Windows desktop icon-position manipulation
special-case desktop-shell automation
```

Those behaviors are outside the Files v1 Tidy contract.

### No Subprocess Dependency

Do not shell out to the old executable from `/tidy`.

Reasons:

- avoids duplicated behavior and version drift,
- avoids subprocess/lifecycle complexity,
- enables direct progress/conflict/result integration,
- enables reuse of app-owned file-operation validation and reporting,
- keeps Tidy testable as normal application code,
- removes dependency on legacy Desktop-specific behavior.

### Reuse Shared File-Operation Infrastructure

`TidyEngine` decides organization intent/classification.

It should not independently reimplement low-level move/conflict/error semantics that already belong to `FileOperationService`.

Conceptually:

```text
TidyEngine
→ determine category and destination
→ produce/execute move plan through shared file-operation services
```

This keeps permission handling, locked-file behavior, collision skipping, progress, task delegation, and error reporting consistent with the rest of Files.

### Classification Rules

Category/extension rules should be explicit, deterministic, and maintainable as data/configuration or a clearly isolated ruleset.

Do not use opaque AI classification for ordinary v1 Tidy behavior.

The exact category mapping can evolve without changing `/tidy`'s public command contract.

### Principle

> Rebuild the useful behavior, not the old application's implementation.

> Tidy belongs inside Files as a first-class engine, not as a legacy executable bolted onto it.

## Implementation Architecture — Packaging, Installation, and Updates

### Dual Distribution Model — Confirmed

Version one should ship through two direct-download distribution paths:

```text
1. Traditional Windows installer
2. Portable ZIP
```

Both use the same self-contained .NET application build underneath.

The product is not required to ship through the Microsoft Store.

### Self-Contained .NET Deployment

Publish the application self-contained so users do not need to separately install the matching .NET runtime.

Conceptually:

```text
WPF application
+ required .NET runtime
+ required app dependencies
→ distributable application payload
```

The installer and portable ZIP are packaging choices around the same application payload.

### Traditional Installer

Provide a conventional Windows installer for users who prefer a normal installed application experience.

The installer should handle:

```text
install location
Start Menu entry
optional desktop shortcut if offered
uninstall registration
clean uninstall
upgrades/reinstallation
application versioning
```

Prefer a simple, maintainable installer technology such as Inno Setup unless a concrete requirement later justifies a more complex installer toolchain.

The installer is delivery infrastructure, not a place for custom product logic.

### Portable ZIP

Also provide a portable ZIP release.

Expected behavior:

```text
download ZIP
extract anywhere
run application
no installation required
```

The portable release uses the same self-contained application binaries.

By default, portable execution does not imply "all user state beside the EXE." User settings/history continue using the established `%AppData%\<AppName>\` storage model unless a future explicit true-portable-data mode is designed.

### No Microsoft Store Requirement

Do not make Microsoft Store distribution part of the v1 release plan.

The release model is direct distribution controlled by the project.

### Code Signing

A paid code-signing certificate is not a v1 requirement.

The project may initially distribute unsigned installer and portable builds.

Do not create a self-signed certificate merely to imply public trust. If trusted signing becomes worthwhile later, revisit it as a release/infrastructure decision.

Unsigned direct-download builds may receive Windows reputation/SmartScreen warnings; this should be treated as a distribution/trust limitation rather than an application malfunction.

### Update Philosophy — User Controlled

The application may check for updates, but it must not silently or forcibly install them.

Normal update UX:

```text
Update available: 1.2.0

[View Changes] [Update] [Later]
```

The user decides whether and when to update.

#### Installer Build Updates

For installed builds, an approved update may download the newer installer and launch it through the normal upgrade path.

Do not build a complicated custom patching/updater subsystem unless a real need appears.

#### Portable Build Updates

For portable builds, v1 may simply direct/download the newer portable release rather than attempting in-place self-replacement.

A more sophisticated portable updater can be revisited later.

### Release Principle

> Offer a normal installer for convenience and a portable build for control.

> Updates may be offered; installation remains the user's choice.

> Packaging should make the app easier to use, not become another product subsystem.

## Repository and Licensing Direction

The implementation is intended for a public GPLv3 open-source repository.

When repository setup begins, create and maintain the appropriate project-level community files, including at minimum:

```text
LICENSE
README.md
CONTRIBUTING.md
SECURITY.md
```

A Code of Conduct may also be included as the contributor community develops.

Licensing/community infrastructure should remain simple unless a concrete future need justifies additional legal/process complexity.

## Product Naming Conventions

The application/product name is **Filekin**.

Implementation-facing defaults:

```text
Executable:       Filekin.exe
AppData root:     %AppData%\Filekin\
Repository:       filekin
```

Existing architecture terminology such as `FilesWorkspaceHost`, `FileHierarchySurface`, or the `Files` workspace refers to the visual filesystem portion of Filekin and does not imply that the product itself is named Files.

New namespaces/project identifiers should use the Filekin name rather than legacy concept-folder naming.
