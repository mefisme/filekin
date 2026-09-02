# Product

## Status

Living product document. The concept is still being explored and should not yet be treated as an implementation specification.

## Core Idea

A modern Windows file workspace that combines visual file management with a real terminal.

The visual interface and terminal share the same filesystem state rather than behaving like two separate tools. The product should feel like a visual terminal workspace rather than a traditional Windows File Explorer clone with a terminal attached.

Core interaction principle:

> Anything you can click, you can type. Anything you type that affects files should be visible.

### Small Language, Large Capability

The application's simple language is built on three foundations:

1. **`/` = Action** — run a built-in workspace action such as `/go`, `/where`, `/unzip`, or `/tidy`.
2. **`@` = Reference** — identify something the workspace already knows about, such as `@thisfolder`, `@selection`, `@projects`, or `@parent`. A reference is not limited to folders; it represents an addressable workspace object.
3. **Everything else = Real Shell** — normal PowerShell and CLI commands remain available without replacing them with proprietary equivalents.

Examples:

```text
/where python
/unzip @selection @thisfolder
/go D:\Client Work
cd @projects
git status
Get-ChildItem @projects -Recurse
```

The `@` context layer should work with built-in commands and, where safely resolvable, ordinary shell commands.



### Built-In References

Keep the built-in reference vocabulary intentionally small for readability and learnability.

Version-one built-ins:

```text
@thisfolder
@parent
@selection
```

Semantics:

- `@thisfolder` — the current filesystem location shown by the workspace.
- `@parent` — the parent directory of the current filesystem location.
- `@selection` — the currently selected visible item or items.

If `@selection` is empty, the application should report that clearly rather than guessing.

User-assigned sidebar Locations automatically become references:

```text
@projects
@downloads
@music
@archive
```

Do not add `@last` in version one because its meaning is inherently ambiguous.

Avoid synonyms such as `@here`, `@cwd`, `@current`, or `@folder` when `@thisfolder` already expresses the same concept.

### Syntax Constraint

Do not introduce another special syntax character unless `/`, `@`, and the underlying real shell genuinely cannot express the requirement.

The application should resist becoming its own programming language. PowerShell and other real CLI tools remain available for advanced syntax and scripting.

The intended mental model is:

```text
/ = ACTION
@ = REFERENCE
everything else = REAL SHELL
```

A slash command may act on one or more references:

```text
/action @reference
/action @source @destination
```

### Files Command Bar Boundary

The simple `/` and `@` language belongs to the Files workspace command bar.

Shell commands entered there can still use workspace references:

```text
python @selection
git -C @projects status
```

The application resolves references before handing the command to the real shell.

Hosted terminal applications keep their own native terminal behavior; the workspace does not inject its mini-language into Codex, Claude Code, SSH, shells, or other interactive tools.

### Visible Operation History

Version one includes `/undo` and `/history`.

`/undo` reverses the most recent safely undoable app-owned filesystem operation. `/history` provides a bird's-eye visual record of what the application changed and which operations remain reversible.

This operation journal is separate from normal command recall through the Up/Down arrow keys.


### Persistent History, Session-Scoped Undo

Operation history persists across app restarts so users can see what the workspace changed over time.

Undoability is limited to the current app session in version one.

History retention should be automatic rather than another maintenance task for users.

### Lightweight History Retention

The expected version-one behavior is a rolling record of the most recent 50 app-owned operations. Bulk actions count as one operation.

Retention is automatic and requires no routine configuration or cleanup from the user.

### Safe Undo and Native Delete

Undo never silently overwrites conflicting files. The user chooses how conflicts are resolved, and partial reversals are reported accurately.

Normal deletion respects the user's Windows Recycle Bin behavior/settings where supported rather than creating a separate application trash system.

Recoverable delete answers to `/toss`, `/trash`, and `/delete` alike. The operation is one thing; the
word the user reaches for is theirs.

### Virtual Workspace Locations

The Files workspace can present useful Windows concepts as first-class virtual locations when they are not naturally represented by ordinary filesystem paths.

Recycle Bin is the first confirmed example: users see and work with `Recycle Bin`, not the raw `$Recycle.Bin` implementation structure.

### Narrow Undo, Safer Complex Actions

Version one does not attempt universal filesystem rollback. Undo is reserved for simple direct reversals such as move and rename where the application can make a reliable promise.

Complex operations such as `/tidy` use preview/confirmation as their preferred safety model and may still appear in `/history` without being undoable. `/tidy` shows its plan before moving anything, and the plan is chosen by category rather than by file.

`/unzip` is an exception to that earlier boundary: Filekin records exactly what extraction writes,
so the result can offer a session-scoped Undo that removes only Filekin-created content and restores
originals recycled during replacement. Durable history remains separate future work.

### Deterministic Command Execution

The command bar enhances a real shell rather than replacing it. `/` clearly means an application action; recognized `@` references can be used inside shell commands; known interactive tools can open in hosted terminal tabs.

Execution routing is deterministic rather than AI-controlled, preserving readability and trust.

### Command Bar Reports, Workspace Explains

The Files command bar remains a one-line control. It reports concise status and keeps the most recent command result inspectable until the next command is actually executed.

Detailed output and interactive informational commands use a closeable workspace view instead of expanding a terminal pane over the file hierarchy.

### Ephemeral Shell Output

Version one keeps only the most recent finite-command output available for immediate inspection. It remains available until the user executes another command, even if they have already begun typing the next one.

Shell output is not turned into a persistent transcript or folded into `/history`.

### Commands Speak Syntax; Views Speak English

The workspace's small language stays symbolic where symbols are useful: `/` means action and `@` means reference.

When those commands produce rich workspace views, the interface responds in readable English: `Files · History`, `Files · Where`, `Files · Disk`.

This reinforces the command language without making the visual interface cryptic.

### Files Owns Selection

Rich views may contain clickable results and explicit actions, but they do not change the meaning of `@selection`.

Filesystem selection belongs to the underlying Files context. Rich-view results navigate back into Files when they need to establish a real selection.

This lets the command bar remain useful while rich views are open without creating competing meanings for the application's small command language.

### Keyboard and Mouse Are First-Class

The product is intentionally both a clickable Files workspace and a command-driven workspace. Strong keyboard support is therefore a core requirement rather than an optional secondary interaction mode.

Rich views remain fully keyboard-operable while keeping keyboard focus distinct from filesystem selection and `@selection`.

### Fourth UX Rule: The Interface Teaches Its Language Through Use

Users should not need to study a command manual before benefiting from the command line.

The GUI should reveal the language naturally:

- Selecting files can reveal `@selection`.
- The current directory can reveal `@thisfolder`.
- Assigned Locations can reveal aliases such as `@projects`.
- Typing `/` can discover and autocomplete built-in commands.
- Graphical actions may occasionally show a subtle faster command-line equivalent.

The intended learning path is:

```text
click → discover → use simple commands → mix commands with @ context → use the full shell
```

The GUI remains complete throughout that progression.

## Problems We Want to Solve

- Common Windows filesystem tasks take too many clicks.
- Useful application files can be scattered across many Windows locations.
- Terminal navigation is powerful but lacks persistent visual context.
- Traditional file managers often hide useful filesystem information.
- Archive extraction can create unnecessary nested folders.
- Long paths make terminal filesystem operations cumbersome.
- Users often need to understand what files and folders actually are before changing or deleting them.
- Switching between a file manager and terminal creates unnecessary friction.

## Product Direction

The application should combine:

- Direct mouse-based filesystem interaction.
- Keyboard-first navigation.
- A real shell.
- Visual representations of command results.
- Small purpose-built utility commands.
- Optional AI assistance where interpretation is genuinely useful.

Normal filesystem operations should remain deterministic.

AI should help users understand the filesystem rather than unnecessarily controlling basic filesystem operations.

## Initial Utility Concepts

### `/where`

Find and explain locations associated with an application or tool, potentially including:

- Executables
- User data
- Configuration
- Cache
- Extensions/plugins
- PATH entries
- AppData locations
- Program Files locations
- Start Menu entries
- Related processes
- Relevant registry information

Results should be visually navigable.

**Implemented, 2026-08-28, with two narrowings (DECISIONS.md).** Everything above ships except
*related processes*, which are not locations and change while the view is open, and *relevant
registry information*, which is read — App Paths and the uninstall metadata are how a program's real
install folder is found — but never displayed. Filekin shows the filesystem path a registry entry
points at, never the key itself. Exactly one query is accepted, and a name containing spaces must be
quoted. An eligible executable also offers **Add to PATH**, which adds its folder to the real Windows
user PATH. Cache, extension, add-on, and plugin folders are discovered only beneath an already
matched program directory, so those generic role names cannot broaden the system scan.

### `/unzip`

Extract archive contents into the destination the user actually specifies.

Extraction normally creates exactly one new folder in that destination. An archive that already has
one wrapper folder reuses it; loose archive contents receive a folder named after the archive. The
preview can remove that folder explicitly when the user wants the contents directly in the destination.

Version one opens ZIP archives only. Multiple archives are allowed and are planned independently.
The default preview shows the destination, layout, collisions, and files before anything is written.
Once extraction starts, leaving the preview with Back/Esc does not stop it. The command bar keeps a
compact live status with View and Stop actions so Files remains usable while the work finishes.

### `/zip`

Create a ZIP archive from one or more files or folders. The preview owns the two choices that matter:
whether a single source keeps its outer folder and whether an existing archive is replaced. The
command deliberately has no switches; its grammar is `/zip <item...> [name.zip]`.
Compression uses the same detachable operation lifecycle and command-bar status as extraction.

### `/tidy`

Potential integration with an existing utility that organizes files and desktop contents.

## Filesystem-Centered Terminal Workspace

The product should also organize interactive terminal applications around the filesystem locations where they are launched.

Launching a terminal application such as Codex CLI, Claude Code, a development server, or another interactive CLI should not require the user to manually arrange separate terminal windows.

Potential behavior:

- Launch an interactive CLI in a new terminal tab.
- Launch it in a split terminal pane.
- Launch it in the user's preferred external terminal.
- Inherit the current filesystem location as the terminal session's working directory.
- Label sessions by both the running tool and associated folder/project.
- Surface active terminal sessions from the filesystem view.

Example session labels:

```text
CODEX · MyApp
CLAUDE · Website
DEV SERVER · API
POWERSHELL · Downloads
```

A directory may eventually indicate terminal sessions associated with it:

```text
DIR   MyApp/          [CODEX ●] [SERVER ●]
DIR   Website/        [CLAUDE ●]
DIR   Experiment/
```

This extends the product beyond file management toward a filesystem-centered Windows workspace.

## Cooperative Agent Projects

Filekin coordinates Codex and Claude Code in one folder through each installed tool and the user's own
subscription. Filekin stores no provider credentials, never enables metered usage, and keeps exactly
one working-tree lease owner. The other agent waits without model prompts. Provider-reported allowance
guides initial selection and early cooperative handoff; unknown or unsafe state pauses visibly.

Live leases, usage, messages, and handoffs are transactional app state exposed through a project-bound
MCP server. Project memory remains inspectable in existing agent instruction and skill files; Filekin
previews any proposed bootstrap and never silently overwrites them.

`/agents` opens/selects one persistent `Agents · <folder>` task tab. It hosts explicit setup and the
control room; Files remains separate, multiple projects may coexist, and closing the tab closes only
the view. The control room shows coordination facts and actions. **Session** opens the exact provider
session in a marked ordinary Filekin terminal tab, where the provider's own CLI shows output, questions,
approvals, and `/clear`. Filekin never builds a second transcript UI, scrapes VT output, injects keys,
or treats ordinary user-launched agent terminals as coordinated projects.

Once at least one agent project exists, the sidebar exposes `/projects`. Its rich view lists every
saved project with folder, connection, work, agents, and usage-left facts; activating a row opens that
folder's control room. Project setup records one explicit work mode: **Use app settings** (default),
**Plan / read-only**, or **Trust (auto)**. The answer remains visible and may be changed while no agent
session is running.

This capability manages development agents only. Filesystem behavior remains deterministic.
## Current Product Questions

Open decisions are limited to features whose exact behavior is not yet approved: `/find`, terminal
overflow/panes and assistive text, file-path copy before context-menu completion, optional hosted-shell
profile loading, and the Agent Control Room questions in `HANDOFF.md`. Missing decisions are not
permission to invent UI.
## Sparse, User-Controlled Navigation

The sidebar should primarily contain user-assigned **Locations** plus compact active-session context, rather than automatically reproducing Explorer standard folders. Location aliases may be usable from commands such as `@projects`. Transient navigation should be summoned with commands such as `/recent`, `/drives`, and `/places`.

Locations identify folders rather than brittle path strings. When an app-owned `/move` or `/rename`
relocates a folder, any saved Location at or beneath that folder follows to the new path. Copying does
not retarget a Location because the original remains.

> Permanent UI is for persistent user context. Commands are for transient context.

Mouse navigation remains complete; the command line makes the same workflows faster and more expressive.

## Product Commitments

### One Files Context, Real Independent Terminals

Files and its persistent PowerShell command bar always share one filesystem location. GUI navigation
moves the runspace; filesystem `cd` can move Files. Non-filesystem providers and interactive tools
belong in independent ConPTY tabs that inherit the Files folder once, then own their shell, processes,
input, and working directory.

### Small, Explicit Language

Slash commands are app-owned actions; known `@` tokens are readable Files references. Unknown shell
input remains real PowerShell. `/run` is the only app-owned launcher, `/go` navigates Files, and
`/ext` is the external escape hatch. Completion teaches only slash commands and known references.

### Files Own Selection; Views Explain Work

Filesystem selection belongs to the Files hierarchy. Rich views inspect or explain without redefining
`@selection`; task tabs host persistent work. Finite shell output stays compact and expandable, while
structured commands use human-readable views. The interface remains keyboard-first with visible focus,
conventional navigation, and Space-to-command behavior.

### Deterministic, Recoverable File Work

Core file operations, archives, Tidy, history, and undo are app-owned and deterministic. Batch work
keeps independent successes, isolates conflicts, refreshes after writes, and never silently overwrites
or destroys edited output. Normal deletion uses the Windows Recycle Bin. Expensive work stays off the UI
thread.

### Sparse Navigation and Windows Integration

The sidebar contains user-owned Locations and direct Filekin surfaces rather than an Explorer tree.
`/places` and `/drives` provide transient system navigation. Windows owns associations, Properties,
Recycle Bin semantics, UAC, known folders, and network authentication; Filekin owns the visual
experience.

### Configuration, Packaging, and Identity

Readable preferences live in `%AppData%\Filekin\settings.json`; transactional history and coordination
live in `state.db`; secrets live in neither. Filekin ships self-contained as both a traditional
installer and portable ZIP, with user-controlled updates. It is a free GPLv3 Windows application built
in C#/.NET/WPF with a custom compact terminal/developer-tool visual language.
## Official Product Identity — Filekin

The product name is **Filekin**.

```text
Product:        Filekin
Executable:     Filekin.exe
App data:       %AppData%\Filekin\
Installer:      Filekin Installer
Portable:       Filekin Portable
Repository:     filekin
License:        GNU GPLv3
```

Primary category description:

> **Filekin — a keyboard-first Windows file manager + terminal.**

The name `Files` may still appear in these specifications when referring to Filekin's visual filesystem workspace/surface. It is no longer a placeholder product name.
