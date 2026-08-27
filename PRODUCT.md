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

### Virtual Workspace Locations

The Files workspace can present useful Windows concepts as first-class virtual locations when they are not naturally represented by ordinary filesystem paths.

Recycle Bin is the first confirmed example: users see and work with `Recycle Bin`, not the raw `$Recycle.Bin` implementation structure.

### Narrow Undo, Safer Complex Actions

Version one does not attempt universal filesystem rollback. Undo is reserved for simple direct reversals such as move and rename where the application can make a reliable promise.

Complex operations such as `/tidy` use preview/confirmation as their preferred safety model and may still appear in `/history` without being undoable.

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

## Open Questions

- Product name.
- Final visual aesthetic.
- Exact layout.
- Single pane versus optional dual pane.
- How much terminal history remains visible.
- How commands render custom visual results.
- How AI is invoked.
- Plugin/extension architecture.
- How external `/commands` are installed and discovered.
- Technology stack.
- Undo and recovery model.

## Sparse, User-Controlled Navigation

The sidebar should primarily contain user-assigned **Locations** plus compact active-session context, rather than automatically reproducing Explorer standard folders. Location aliases may be usable from commands such as `@projects`. Transient navigation should be summoned with commands such as `/recent`, `/drives`, and `/places`.

> Permanent UI is for persistent user context. Commands are for transient context.

Mouse navigation remains complete; the command line makes the same workflows faster and more expressive.

## Product Description

A modern Windows file workspace that combines visual file management with a real terminal. It keeps everyday navigation graphical and approachable while introducing a small command language using `/` for actions and `@` for locations, selections, and other context. The interface teaches these commands naturally as users work, while preserving full shell access for power users. Interactive terminal applications can live in organized tabs and panes tied to their working folders, keeping files, commands, and terminal sessions together in one clean workspace.

### Space to Command

The command-driven side of the workspace should be immediately reachable from the clickable side. From a neutral Files or rich-view surface, Space focuses the command bar so the user can simply press Space and type.

Normal Space behavior is preserved where the key already has an expected editing or control function.

### Simple Execution With `/run`

The workspace provides `/run` as a readable app-owned execution command.

The common case stays short:

```text
/run tool.exe
```

Relative targets naturally resolve from the current Files location, while references allow explicit composition such as `/run @projects\tool.exe`.

This gives newer users a simple execution model without hiding or replacing native shell execution for power users.

### Simple Folder Navigation With `/go`

`/go <folder>` moves the visual Files workspace to one folder without requiring PowerShell quoting.
The entire remainder of the line is the folder target, so the common Windows case stays readable:

```text
/go D:\Client Work\Current Project
/go ..
/go @downloads
/go @projects\Current Project
```

Relative paths resolve from the visible Files folder. A reference must resolve to exactly one folder.
Quotes remain accepted, but spaces alone never make `/go` ambiguous. This is an explicit workspace
action; a bare Windows path keeps its ordinary PowerShell meaning.

### Preserve the Real Shell

The product simplifies common work through `/` actions and `@` references without changing the meaning of raw PowerShell path syntax.

Users can learn the workspace's small convenience language while power users retain familiar, transferable shell navigation and execution behavior.

### PowerShell First, Shell Architecture Open

Version one ships with PowerShell as the guaranteed command-bar shell, while the underlying architecture uses a pluggable shell boundary.

This keeps the initial product simple without permanently coupling the Files workspace, `/` actions, or `@` references to PowerShell.

### Navigation Is for Files

Back, Forward, and Up describe filesystem navigation. Rich views remain temporary command-driven surfaces rather than browser-history destinations.

Back/Esc can dismiss a rich view, but Forward does not reopen it. This keeps the workspace from becoming a browser-style page stack.

### Fast, Not Buried in Menus

The product should make common work immediate without turning right-click into a catalog of every possible capability.

Familiar direct interactions and keyboard shortcuts handle everyday file manipulation. A deliberately compact context menu covers obvious actions, while the command bar carries the broader and more powerful feature set.

> Do not bury capability in menus. Give common actions direct interactions and let the command bar carry the long tail.

### Readable When Seen, Fast When Typed

The workspace keeps explicit vocabulary such as `@thisfolder` when it improves readability. Autocomplete provides the speed layer so the language does not have to trade clarity for abbreviation.

> Readable when seen. Fast when typed.

### Small, Self-Teaching Completion

The command bar helps users discover and complete the workspace's own `/` commands and `@` references without becoming an IDE-style suggestion system. Tab completes the language the app owns; ordinary shell completion remains the shell's responsibility.

> We autocomplete what we invented. The shell completes what it owns.

### Predictable Selection References

`@selection` always means the full selected filesystem set. Commands decide whether they accept one item, many items, or particular file types.

This keeps the reference language stable while allowing commands such as `/run`, `/info`, `/where`, and `/unzip` to have clear, purpose-specific input rules.

### Command-Driven File Operations

The command bar is a real filesystem control surface, not only a launcher for special tools.

Core app-owned verbs include `/copy`, `/move`, `/rename`, and `/delete`, giving keyboard-driven users readable source/destination operations while preserving familiar Windows shortcuts and clipboard behavior.

### Where and Find Serve Different Jobs

The command language keeps both `/where` and `/find`.

`/where` answers where a program/tool lives across the system. `/find` searches for files or folders inside the current Files location or another explicit scope.

Both can use readable rich Files views without conflating application discovery with ordinary filesystem search.

### Quick Filesystem Inspection With `/info`

`/info` gives users a fast answer to practical questions such as:

- How large is this file?
- How large is this folder and everything inside it?
- How much space do these selected items use?
- Where exactly is this item?
- When was it created or modified?
- What useful metadata applies to this file type?

The rich view prioritizes useful information rather than reproducing the full Windows Properties dialog. Large folder calculations stay responsive, expensive details are on demand, and native Windows Properties remains available when deeper operating-system controls are needed.

### Places and Drives Without Sidebar Clutter

The persistent Locations sidebar is reserved for locations that matter to the user's work and projects.

`/places` summons standard Windows/user folders when needed. `/drives` summons available machine volumes/drives when needed.

This keeps system navigation immediately available while preserving a clean, personalized workspace.

### Recent Is Deliberately Deferred

Version one does not include `/recent`. The product should first prove that its tabs, Locations, navigation history, system-location views, and search make files easy enough to reach.

A Recent feature can be reconsidered later from demonstrated need rather than inherited file-explorer convention.

### Disk Analysis Is Deliberately Deferred

Version one does not include `/disk`. Drive capacity remains visible through `/drives`, and filesystem target sizes through `/info`.

A deeper "what is consuming my storage?" feature may be reconsidered later without forcing an unclear command into the initial vocabulary.

### Interactive Tools Should Just Work

Version one does not include `/interactive`.

Interactive-process handling belongs to the product's terminal infrastructure, not to the user's core command vocabulary. Known interactive tools should route correctly without requiring users to classify them manually.

### Tidy Messy Folders

`/tidy` is a confirmed v1 feature aimed at users who struggle with keeping loose files organized.

It can target Desktop, Downloads, the current folder, or another supplied path and sort loose files into predictable categories such as Documents, Photos, Audio, Videos, Installers, and Archives.

The Files version deliberately drops Desktop icon-position automation. Tidy has one understandable job: organize loose files in the folder the user specifies while leaving existing folder structure alone.

Whether normal Tidy execution requires a preview/confirmation remains an open safety/UX decision rather than a v1 requirement.

### Tidy Should Feel Instant

A central part of `/tidy` is the satisfaction of cleaning a messy folder with one explicit command.

Version one therefore does not add a routine confirmation step. The user specifies the target, executes Tidy, and receives a concise result immediately afterward.

Safety comes from Tidy's conservative rules—not from making the user confirm an action they just explicitly requested.

### Keep Moving When One Item Has a Problem

Batch filesystem work should not grind to a halt because one unrelated file is blocked.

If most targets can safely complete, they do. Conflicts are separated for attention afterward.

Leaving the conflict view skips what remains unresolved without undoing successful work.

This supports the product's broader goal of removing unnecessary interruption from routine file management.

### Simple Conflict Choices

When an explicit copy or move encounters an existing destination item, users get three understandable choices: Replace, Keep Both, or Skip.

Keep Both handles naming automatically, and repeated compatible conflicts can reuse the same choice.

Tidy behaves differently by design: cleanup keeps moving, safely skips collisions, and reports them afterward rather than interrupting the fast workflow.

### Power Without Making Everything Administrator

Files runs normally with standard Windows permissions.

When an app-owned operation genuinely needs elevation, users can approve that operation through Windows UAC. Power users may also opt into an advanced elevated PowerShell session for raw shell work.

The boundary stays clear: app commands keep the product's safety model; raw elevated PowerShell keeps PowerShell's power.

### Respect Windows Instead of Fighting It

Files does not force-unlock files, terminate applications, bypass protected locations, or recreate Windows credential and ACL management.

Locked items can be retried or skipped. Read-only items are left alone unless the user's requested operation genuinely needs to change them, in which case the choice is explicit.

The result is a file manager that stays fast while respecting the operating system's security and ownership boundaries.

### Put Work Where It Belongs

Files does not force every operation into the command bar.

Small work stays lightweight. Large filesystem jobs can move into dedicated task tabs so the user can continue working. Inspection/search stays in the rich view the user requested, and interactive programs belong in terminal tabs.

The user issues the command; the application chooses the appropriate surface.

### Windows Foundation, Custom Identity

Version one will be a C#/.NET WPF application.

WPF is chosen as a mature Windows foundation for the technically unusual combination of a file manager and terminal, not because the product should look like a traditional WPF application.

Files should retain its own modern terminal-like identity, and long-running filesystem/process work must never make the interface feel blocked or sluggish.

### Reliable Windows Plumbing, Different Product Experience

Files uses Windows where Windows already owns the underlying behavior, but the interface is intentionally a different experience from Explorer.

The product should feel fast, restrained, command-driven, and dependable.

Implementation must resist AI-generated feature/UI drift: no speculative capabilities, no generic dashboard design, and no unnecessary engineering cleverness.

> Reliable and simple beats clever.

### One Context in Files, Independent Contexts in Terminal Tabs

The Files command bar is intentionally tied to whatever folder the user is looking at. That keeps command behavior obvious and reduces hidden state.

Users who want an independent PowerShell session simply launch PowerShell into a terminal tab, where it can keep its own working directory and process state without affecting Files.

### PowerShell Where It Helps, Real Terminals Where They Are Needed

The Files command bar maintains a real persistent PowerShell session that stays synchronized with the visual filesystem location.

That means shell navigation can move Files and Files navigation can move the shell context.

Programs that need a genuine interactive terminal are moved into independent terminal tabs instead, using Windows terminal infrastructure rather than pretending a rich result view is a terminal.

### One Files Context, No Hidden Divergence

The visual Files hierarchy and the Files command bar always represent the same filesystem location.

If a PowerShell command enters a context Files cannot display—such as the Registry provider—that shell context belongs in an independent terminal tab instead.

This preserves the product's core promise that what the user sees is what their Files command bar controls.

### Real Terminal Behavior

Terminal tabs behave like actual shell sessions rather than special-purpose tool windows.

Files supplies the starting directory, PowerShell owns the terminal session, and tools such as Claude or Codex run inside it.

Exit the tool and you return to PowerShell. Exit PowerShell and the tab is gone.

The product does not add lifecycle ceremony where normal terminal behavior already makes sense.

### One Visual Family, Clear Different Jobs

The filesystem hierarchy, command-driven rich views, and long-running task tabs should feel like parts of the same product without becoming indistinguishable.

Rich views and task tabs share a restrained design language. Files remains clearly for browsing. Rich views are for command results and inspection. Task tabs are for persistent work.

This keeps the interface coherent without turning every feature into another generic dashboard.

### Configuration Users Can Own

The app keeps user-facing configuration and saved Locations in a readable settings file under the product's own AppData directory.

Advanced users can inspect, edit, and back up their configuration.

Transactional history/undo state uses embedded SQLite separately, keeping the product both transparent and reliable.

### Tidy Rebuilt for This Product

The original standalone Tidy tool informs the feature, but Files gets a clean native implementation.

Only the useful folder-organization behavior carries forward. Desktop icon rearrangement and other legacy shell-specific behavior stay out.

This keeps `/tidy` simple, integrated, testable, and consistent with the rest of the product.

### Install It or Carry It

Version one supports both a normal Windows installer and a portable ZIP.

The installer serves users who want a conventional installed application. The portable build serves users who want to extract and run the tool directly.

Both are self-contained, so the product should work without asking users to separately install .NET.

Updates are offered, not forced. Microsoft Store distribution and paid code signing are not v1 requirements.

### Free and Open Source

The project is intended to be free and open-source software under GNU GPLv3.

Development should happen publicly, allowing people to inspect the implementation, build it themselves, report problems, contribute improvements, and create GPL-compatible forks.

Official installer and portable releases are intended to remain freely available.

### Community-Supported Development

Optional donations may support development.

Donations are support for the project rather than a feature gate or requirement to use the software.

The project should remain approachable to outside contributors without unnecessary licensing bureaucracy.

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
