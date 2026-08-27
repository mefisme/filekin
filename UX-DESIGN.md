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

Keep the sidebar deliberately small: **LOCATIONS** chosen by the user and a compact **ACTIVE** sessions area. The sidebar `+` adds a Location; existing entries expose a compact Edit/Remove context menu. The same collection is keyboard-manageable through `/location add projects @thisfolder`, `/location set projects D:\NewPath`, `/location rename projects client-work`, and `/location remove client-work`. Removing a Location never removes its folder. `/recent`, `/drives`, and `/places` should render transient navigation in the main area instead of permanently consuming sidebar space.

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

When files are selected, the UI may subtly expose `@selection`. Assigned Locations should expose their aliases. Typing `/` and pressing Tab in the command area should open lightweight command discovery/autocomplete with concise explanations and argument hints.

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

`/tidy` can appear in `/history` without an Undo action. `/unzip` now exposes a session-scoped Undo
on its command result because extraction records every created path and recycled original.

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

A relative target is looked for in the visible Files folder first, then through the ordinary Windows `PATH` and `PATHEXT` lookup (DECISIONS.md, 2026-08-26). Filekin never searches the whole machine for an application. A target that resolves nowhere fails as Windows fails it, reported inline:

```text
✕ tool.exe: Could not start tool.exe: The system cannot find the file specified.
```

Where the target runs is visible in the result, not asked about. A console program or script opens a hosted terminal tab and the command bar stays quiet — the new tab is the feedback. A GUI application, shortcut, or associated document launches independently and the command bar reports `Launched tool.exe.` A folder is refused:

```text
✕ Projects: folders are navigated in Files, not run.
```

Power users remain free to use normal shell execution syntax.

### Offering a Terminal After the Fact

An unknown raw command starts in the finite runspace. If its executable is a concrete Windows console target and it is still running two seconds later, one offer appears below the command bar — the same in-app confirm strip every other Y/N question uses, never an OS dialog:

```text
tool is still running. Run it again in a terminal tab?   Y / N
```

`Y` stops it and starts the same command again as a **fresh** process in a hosted terminal tab; nothing is migrated, and the wording says "again" for that reason. `N` or `Esc` leaves it running and the status becomes:

```text
… tool is still running · Esc to stop
```

The offer is made at most once, and never after the user has already pressed `Esc`.

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
/ + Tab    discover/complete app commands
@ + Tab    discover/complete known references
Tab        accept highlighted app suggestion
Up/Down    browse visible suggestions
Esc        dismiss and preserve the draft
Enter      execute the typed text
```

Typing alone never opens the list. A unique match completes immediately; an ambiguous match extends the shared prefix and opens a compact overlay above the command bar. Command rows pair the token with a concise explanation; reference rows pair it with the resolved destination when available. The overlay does not resize the Files workspace.

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

## Archive Preview

`/unzip` and `/zip` normally open one shared archive preview before writing:

```text
Files · Extract archive.zip

D:\Photos\archive

☑ Into a folder      archive
☐ Replace existing files

photo-01.jpg                                    4.2 MB
photo-02.jpg                                    3.8 MB

[Extract] [Cancel]
```

For extraction, exactly one new folder is the predictable default: reuse an archive's existing
wrapper or create one named after the archive. Turning off `Into a folder` places the contents
directly in the destination. With several archives, each keeps its own proposed folder.

The list is the plan Filekin will actually execute, capped for rendering performance with an
`and N more` row when needed. Collisions and refused traversal entries are called out before the
action. Skip is the shipped collision default. Replace sends each original to the Recycle Bin first.

After a successful extraction or archive creation, the command result carries `Undo` while that
session operation remains reversible:

```text
✓ Extracted 34 files                              Undo
```

The Archives Settings category controls the shared preview default and whether collisions default to
Skip or Replace. `/unzip -y` skips the preview once; `/zip` deliberately has no command-line switches.

After the user starts extraction or compression, the archive surface is no longer modal. Back/Esc
returns to Files without cancelling the operation. While it runs, a compact task row below the
command input shows the archive title and current entry plus **View** and explicit **Stop** actions.
View reopens the live archive surface; Back/Esc can detach again. The operation result replaces that
live status when the work completes or stops. Undo appears only beside the archive result it reverses,
not beside a later unrelated command result.

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

It is a **field sheet**, not a listing: a fixed label column, the value, and an optional action on the right. No hover highlight, no hand cursor, nothing to navigate into. Places and Drives are lists of destinations to choose from; Info describes one thing.

Bare `/info` describes the current selection, or the visible folder when nothing is selected.

### Single file

```text
Files · Info

tool.exe

Type          Application (.exe)
Size          14.8 MB
Path          D:\Projects\App\tool.exe                     [Copy]
Created       Aug 20, 2026  9:14 AM
Modified      Aug 24, 2026  4:32 PM
Architecture  x64
Version       1.4.2
Company       Contoso Ltd

SHA-256       —                                       [Calculate]

[Windows Properties]
```

Only relevant type-specific fields appear. An empty value is never rendered as a blank row.

**Company, not Publisher.** That name is a string written inside the file. Filekin has not checked a signature, so it must not use a word that says it has. Verified signatures live behind Windows Properties.

A text file adds `Encoding` immediately and a `Lines` row with a `Count` action. Encoding costs nothing — deciding the file is text already read the first block — while counting lines reads the whole file, so it waits to be asked, exactly like the checksum.

A shortcut adds what it points at, and nothing that edits it:

```text
Type          Shortcut (.lnk)
Target        C:\Program Files\App\App.exe
Arguments     --project "D:\Work"
Start in      D:\Work
```

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

The trailing ellipsis is the honest signal that a number is still moving; it disappears when the scan finishes. The status line beside the Info title reads `Scanning…` while it runs.

A total that had to skip something says so rather than presenting itself as complete:

```text
Size        41.2 GB
Files       182,004
Folders     19,338

Some folders could not be read
```

Junctions and symlinks are counted as one link each and never walked into, so a folder that contains a link back to itself still finishes and still reports the truth. Closing the sheet stops the scan.

### Multiple selection

```text
/info @selection

Files · Info

37 selected items

Total size  684 MB
Files       31
Folders     6
Location    D:\Projects\My Project
Modified    Aug 12–26, 2026
```

A selection counts its own folders, so the item count adds up: 31 files plus 6 folders is the 37 items named at the top, and the size still includes everything inside those 6 folders. When the selected items come from more than one folder, Location says how many rather than naming one of them.

Windows Properties is a single-target action, so it does not appear on a multi-item sheet.

Summarize the set rather than displaying dozens of individual property sheets.

Back/Esc dismisses Info and returns to Files. Info never becomes a Forward-history destination.

## Files · Places

`/places` is the quick system-folder view:

```text
Files · Places

Desktop
Documents
Downloads
Pictures
Music
Videos
────────────
OneDrive — Personal
Dropbox
```

The fixed common section contains only Desktop, Documents, Downloads, Pictures, Music, and Videos. Home/user profile is intentionally absent. Only common folders that resolve are shown. The optional cloud section contains sync roots registered for the current Windows user and uses the Windows-provided provider/account name; it is omitted when no cloud roots are registered. Do not hardcode provider names or guess conventional folders.

Rows are direct navigation actions rather than selectable filesystem entities. Single-click or Enter navigates to the target and dismisses the rich view. The view is intentionally temporary because persistent sidebar Locations belong to the user/projects.

## Files · Drives

`/drives` provides quick drive navigation:

```text
Files · Drives

ROOT   LABEL       TYPE        SPACE
C:\    Windows     Local       218 GB free of 476 GB
D:\    Projects    Local       640 GB free of 1.8 TB
E:\    Backup      USB         1.2 TB free of 2 TB
Z:\    Team        Network     Unavailable
```

Capacity rows may include a restrained usage bar. Assigned removable, optical, or network drives that are disconnected or have no media remain visible but disabled with `Unavailable` or `No media`; do not hang the view trying to wake them. Single-click or Enter opens an available drive root.

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

the app opens/promotes that context into a PowerShell terminal tab.

Files remains at its filesystem location, and its command bar stays aligned with Files.

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

## The Settings Surface

Settings is a rich view over the preserved Files workspace, not a dialog. The sidebar footer entry and `/settings` open the same thing; Esc or Back returns to exactly the Files state that was underneath.

A category rail holds five subjects:

```text
Appearance   theme and accent colour
Startup      Open Files at launch
Terminal     which programs open in a terminal tab
Archives     preview and existing-file behavior
Advanced     the readable settings file itself
```

A new preference joins an existing category. The rail grows only when a genuinely new subject arrives, which is what keeps this from becoming the bloated Settings screen the visual rules already forbid. Categories are text — no glyphs beside four words.

Choices are single-click, like every other row in a rich view, and take effect immediately. Nothing here has a Save button, so there is never an unsaved state to lose on dismissal. A write that fails says so in place and keeps the previous value.

## Theme and Accent

```text
THEME              ACCENT
● Dark             ● Blue    ○ Orange
○ Light            ○ Teal    ○ Pink
○ Follow system    ○ Green   ○ Purple
```

A theme is a palette. Dark and light define the same colour roles on different grounds and change nothing else — the same fonts, the same spacing, the same layout. Anything coloured follows, including a hosted terminal: a dark terminal panel inside a light window is a half-applied theme, not a design choice.

Accent shades are muted rather than saturated, so one choice reads correctly on both grounds. The accent is a spark — the active marker, the directory names, the caret — and never takes over a surface. Semantic status colours are a separate set the accent never replaces.

## Startup Files Location

Settings exposes one compact choice labeled **Open Files at launch**:

```text
Home (default)
@projects
@client-work
Choose folder…
```

Saved Locations are presented by their current `@name`; choosing one means the startup target follows later path changes to that Location. `Choose folder…` stores an explicit filesystem folder instead. This is an intentional preference, not an automatic “reopen whatever I last viewed” behavior.

If the chosen Location is removed or the target cannot be opened, Filekin starts at Home and shows a small non-blocking notice. Temporarily unavailable paths remain configured rather than being silently erased.

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

## Command Bar Output — Adaptive / Hybrid Model

The Files command bar should remain visually quiet. Running a command must not automatically create a persistent console/output pane or cause the Files hierarchy to jump around.

> Output only occupies space when there is output worth occupying space.

### Command Bar Visual Rule

The default command bar is intentionally minimal:

```text
D:\GitHub\filekin  ›  git status_
```

The current filesystem path is visually quieter than the command text.

Do not add routine execution chrome such as a play button, favorite/star button, refresh button, PowerShell badge, or dropdown simply to explain that the command bar can run commands.

**Enter executes the command.**

### Successful Command With No Meaningful Output

If a command succeeds without useful textual output, do not open an output panel or rich view.

Example:

```text
C:\Projects › mkdir test
```

The filesystem hierarchy updates naturally and the command bar may show a small temporary success indication:

```text
C:\Projects › _                         ✓
```

The success state disappears without requiring dismissal.

### Small Text Result

A small useful result may appear temporarily inline immediately beneath the command bar.

Example:

```text
C:\Projects › git branch --show-current

  main
```

This is **inline output**, not a permanently allocated output console.

Inline output may disappear when the user executes another command, navigates, presses Esc, or otherwise leaves the result context. Exact dismissal/focus behavior may be refined during implementation without changing the core model.

### Substantial Command Output

Do not automatically expand a large console beneath Files.

For substantial output, show a compact execution result:

```text
C:\Projects › git status

✓ Completed · 14 lines                         View
```

Selecting `View` opens the complete result using the existing rich-view system:

```text
Files · Output
```

The underlying Files hierarchy/location/selection state remains preserved. Back/Esc returns to the exact Files context that existed before opening the output rich view.

### Errors

Useful errors should be visible immediately rather than hidden behind another action.

Example:

```text
C:\Projects › git stats

Command not found: git stats
```

If the complete error is large, show the concise useful failure inline and provide access to the full details:

```text
✕ Command failed                         View details
```

`View details` opens the complete result in the rich-view system.

### Interactive Commands

Interactive commands do not use Files inline/rich output.

Known interactive tools are routed to an independent ConPTY-backed terminal tab according to the established terminal-routing rules.

Example:

```text
C:\Projects › claude
```

becomes an independent tab such as:

```text
[ Files ] [ Terminal · Claude × ]
```

### Command History and Output

Recalling command history recalls the command text, not the previous output surface.

Previous output belongs to that execution event rather than permanently attaching itself to recalled command text. Persistent operation/history records remain governed by the separate history design.

### Adaptive Output Hierarchy

```text
no meaningful output
→ subtle transient status

small useful output
→ temporary inline result

substantial output
→ compact summary + View
→ Files · Output rich view

large error
→ concise inline failure + View details

interactive command
→ terminal tab
```

There is no permanently visible command-output console in the default Files layout.

This model keeps Filekin clean and fast while preserving access to full command results when needed.

## Sidebar Navigation Language — Locations and Filekin Surfaces

The Files sidebar is **not** a Windows Explorer navigation tree. It must not grow into a second filesystem hierarchy.

### Locations

The primary sidebar section remains titled:

```text
LOCATIONS
```

Locations are user-defined named filesystem destinations. They use Filekin's `@` reference language as their visual identity instead of Explorer-style folder, download, music, drive, or other content-type icons.

Conceptually:

```text
LOCATIONS

@ Projects
@ Downloads
@ Music
@ GitHub
@ SnapMap
```

The `@` marker should be visually restrained and smaller than the location label. It is meaningful syntax, not a decorative oversized icon.

Do not automatically populate this section with Windows special folders, drives, Quick Access, This PC, bookmarks, or other Explorer concepts. A location appears here because the user deliberately created/kept that Filekin Location.

### Discoverable Filekin Surfaces

Mouse-first/new users must be able to reach Filekin's built-in `/places` and `/drives` surfaces without knowing or using the command bar.

Place these as direct navigation entries below the custom Locations:

```text
LOCATIONS

@ Projects
@ Downloads
@ Music
@ GitHub
@ SnapMap

────────────

/places
/drives
```

These entries use their literal `/` command syntax rather than conventional Explorer icons.

This reinforces Filekin's navigation language:

```text
@ = named user Locations
/ = Filekin surfaces
```

### `/drives` Behavior

`/drives` is **not** an expandable sidebar category and the sidebar must not list individual drives beneath it.

Selecting `/drives` changes the main Files content area to the Drives surface.

Conceptually:

```text
/drives

NAME          TYPE          SPACE
C:\           Local Disk    ...
D:\           Local Disk    ...
E:\           USB Drive     ...
```

Opening a drive from that surface enters the normal Files hierarchy at that drive root.

Example:

```text
/drives
→ D:\
→ D:\GitHub
→ D:\GitHub\filekin
```

Do not duplicate `C:\`, `D:\`, or other drive entries in the sidebar.

### `/places` Behavior

Selecting `/places` changes the main Files content area to Filekin's Places surface.

It does not expand Windows special folders beneath the sidebar.

The GUI entry and the command-bar `/places` command are two interfaces to the same Filekin surface.

### Design Principle

The sidebar is a compact navigation language, not a miniature file explorer.

Keep it limited to:

1. user-defined `@` Locations,
2. direct access to built-in `/` Filekin surfaces.

Filesystem hierarchy exploration belongs in the main Files view.

## Command Output Decision — Expandable Command Shell

Filekin v1 uses the **expandable command shell** as the primary presentation for substantial finite command output.

The earlier `Files · Output` rich-view/tab alternative is not the default command-output model.

### Collapsed State

After a finite command produces substantial output, preserve the Files hierarchy and show only a compact result beneath the command bar:

```text
D:\GitHub\filekin  ›  git status

✓ Completed · 14 lines                         View
```

The output itself remains hidden until the user explicitly requests it.

### Expanded State

Selecting `View` expands a temporary shell-output region directly beneath the command bar while keeping the Files hierarchy visible above.

Conceptually:

```text
FILES
────────────────────────────────────────
file hierarchy remains visible
────────────────────────────────────────
D:\GitHub\filekin  ›  git status
────────────────────────────────────────
On branch main
Changes not staged for commit:
...
────────────────────────────────────────
14 lines                              Collapse
```

Once expanded, the action must read **`Collapse`**, not `View` or `View Output`.

`Esc` also collapses the shell output and returns focus appropriately to the Files/command-bar context.

### Spatial Relationship

The expanded output belongs to the command that produced it. Keeping it directly attached to the command bar makes the relationship obvious and preserves visual context with the filesystem above.

Do not open a new output tab merely because a normal finite shell command produced substantial text.

### Rich Views Remain a Separate Concept

Rich views remain part of Filekin, but they are reserved for information Filekin can meaningfully structure, enhance, or present as an application-native surface.

Examples may include Filekin-native search/results, Places, Drives, history, tasks, or other structured features defined elsewhere.

The mental model is:

```text
finite shell command
→ command bar
→ expandable shell output when needed

Filekin-native structured feature
→ rich Filekin view/surface

interactive CLI
→ independent terminal tab
```

### No Permanent Console

The expandable shell does not create a permanently visible console.

It exists only when the user explicitly expands command output and collapses cleanly back into the normal Files layout.

The default Files workspace remains dominated by the filesystem hierarchy, not terminal output.

## UI Control Discipline — No Decorative or Speculative Chrome

Filekin must not add controls merely because they are conventional in file managers, terminals, IDEs, or AI-generated UI mockups.

Every visible control must have:

1. a defined user-facing function,
2. a demonstrated need in the Filekin workflow,
3. an intentional location in the interaction model.

If those conditions are not met, the control does not belong in the UI.

### Command Bar

The Files command bar is especially strict. Do not add speculative controls such as:

- shell-selection dropdowns,
- trash/delete-output buttons,
- pop-out buttons,
- duplicate/open-in-new-panel buttons,
- copy-output icons,
- run/play buttons,
- refresh buttons,
- favorites/stars,
- arbitrary overflow menus,
- decorative status controls.

The established command-bar interaction is intentionally minimal:

```text
D:\GitHub\filekin  ›  git status
```

Enter executes.

For substantial finite output, the collapsed state provides the established `View` action. When output is expanded, that action becomes `Collapse`; Esc also collapses it.

```text
D:\GitHub\filekin  ›  git status

✓ Completed · 14 lines                         Collapse

<command output>
```

Do not invent additional command-bar/output controls during implementation or visual design.

New controls require an explicit product/UX decision before they are added.

### General Principle

Empty space is not a problem that needs to be filled with controls.

Filekin should prefer fewer, understandable controls over familiar-looking but unnecessary chrome. Its visual identity comes from deliberate interaction, typography, spacing, hierarchy, and its `@` / `/` navigation language—not from accumulating generic developer-tool widgets.
