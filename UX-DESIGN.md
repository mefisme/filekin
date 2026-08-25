# UX Design

## Status

Living design document. Visual and interaction ideas are exploratory unless marked as decided.

## Foundational Learning Rule

> **The interface teaches its language through use.**

The UI should progressively expose `/` actions and `@` context without requiring a separate tutorial. Mouse-first use remains fully functional, while subtle labels, autocomplete, and contextual hints help users become faster with the command line over time.

## Design Direction

The application should have a semi-terminal aesthetic across the entire interface.

It should **not** look like traditional File Explorer on top with a terminal panel underneath.

Instead, the filesystem itself should feel like a visualized terminal.

## Main Filesystem View

A directory could appear as structured terminal-like output:

```text
C:\users\user\projects
────────────────────────────────────────────────────────

TYPE    NAME                    SIZE       MODIFIED
DIR     project-one/            182 MB     08/23  22:41
DIR     website/                 64 MB     08/22  16:02
ZIP     archive.zip             428 MB     08/20  14:52
MD      notes.md                 12 KB     08/23  01:17
PY      test.py                   4 KB     08/24  00:32
```

Despite the appearance, these remain normal GUI objects:

- Clickable
- Double-clickable
- Draggable
- Multi-selectable
- Sortable
- Context-menu capable

## File Representation

Traditional large file/folder icons should be minimized.

File types may be represented textually:

```text
DIR   projects/
IMG   cover.png
WAV   kick.wav
PY    main.py
ZIP   source.zip
EXE   setup.exe
```

Directories may use a trailing `/` as part of the visual language.

## Navigation

The current location should resemble a terminal path:

```text
C:\users\user\projects\
```

Path segments can still be clickable.

The location should also be directly editable.

Terminal navigation and GUI navigation must remain synchronized:

- `cd folder` updates the visual filesystem.
- Double-clicking a directory updates the shell working directory.

## Command Area

Current preference: avoid a permanently separate terminal pane.

A single command line can live near the bottom:

```text
C:\users\user\projects > _
```

When a command produces substantial terminal output, the command area may expand.

When the user is finished with the output, it can collapse again.

This is intended to make the product feel like one interface rather than a file manager stacked above a terminal.

## Real Shell

The application should expose a real shell rather than inventing a proprietary fake shell.

Normal commands could pass through directly:

```text
git status
python app.py
Get-Process
```

Application commands can coexist:

```text
/tidy
/where python
/unzip archive.zip .
```

## GUI Selection + Terminal

GUI selections should be addressable from commands.

Possible references:

```text
@selection
@folder
@parent
@last
```

Example:

```text
move @selection D:\Backup
```

This avoids repeatedly typing long filesystem paths.

## Visual Command Results

Commands may temporarily change what the main visual area represents.

Possible modes:

```text
VIEW: DIRECTORY
VIEW: DISK
VIEW: WHERE
VIEW: SEARCH
```

Example:

```text
/where python
```

could replace the directory listing with an interactive visualization of Python-related locations. Clicking a path returns to the normal directory view at that location.

## Visual Personality

Avoid a stereotypical hacker aesthetic.

Avoid:

- Neon-green-on-black as the defining theme.
- CRT effects.
- Matrix-style decoration.
- Excessive terminal nostalgia.

Prefer:

- Modern developer-tool feeling.
- Strong typography.
- Structured alignment.
- Restrained decoration.
- Subtle boundaries.
- Monospaced typography where it communicates filesystem/terminal information.
- Normal UI typography where it improves readability.

Both dark and light themes remain possible.

## Learning Through Interaction

GUI actions may reveal their command equivalents.

Concept:

> Click action → see command → eventually type command.

This could quietly teach terminal/filesystem literacy without turning the application into a tutorial.


## Terminal Application Tabs and Panes

Interactive terminal applications should be able to graduate from the compact command area into full terminal sessions.

Example:

```text
┌ FILES ┬ CODEX · MyApp ┬ CLAUDE · Website ┬ + ┐
```

Launching `codex` while browsing `D:\Projects\MyApp` could open a full terminal tab whose working directory is automatically `D:\Projects\MyApp`.

Launching `claude` from another location could create another independently persistent session.

### Terminal Panes

Terminal tabs may support splitting into panes.

Example:

```text
┌──────────────────────────┬──────────────────────────┐
│ CODEX · MyApp            │ CLAUDE · MyApp           │
│                          │                          │
│ D:\Projects\MyApp       │ D:\Projects\MyApp       │
│                          │                          │
└──────────────────────────┴──────────────────────────┘
```

This would allow multiple CLI tools or agents to work from the same project without requiring users to manually arrange Windows terminal windows.

### External Terminal Escape Hatch

Users should not be forced to use the embedded terminal workspace.

Possible launch targets:

```text
New Tab
New Pane
New Window
Preferred External Terminal
```

The preferred external terminal should open at the current filesystem location.

### Session Identity

Tabs should describe what they represent rather than becoming a row of generic `PowerShell` labels.

Prefer:

```text
CODEX · MyApp
CLAUDE · Website
DEV SERVER · API
```

over:

```text
PowerShell
PowerShell
PowerShell
```

### Filesystem Session Indicators

The visual filesystem may surface active sessions associated with directories.

Example:

```text
DIR   MyApp/          [CODEX ●] [SERVER ●]
DIR   Website/        [CLAUDE ●]
```

Selecting a session indicator could jump directly to its terminal tab or pane.

This reinforces the idea that the application visualizes not only where files exist, but what activity is currently associated with those locations.

### UX Principle

The compact command area and full terminal sessions serve different purposes:

- **Command area:** quick filesystem commands, navigation, slash utilities, and short shell operations.
- **Terminal session:** persistent or interactive applications that need a full terminal environment.

The transition between them should feel natural rather than exposing two unrelated terminal systems.

## Sparse Navigation Sidebar

Keep the sidebar deliberately small: **LOCATIONS** chosen by the user and a compact **ACTIVE** sessions area. Locations may have aliases and eventually support commands such as `/location add . projects` and `cd @projects`. `/recent`, `/drives`, and `/places` should render transient navigation in the main area instead of permanently consuming sidebar space.

> The GUI gets you there. The command line gets you there faster.

> Permanent UI is for persistent user context. Commands are for transient context.

## Self-Learning Command UX

The interface should teach its command language without requiring a tutorial.

Three concepts should remain visually consistent:

```text
/       action
@       context/reference
other   real shell command
```

Examples of human-readable references include:

```text
@thisfolder
@selection
@parent
@projects
@downloads
@last
```

When files are selected, the UI may subtly expose `@selection`. Assigned Locations should expose their aliases. Typing `/` in the command area should open lightweight command discovery/autocomplete with concise explanations and argument hints.

Example:

```text
/where       find locations related to an app
/unzip       extract an archive intelligently
/tidy        organize files
/disk        analyze disk usage
/recent      show recent locations
/places      show standard Windows locations
/drives      show connected drives
```

Graphical actions may occasionally teach a faster command equivalent, but should not flood terminal history with generated commands.

### Shell Compatibility

Full shell compatibility is a non-negotiable UX requirement. The simple command language sits on top of the shell rather than replacing it.

Application `/commands` may produce rich visual results. Ordinary shell commands should preserve authentic terminal behavior. `@references` should, where safe and technically feasible, resolve inside ordinary shell commands as filesystem shorthand.

This creates a natural progression from graphical use to slash commands to full shell proficiency without forcing users into separate modes.

## Reference Language UX

The interface should teach only a very small built-in reference vocabulary:

```text
@thisfolder
@parent
@selection
```

User-assigned Locations create their own readable references automatically.

The UI should expose these references contextually rather than requiring documentation. For example, a visible selection can show `@selection`, and a saved Location named `projects` can show `@projects`.

Do not expose ambiguous `@last` behavior or multiple synonyms for the current folder. Readability is preferred over shorthand density.

## Command-Bar and Terminal-Tab Boundary

The Files command bar is the place where `/` actions and `@` references are taught and used.

Persistent terminal tabs should visually feel like native terminal sessions. They should not display or imply that workspace shorthand is available inside third-party interactive tools.

This distinction should remain understandable without adding extra modes or labels unless testing shows users are confused.

## Terminal Closing Behavior

Closing a terminal tab with a live process should produce a simple confirmation explaining that the live session will end.

Completed terminal tabs close normally.

Closing the whole application with multiple active sessions should use one consolidated confirmation rather than a sequence of modal prompts.

The UI should describe live-process termination accurately without implying that a third-party tool's own saved/resumable session data will necessarily be deleted.

## Proposed Terminal Session States

A terminal tab should remain visible when its hosted process completes or fails so output is not lost unexpectedly.

Proposed conceptual states:

```text
running
completed
failed
```

Exact icons and styling remain undecided.

Duplicate sessions are allowed and should receive readable disambiguated names. External terminal windows remain visually and behaviorally separate from hosted tabs.

## Confirmed Terminal Session State UX

Hosted terminal tabs have three conceptual states:

```text
● running
○ completed
! failed
```

Completed and failed tabs remain visible so their output can still be inspected.

Duplicate sessions receive simple disambiguated names such as `CODEX · MyApp · 2`.

Terminal-tab titles describe launch context rather than continuously tracking internal working-directory changes.

## Operation History UX

`/history` should render a readable visual overview of app-owned filesystem operations rather than plain terminal text.

The view should prioritize:

- operation description,
- time,
- source/destination where useful,
- reversibility,
- a clear Undo or Restore action where available.

Command recall remains lightweight and familiar: Up/Down arrows cycle through the Files command bar's previously entered text.

Do not visually conflate command-entry recall with the filesystem operation journal.

## Persistent History UX

`/history` may group entries by current and previous sessions.

Current-session entries can expose Undo/Restore where valid. Previous-session entries remain visible but informational only.

Users should not be expected to manually maintain the operation log. Retention should happen automatically, with advanced controls only if needed.

## History Retention UX

Users should not have to think about history retention.

Version one is expected to keep a rolling 50-operation journal automatically. Bulk operations remain single top-level entries and may expand to show affected files.

A Clear History control may exist in Settings but should not be presented as routine maintenance.

## Undo Conflict UX

When undo encounters an existing item at the restore destination, stop and ask rather than silently overwriting.

Conflict choices:

```text
Replace
Keep Both
Skip
Cancel Undo
```

Bulk conflicts may include Apply to All.

Replace should never be the default-selected action.

Normal app deletion should feel native to Windows and respect the user's Recycle Bin behavior rather than introducing another trash concept.

## Recycle Bin and Virtual Locations

Recycle Bin should appear as a readable Files workspace location, never as the raw `$Recycle.Bin` hierarchy.

The view should behave like part of the same workspace: users can browse items, select them, and restore them through clear native actions.

Virtual locations should remain visually understandable as special destinations without requiring a separate complex navigation system.

Recycle Bin does not have to occupy permanent sidebar space; the sidebar remains user-controlled.

## Undo Scope and Complex Operations

Do not show Undo controls for operations the application cannot reliably reverse.

Simple move/rename operations may expose Undo when valid.

`/unzip` and `/tidy` can appear in `/history` without an Undo action.

If `/tidy` becomes part of the workspace, favor a clear preview/confirmation experience before applying organizational changes rather than promising a complex rollback afterward.

## Command Execution UX

The Files command bar should feel predictable:

```text
/command     → workspace action
shell input  → real shell
known CLI    → hosted terminal tab when registered as interactive
```

Known `@` references can enhance shell commands without claiming ownership of all `@` syntax.

Application errors should be concise and corrective. Shell failures should preserve the shell's authentic output.

Do not use invisible AI decisions to change where identical commands execute.

## Command Result Surface

The command bar stays one line.

After execution, it may report:

```text
✓ Completed · 6 lines        [View]
! Failed · exit code 1       [View]
✓ Moved 12 files             [Undo]
```

The most recent command's View/result affordance remains visible until the next command is actually executed. Typing or editing another command does not remove it.

`View` uses the main workspace area. It never grows a terminal pane upward over the file hierarchy.

Closing a result view returns to the prior Files hierarchy view/state.

View-oriented commands such as `/history`, `/where`, and `/disk` may open their workspace view immediately.

> The command bar reports. The workspace explains.

## Last Output Lifetime

The most recent finite command's result remains inspectable until another command is actually executed.

Users may begin typing or editing their next command without losing the previous `[View]` result.

There is no expiration timer and no multi-result output stack in version one.

After the next command executes, the previous finite shell output may be discarded.

`/history` does not archive arbitrary shell output; it remains focused on app-owned operations.

## Files View Language

Rich command results should read as natural sub-views of Files:

```text
Files
Files · History
Files · Where — python
Files · Disk
Files · Recycle Bin
```

The command syntax remains separate:

```text
/history
/where python
/disk
```

> Commands use symbols. The interface answers in English.

A rich view occupies the main workspace surface while preserving the underlying folder location, selection, and scroll state. Back returns directly to that Files state.

Version one should avoid deep nested rich-view navigation.

### Visibility Tradeoff

While a rich view is open, the full file hierarchy is intentionally not simultaneously displayed. This protects the flat, uncluttered terminal-workspace aesthetic.

Any future solution for referencing the hidden underlying Files state should remain lightweight and should not introduce a permanent split pane merely to solve occasional contextual needs.

## Rich Views and Selection

Rich views can be interactive without becoming selection surfaces.

> Rich views contain controls and results. Files contains filesystem selection.

`@selection` always refers to the selected filesystem item(s) in the underlying Files hierarchy.

`Files · History` rows are not selected. Users press explicit controls such as:

```text
[Details] [Undo] [Restore]
```

Results in views such as `Files · Where` can expose:

```text
[Open] [Go to]
```

`Go to` returns to/reveals the item in Files and can establish a normal filesystem selection there.

Likewise, `Files · Disk` can use `[Open]` to navigate into a real filesystem location.

While a rich view is displayed, the command bar continues using the preserved underlying Files context for `@thisfolder` and `@selection`.

This avoids introducing a permanent split pane simply to keep Files visible. Back immediately restores the hierarchy exactly where the user left it.

A dedicated Peek Files control is not required in version one.

> A clickable result is not automatically a selection.

## Strong Keyboard Support

Strong keyboard support is part of the core interaction model.

The application combines a clickable filesystem with terminal control, so neither mouse nor keyboard users should encounter a major workspace surface that effectively requires the other input method.

### Rich-View Navigation

Rich views are fully usable by mouse or keyboard.

Baseline keyboard behavior:

```text
↑ / ↓   previous / next primary action
Tab     next available control
Enter   activate focused control
Esc     return to Files
```

When a rich view opens, focus should enter the view at a sensible actionable control.

### Focus Is Not Selection

Do not highlight an entire History/Where result row in a way that resembles file selection merely to support keyboard navigation.

Instead, focus the action itself:

```text
Moved 4 files → src/       [Details] [Undo]
                                      ^ focus

C:\Python313\python.exe     [Go to] [Open]
                             ^ focus
```

This creates a clear visual grammar:

```text
file highlight         = filesystem selection
button focus indicator = keyboard focus
command-bar caret      = command focus
```

`@selection` therefore remains unambiguous even while a user navigates a rich view entirely by keyboard.

The exact shortcut for jumping directly back to the command bar is still to be decided.

## Space-to-Command

The command bar is always one simple key away.

```text
neutral workspace
      ↓ Space
command bar focused
```

Space redirects focus only from neutral workspace surfaces. It remains normal input when focus is in text/editable fields, the command bar, buttons, checkboxes, or other controls that legitimately use Space.

> From any neutral workspace surface, press Space and type.

A subtle onboarding hint such as `Space to command` may be considered, but it should not become permanent visual clutter.

## `/run` Execution UX

Execution should be readable without requiring shell-specific invocation syntax.

```text
/run tool.exe
```

means run `tool.exe` from the current Files location.

Users can be more explicit when needed:

```text
/run @thisfolder\tool.exe
/run @projects\tool.exe
/run "C:\Program Files\Tool\tool.exe"
```

This keeps the language readable:

```text
/run   @projects\tool.exe
ACTION REFERENCE + TARGET
```

Do not make users repeat `@thisfolder` for ordinary relative targets.

If the target cannot be resolved locally, report that clearly rather than unexpectedly searching the entire machine. A useful corrective action may be:

```text
Not found in this folder.
Try: /where tool.exe
```

Power users remain free to use normal shell execution syntax.

## Path Language Boundary

The interface should teach a simple boundary:

```text
/   action
@   reference
raw shell/path syntax   PowerShell
```

The application should not make a bare Windows path secretly mean Navigate, Select, Open, or Run.

Simple workspace behavior is expressed explicitly:

```text
@projects
/run tool.exe
```

Power users can continue using familiar shell forms:

```text
cd C:\Projects
.\tool.exe
& "C:\Program Files\Tool\tool.exe"
```

This keeps skills learned in the shell transferable outside the application.

## Shell Backend UX

Version one presents a consistent PowerShell-backed command-bar experience without requiring users to manage shell choices.

The architecture may support additional explicit shell choices later, but the application should never silently change the shell because the user navigated to another folder or project.

The visible workspace language remains stable:

```text
/ = app action
@ = workspace reference
ordinary shell input = selected shell
```

In v1, the selected/guaranteed shell is PowerShell.

## Files Navigation vs. Rich Views

Keep filesystem navigation and command-driven rich views visually and behaviorally separate.

```text
Back
→ dismiss rich view first
→ otherwise previous Files location

Forward
→ next Files location only

Up
→ parent directory only

Esc
→ dismiss active rich view
```

A dismissed `Files · History`, `Files · Where`, `Files · Disk`, or command-output view is not restored by Forward. The user invokes it again through its command.

> Rich views are invoked, not visited.

## Fast, Shallow File Interaction

Users should feel fast rather than buried in menus.

Baseline GUI behavior remains familiar:

```text
single click       select
double-click       open
Enter              open
F2                 rename
Delete             delete
Ctrl+C / X / V     clipboard operations
Space              focus command bar from neutral workspace
```

### Context Menu

Keep right-click shallow and visually compact:

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

Do not mirror every command-bar action into this menu. Avoid nested `More > Tools > Advanced` style interaction.

The preferred hierarchy is:

```text
keyboard shortcuts  → fastest
direct GUI           → obvious
command bar          → powerful
rare/advanced        → available without primary-menu clutter
```

> The context menu handles obvious manipulation; the command bar handles capability.

> Do not bury capability in menus. Give common actions direct interactions and let the command bar carry the long tail.

GUI Open follows Windows associations/default behavior. `/run` remains the explicit command-language expression of execution intent.

## Readability Over Abbreviation

Keep `@thisfolder` even though it is longer than alternatives such as `@here`.

It reads clearly in context:

```text
/unzip archive.zip @thisfolder
/info @thisfolder
```

Typing speed should come from autocomplete, not from introducing shorter ambiguous aliases.

```text
@t
→ @thisfolder
```

Typing `@` can expose the small reference vocabulary, with conventional keyboard completion.

> Readable when seen. Fast when typed.

## Command-Bar Completion

Keep autocomplete small enough to teach the workspace language without turning the command bar into an IDE.

```text
/       discover/complete app commands
@       discover/complete known references
Tab     complete app suggestion
Up/Down browse suggestions
Esc     dismiss
Enter   execute
```

Do not use Enter as a hidden completion key. Do not add a separate v1 behavior where Tab cycles through files in the visible folder. Outside recognized `/` and `@` completion, preserve the selected shell's native Tab behavior.

> We autocomplete what we invented. The shell completes what it owns.

## Multi-Selection Behavior

Keep `@selection` predictable: it always means all selected filesystem items.

Commands decide what they can accept.

Examples:

```text
/run @selection
→ run selected targets

/info @selection
→ show info for selected targets

/where python
→ one search query

/unzip @selection
→ validate selected archives
```

Do not silently use only the first selected item when multiple items are selected.

If a command cannot accept the selection, explain why and what input is expected.

Large `/run @selection` batches may ask for confirmation before launching everything.

> References do not guess; commands validate.

## Command-Driven File Manipulation

Keyboard users can perform common filesystem operations directly from the command bar:

```text
/copy @selection @projects
/move @selection @projects
/rename @selection README.md
/delete @selection
```

This does not replace familiar shortcuts:

```text
F2        rename
Delete    delete
Ctrl+C/X/V clipboard
```

The command versions exist for direct source/destination workflows and for users who prefer to stay in the command surface.

Do not add `/paste` just to reproduce clipboard behavior.

> The command bar should be able to operate the filesystem, not just launch utilities.

## Where vs. Find

Keep the distinction visible and understandable in plain English:

```text
/where python
→ Files · Where — python
→ Where does this program/tool live?

/find config.json
→ Files · Find — config.json
→ Find matching filesystem items here.
```

`/find` defaults to the current Files location and may accept an explicit scope such as `@projects`.

Both use the same rich-view interaction model: mouse or keyboard, explicit Open/Go to actions, Back/Esc to return, and no change to `@selection`.

## Files · Info

`/info` should feel like quick inspection, not a property-sheet dump.

### Single file

```text
Files · Info

tool.exe

Type        Application
Size        14.8 MB
Path        D:\Projects\App\tool.exe       [Copy]
Created     Aug 20, 2026
Modified    Aug 24, 2026

Architecture  x64
Version       1.4.2

Checksum                              [Calculate]

[Windows Properties]
```

Only relevant type-specific fields appear.

### Folder

```text
/info @thisfolder

Files · Info

My Project

Size        2.84 GB
Files       1,482
Folders     96
Modified    Aug 24, 2026
Path        D:\Projects\My Project
```

For a large hierarchy, show the view immediately and calculate recursively without blocking:

```text
Size        Calculating…
Files       18,420…
Folders     1,203…
```

### Multiple selection

```text
/info @selection

Files · Info

37 items

Total Size  684 MB
Files       31
Folders     6
Location    D:\Projects\My Project
```

Summarize the set rather than displaying dozens of individual property sheets.

Back/Esc dismisses Info and returns to Files. Info never becomes a Forward-history destination.

## Files · Places

`/places` is the quick system-folder view:

```text
Files · Places

Home
Desktop
Documents
Downloads
Pictures
Music
Videos
```

Only valid locations are shown. The view is intentionally temporary because persistent sidebar Locations belong to the user/projects.

## Files · Drives

`/drives` provides quick drive navigation:

```text
Files · Drives

Windows (C:)       218 GB free
Projects (D:)      640 GB free
Backup (E:)        1.2 TB free
Network (Z:)
```

Keep it concise. It should answer "where can I go?" rather than becoming Disk Management.

Both views support keyboard/mouse navigation, Back/Esc dismissal, and the established rich-view focus rules.

> Keep personal locations persistent; summon system locations when needed.

## No Recent View in Version One

Do not add a `Files · Recent` rich view in v1.

Tabs, Back/Forward, personalized Locations, `/places`, `/drives`, and `/find` already provide multiple fast routes back to useful filesystem locations. Avoid adding another activity surface until users demonstrate a need for it.

## No Disk Analysis Rich View in Version One

Do not expose `Files · Disk` or invent a replacement `/space`/`/storage` command in v1.

Use `/drives` for concise drive capacity/free-space information and `/info` for folder/selection size. A dedicated storage-consumption analyzer can be designed later if users demonstrate a need.

## No Interactive-Mode Command in Version One

Do not expose `/interactive` as part of the core command language.

Known interactive tools should simply open in the correct hosted terminal behavior when launched. The process model remains invisible unless future advanced settings are genuinely needed.

This keeps the slash vocabulary focused on user goals rather than internal terminal classifications.

## Files · Tidy

`/tidy` is a command-driven organization workflow for loose files in a chosen folder.

Examples:

```text
/tidy @desktop
/tidy @downloads
/tidy @thisfolder
```

A completed operation can leave a compact command-bar result:

```text
✓ Tidied 47 files                         View
```

The optional rich result can show:

```text
Files · Tidy

47 files organized

Documents       14
Photos          11
Installers       8
Audio             6
Videos            5
Archives           3

2 skipped
3 unchanged
```

Do not mix Desktop icon-layout automation into this view. Desktop behaves like any other target folder.

Do not silently reorganize existing subfolders or overwrite conflicts.

### Confirmation

The UX for pre-execution confirmation is deliberately unresolved. Do not assume that every Tidy operation needs an extra confirmation click; decide that separately against the command safety model.

## Tidy Has No Routine Confirmation Step

Do not interrupt normal `/tidy` execution with a preview or confirmation dialog.

The desired interaction is:

```text
/tidy @downloads
Enter
↓
organization runs
↓
✓ Tidied 47 files · 2 skipped             View
```

The result remains visible under the established command-result behavior until another command executes. `View` opens the detailed `Files · Tidy` result.

This speed is intentional. Avoid confirmation fatigue for an explicitly invoked, deterministic, conservative cleanup command.

## Partial Success and Conflict Views

Do not stop an entire batch because one unrelated item needs attention.

Example:

```text
9 moved · 3 need attention

invoice.pdf
Already exists
[Replace] [Rename] [Skip]

database.db
File is in use
[Retry] [Skip]

config.sys
Permission required
[Retry as administrator] [Skip]

Esc  Skip remaining and close
```

Back/Esc does not undo the nine completed moves. It skips unresolved work and returns to Files.

The command bar then shows a completed partial result:

```text
⚠ Moved 9 of 12 · 3 skipped               View
```

Avoid a generic `Cancel` label after partial work has already completed because it implies rollback.

The interaction should feel like handling independent physical tasks: one blocked item does not prevent unrelated items from being put where they belong.

## Destination Collision View

For explicit copy/move conflicts:

```text
invoice.pdf already exists in Documents

[Replace]   [Keep Both]   [Skip]

☐ Apply choice to remaining conflicts
```

`Keep Both` automatically creates a unique name such as `invoice (2).pdf`.

The apply-to-remaining option covers compatible destination collisions only; it does not apply a Replace/Keep Both decision to permissions, locked files, or other unrelated failures.

For Tidy, do not show this interruption:

```text
/tidy @downloads
↓
✓ Tidied 46 files · 1 skipped             View
```

The Tidy result can explain that the skipped item collided with an existing destination file.

## Permission and Elevation UX

Default state:

```text
PowerShell
```

Advanced elevated state:

```text
PowerShell · Admin
```

Keep the elevated indicator persistently visible while that shell is elevated.

For app-owned operation conflicts:

```text
config.json
Administrator permission required

[Retry as administrator] [Skip]
```

Retry invokes Windows UAC. Esc/Back skips unresolved privileged items and keeps already completed work.

Do not make the entire Files application elevated merely to avoid permission prompts.

Do not let an elevated shell invisibly alter slash-command safety semantics.

## Locked File Attention

```text
project.db
File is in use

[Retry]   [Skip]
```

Do not offer force-unlock or kill-process as routine file-operation actions.

## Read-Only Attention

Only interrupt when the requested operation genuinely needs to modify/replace/delete the read-only target:

```text
settings.ini
File is read-only

[Continue]   [Skip]
```

Do not treat read-only as a problem for ordinary reading, copying, inspection, search, or permitted movement.

Network and protected-location failures use the same attention-view language rather than spawning special modal systems.

## Task Tabs

Long-running filesystem work may automatically become a task tab:

```text
Files | Projects | Copying 184 GB…
```

Example content:

```text
Copy
Projects → Backup

████████████████░░░░ 78%

142.6 GB / 184.1 GB
8,421 / 10,204 files
Current: assets\models\character.lwo

3 need attention

[Pause] [Cancel]
```

The originating Files tab stays usable.

When complete:

```text
✓ Copy · Backup
```

Keep the completed tab available for inspection until the user closes it.

Do not create task tabs merely because `/info` or `/find` takes time; those rich views should progressively update in place.

Do not ask users whether an operation should be backgrounded during normal use. The application makes the routing decision.

## WPF Is Not the Visual Language

The implementation uses WPF, but designers and coding agents must not interpret that as permission to use default WPF appearance as the finished product.

Avoid:

```text
dated stock control styling
generic enterprise-form layouts
unnecessarily heavy borders/chrome
default-looking WPF tabs/buttons as final design
```

Target the established Files visual direction:

```text
modern terminal/developer-tool character
compact command-centric interface
clean Files hierarchy
purposeful tabs
subtle state indicators
strong keyboard focus states
rich command views
dark/light theme support
restrained visual chrome
```

Custom WPF styles/control templates should implement the design rather than letting framework defaults define it.

Performance is also part of UX: filesystem and process work must never visibly freeze the interface.

## Windows Behavior Without Explorer Aesthetics

Using Windows APIs underneath must not make the product visually resemble Explorer.

Keep the custom design:

```text
terminal-leaning command surface
compact tabs
clean file hierarchy
shallow context menus
rich command views
strong keyboard interaction
restrained chrome
```

Avoid importing Explorer's visual language merely because the app relies on Windows services.

## Anti-Slop Visual Guardrails

Do not default to generic AI-generated application styling.

Avoid:

```text
oversized cards
gratuitous gradients
decorative dashboard sections
random excessive icons
nested panel-on-panel layouts
bloated Settings screens
generic SaaS visual language
```

Every visible element should support navigation, file interaction, command interaction, status, or comprehension.

> Fast app, fast people: reduce visual friction rather than decorating it.

## Command-Bar Location Model

The visible Files location and command-bar shell context should match.

```text
Files
D:\Projects\Website\src

PS D:\Projects\Website\src> _
```

Do not display a command bar that quietly operates from a different directory than the Files view.

If users want a separate PowerShell context, they can run:

```text
powershell
```

which opens:

```text
[ Files ] [ PowerShell ]
```

The terminal tab becomes independent after launch.

> The Files command bar belongs to Files. Terminal tabs belong to themselves.

## PowerShell Location Feels Native to Files

When the user types:

```powershell
cd D:\Projects
```

the visible Files hierarchy should navigate to `D:\Projects`.

When the user browses Files into another folder, the Files command bar should operate from that same filesystem location.

This bidirectional synchronization applies only to filesystem-backed PowerShell locations.

Do not visually turn PowerShell provider locations such as `HKLM:\` into fake filesystem folders.

## Terminal Handoff

Known interactive commands should open a real terminal tab rather than leaving the command bar stuck in an interactive prompt.

The user should experience a clear distinction:

```text
finite shell command
→ compact result + View

interactive program
→ terminal tab
```

## Files and Command-Bar Context Never Split

Do not allow this:

```text
Files:
D:\Projects

Command bar:
PS HKLM:\>
```

The user must never have to wonder which location the command bar actually controls.

If PowerShell attempts:

```powershell
cd HKLM:\
```

the app opens a fresh ConPTY-backed PowerShell terminal initialized at that provider location.

Files remains at its filesystem location, and its command bar stays aligned with Files.

The terminal does not inherit arbitrary variables, functions, aliases, or other session state from the Files runspace.

> Files and the Files command bar are one filesystem context.

## Terminal Tab Lifecycle

Launching:

```text
claude
```

from:

```text
D:\Projects\App
```

may create:

```text
[ Files ] [ Claude · App ]
```

The terminal internally runs PowerShell at `D:\Projects\App` and launches Claude inside it.

When Claude exits:

```text
PS D:\Projects\App>
```

remains.

When the user exits PowerShell:

```powershell
exit
```

the terminal tab closes.

Do not show a dead-terminal page or Restart button after a normal shell exit.

Terminal tabs inherit the Files folder at launch only; thereafter they are independent.

The terminal should feel like a normal terminal, not like the Files command bar transplanted into another tab.

## Surface Visual Hierarchy

### Files hierarchy

Purpose: browse and select filesystem content.

```text
Files
────────────────
src/
docs/
README.md
package.json
```

### Rich view

Purpose: inspect or interact with command-driven information.

```text
Files · Info
────────────────
README.md

Type       Markdown
Size       18 KB
Modified   Today
Path       D:\Project\README.md
```

Rich views must not be mistaken for another filesystem folder.

### Task tab

Purpose: monitor/manage persistent work.

```text
Copy · Backup
────────────────
78%
142.6 GB / 184.1 GB
8,421 / 10,204 files

3 need attention

[Pause] [Cancel]
```

Task tabs should visually mirror the rich-view family: same typography, spacing discipline, metadata/result grammar, action styling, progress/error language, and restraint.

Their lifecycle communicates the difference: rich views temporarily replace Files content and return with Back/Esc; task tabs live independently and persist.

> Reuse the frame and visual language, not the identity of the content.

## Transparent Configuration

The settings system should not require users to understand a database or Registry layout to back up their personal configuration.

Saved Locations and user-facing preferences live in a readable:

```text
%AppData%\<AppName>\settings.json
```

This is primarily an engineering/storage decision; normal users still manage settings through the app UI.

Advanced users may inspect/edit the file directly without being presented with generated framework noise.

## Tidy Is a Native Files Feature

The user should never experience `/tidy` as an external utility launching behind the scenes.

It behaves like every other app-owned command:

```text
/tidy @downloads
↓
✓ Tidied 47 files · 2 skipped    View
```

No legacy Desktop-icon positioning UI or behavior appears in Files.

The rich result uses the shared rich-view visual language.

## Installation Choices

Offer two straightforward release choices:

```text
Installer
→ normal Windows installation

Portable
→ extract and run
```

Do not turn release/install choices into a complicated onboarding flow.

## Update UX

Updates are never silently forced.

Example:

```text
Version 1.2.0 is available

[View Changes]   [Update]   [Later]
```

Keep update messaging compact and non-blocking unless a future update is genuinely required for compatibility or safety.

Portable and installed users should both understand what will happen before choosing Update.

## Product Identity

The application is **Filekin**.

`Files` remains valid terminology for the primary visual filesystem workspace. Product-level chrome, installer/release naming, About information, and public documentation should use `Filekin`.
