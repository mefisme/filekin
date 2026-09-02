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

## Resolved Product Boundaries

Detailed user-visible behavior belongs in `PRODUCT.md`, `FEATURES.md`, `UX-DESIGN.md`, and
`DECISIONS.md`; it is not repeated here as an architecture decision diary. The implementation
architecture below assumes these settled boundaries:

- deterministic app-command routing and known `@` reference expansion;
- one synchronized Files/runspace filesystem context and independent ConPTY terminal sessions;
- app-owned operation history with narrow, reevaluated undo;
- distinct file hierarchy, rich-view, and task surfaces with keyboard-first focus behavior;
- Windows-native integration behind Core boundaries;
- bounded background work, partial-success reporting, and explicit collision/elevation decisions.

When implementation evidence conflicts with those specifications, record the conflict in `HANDOFF.md`
and stop for a product decision instead of encoding a new behavior here.

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

## Implementation Architecture — Cooperative Agent Coordination

### Boundaries

Agent coordination is opt-in app infrastructure above the existing Files and terminal systems.

```text
Filekin.Core
└── AgentProjectCoordinator: provider-neutral state machine, lease, selection, handoff

Filekin.Infrastructure.Windows
├── SqliteAgentProjectStore / AgentCoordinationRuntime
├── CodexAppServerClient
├── ClaudeBackgroundSessionAdapter
├── NativeAgentSessionLauncher / AgentRunService
└── AgentSessionAttachCommand

Filekin.Mcp
└── project/provider-fixed coordination tools and Claude status-line ingestion

Filekin.App
└── persistent project control-room tabs and marked provider CLI terminal tabs
```

`Filekin.Core` contains no WPF, process, provider SDK, JSON-RPC, or MCP types. Provider transports
normalize supported facts into immutable Core values. `AgentCoordinationRuntime` owns reconciliation,
provider-fact refresh, transactional transitions, and fixed MCP launch configuration; it never starts a
model turn. `AgentRunService` is the sole native-session dispatcher.

Coordination is lazy. Ordinary startup and ordinary `codex`/`claude` terminal commands do not opt in,
probe providers, initialize project state, start MCP, ask consent, or acquire a lease.

### Subscription and safety boundary

Each unmodified provider tool owns its authentication and uses the user's own subscription. Filekin
stores only non-secret native session ids and provider-reported usage. It never stores credentials,
selects API billing, enables usage credits, spends resets, answers approvals, or uses
`bypassPermissions`, `-p`, the Agent SDK, terminal injection, or screen scraping.

Codex uses a private App Server process with one immutable project MCP configuration. Coordinated turns
carry only the project-scoped settings the owner approved; Filekin does not modify the user's global
configuration. Server approval/input requests remain human-owned.

Claude uses its documented background-session, status-line, hooks, and MCP paths after project-scoped
inspection proves first-party Claude.ai authentication and refuses billing/provider redirection from
environment or applicable settings. Filekin passes a reviewable in-memory shared-checkout setting after
explicit consent and validates the canonical checkout before accepting the session. A structured
rate-limit callback records unavailability without ingesting transcript or raw errors.

Claude's status-line helper accepts documented JSON on stdin, verifies the project folder, stores only
five-hour/seven-day percentages and resets, and never mutates participant, session, turn, or lease state.
The command form must work in both Git Bash and PowerShell.

Primary protocol references are the official Codex App Server/pricing documentation and Claude Code
CLI, configuration, environment, status-line, hooks, sessions, Agent View, worktree, and legal guidance.
Re-verify current official documentation before changing an unfamiliar provider boundary.

### State, lease, and handoff

Core state includes the project, participants, provider-specific usage windows, one working-tree lease,
messages, handoffs, objective, consent, and lifecycle status. Multiple usage windows stay separate;
missing or stale data is unknown, and no universal quota or next-turn-cost estimate is invented.

Only the active participant owns the cooperative working-tree lease. It is not an OS lock and does not
protect against unrelated processes; parallel coordinated writers require separate Git worktrees.

A relay is:

```text
refresh provider facts → select/start one agent → clock in → grant lease
→ work while partner waits → request/submit handoff → provider stops
→ release/transfer lease → start recipient on demand
```

A handoff submission does not prove provider stop and cannot release a lease. A provider stop without a
usable handoff becomes `NeedsAttention` when a handoff was required. A normal completed turn returns the
lease without becoming a failure. User Stop is cooperative, keeps the project, and never activates the
partner. Filekin owns the handoff reason and preserves useful handoff text even when the agent labels it
differently.

Allowance is recorded independently of presence. Unknown allowance permits a cold start; fresh
exhaustion refuses unless the owner enabled low-allowance work. Automatic handoff requests occur before
the hard floor only when the intended recipient has safe headroom. The in-turn refresh is a non-
overlapping one-shot timer per working project; it rearms after completion, stops outside `Working`,
and records a fault until the next explicit operation restarts it.

### Persistence and restart

Coordination uses normalized transactional tables in app-owned `state.db`; ordinary settings stay in
`settings.json`, and secrets live in neither. One `StateDatabase.SchemaVersion` owns SQLite
`user_version` for coordination and operation history. Read/transition/write reserves the writer before
reading. Existing-table column additions require explicit migrations; `CREATE TABLE IF NOT EXISTS`
does not add columns.

Startup persists reconciliation before any project operation, MCP configuration, or lease change. A
failed provider inspection records `Unavailable` but never proves stop or releases an active writer.
Agent filesystem edits remain external and never enter Filekin `/history`.

The runtime periodically refreshes only projects with an active working lease. Disposal cancels and
drains a running tick before taking the operation gate; reversing that order deadlocks.

### Starting, stopping, and native identity

Explicit setup records the canonical folder, objective, exact shared-checkout consent, and the owner's
permission scope without writing project files. Start refreshes facts, chooses the safest provider
unless one was explicitly chosen, starts it through its native interface, records the native identity
out of band, waits for MCP clock-in, then grants the turn. Failure to clock in requests provider stop
and leaves no lease.

The persisted permission scope is `AgentWorkMode`: `UseMyOwnSettings` sends no provider override,
`LookDontTouch` maps to Claude `plan` and Codex `readOnly`, and `WorkOnItsOwn` maps to Claude `auto` and
Codex `workspaceWrite`. The latter Codex modes also use `approvalPolicy: never`; Filekin does not answer
approvals, and the sandbox decides what is allowed. A mode may change only while no provider is running.

The relay starts a recipient only when a handoff needs it. Saved conversations may be resumed for
handoff continuity, but presence and persisted identity remain different facts. `filekin_clock_in`
reports presence and accepts no native id; a model cannot name or replace its session.

Claude background sessions can outlive Filekin. Their conversation id and short attach/stop handle are
different and are resolved through `claude agents --json`; liveness is based on `pid`, not turn
`state`. Stop must remain true across two polls before Filekin trusts it. Codex currently runs under
Filekin's private App Server and therefore cannot outlive that process.

### Presentation and terminal boundary

`/agents` opens/selects a persistent `Agents · <folder>` task tab. It hosts setup and the control room;
Files remains permanent, multiple projects may coexist, and closing a project tab releases presentation
state only.

`/projects` is a passive app-state query. It appears only after a saved project exists, lists every
project without starting a provider, and navigates to the selected folder's control room. Persisted
rows load first; an existing run service may then inspect providers asynchronously to refresh live
connection facts. Invoking `/projects` directly before any project exists returns the empty list through
a non-creating database check; it does not construct the coordination runtime or create `state.db`.

The control room displays coordination facts and lifecycle actions. It is not a transcript. There are
no custom Agent Session view models or structured transcript tabs.

**Session** opens the exact provider session in a specially marked ordinary ConPTY terminal:

- Claude resolves the live background handle and runs `claude attach <id>`.
- Codex runs `codex resume` with the same fixed project MCP overrides, but only after Filekin's live App
  Server no longer owns that thread; two clients on one live thread are refused.

The provider CLI owns output, questions, approvals, and `/clear`. Filekin injects no terminal input and
normal terminal shortcuts/lifecycle remain unchanged. Closing the terminal ends its root shell after
confirmation; for Claude, that does not itself end the background session and **End** uses its
cooperative stop. A resumed Codex CLI is registered as the project's live process, prevents a second
launch, and reconciles the lease when its terminal closes; **End** closes that exact terminal because
Codex exposes no separate cooperative session-stop command. Filekin-close logic asks providers what
remains live across saved projects,
treats unknown as unknown, and lets the user keep sessions running, end them, or cancel.

The terminal host reports initial-command completion through a private synchronization signal, not VT
parsing. When an attached CLI returns, Filekin unregisters that provider process and leaves the root
PowerShell tab open as an ordinary terminal.

### MCP and optional project memory

Each MCP process receives one project GUID and provider identity at launch. Tool calls cannot replace
those identities, expose native session ids, grant filesystem/terminal access, or perform restart
reconciliation. MCP configuration is inert data until explicit native launch. Token-free stdio tests
cover all coordination tools and concurrent project-fixed identities.

`Filekin.Mcp.exe` ships beside `Filekin.exe` and serves either stdio coordination or the fixed Claude
status-line mode. Release builds and self-contained publishes must carry the matching companion; a
missing/stale binary is a repair/build failure, never a fallback to another executable.

A future bootstrap may preview existing `AGENTS.md`, `CLAUDE.md`, shared skill resources, and optional
`.filekin/PROJECT.md`. It must preserve existing files, require explicit approval, and never create a
competing handoff authority.
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

The history model remains bounded according to the existing product decisions: the most recent 50
user-level app-owned operations. One bulk invocation is one retained row.

The operation journal is an additive table in the same `state.db` used by cooperative agent state.
Its initializer does not independently advance the shared database version, so history or agent
coordination may touch a new database first without causing the other subsystem to skip its own tables.
Recording and rolling pruning happen in one transaction. A separate startup reconciliation transaction
removes prior-process Undo promises while preserving the informational rows and any recorded partial or
failed Undo detail.

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
