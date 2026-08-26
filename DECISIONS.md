# Decisions

This document records important product decisions and, more importantly, why they were made.

Decisions can be revisited as the product develops.

## 2026-08-24 — Visual Terminal Direction

**Decision:** The entire application should use a semi-terminal visual language rather than presenting a conventional File Explorer interface above a terminal.

**Reason:** This gives the product its own interaction model and identity instead of making it another Explorer replacement with an embedded shell.

## 2026-08-24 — Shared GUI and Shell State

**Decision:** GUI navigation and terminal navigation should represent the same filesystem state.

**Reason:** The terminal and visual filesystem are intended to be two ways of interacting with the same location, not independent panes.

## 2026-08-24 — Use a Real Shell

**Decision:** Normal shell commands should execute through a real Windows shell.

**Reason:** Users should gain the power and transferable knowledge of an actual shell rather than being trapped in a proprietary command language.

**Avoid:** Reimplementing all shell functionality as custom commands.

## 2026-08-24 — Slash Commands Extend the Shell

**Decision:** Purpose-built application utilities may use `/command` syntax alongside normal shell commands.

Initial concepts include:

- `/where`
- `/unzip`
- `/tidy`

**Reason:** These commands solve opinionated workflows that are awkward with ordinary Windows UI while keeping the underlying terminal useful on its own.

## 2026-08-24 — Deterministic Filesystem Operations

**Decision:** AI should not be required for ordinary filesystem operations.

**Reason:** Moving, deleting, extracting, renaming, and similar operations should be predictable and testable.

AI may assist with interpretation, discovery, explanations, and intent where that provides real value.

## 2026-08-24 — Avoid Permanent Split Terminal

**Decision:** Current design preference is a command area that can expand for terminal output rather than permanently dividing the application into file-manager and terminal halves.

**Reason:** A permanent split risks making the product feel like two applications stacked together. The desired experience is one unified filesystem interface.

**Status:** This remains open to revision during UX exploration.

## 2026-08-24 — Do Not Package Yet

**Decision:** Keep the project as editable Markdown documents while product discussion continues. Do not create the final ZIP/package yet.

**Reason:** The design is actively evolving.


## 2026-08-24 — Separate Quick Commands from Persistent Terminal Sessions

**Decision:** The compact command area should remain optimized for quick operations, while interactive or persistent terminal applications may open as full terminal tabs or panes.

**Reason:** Applications such as Codex CLI and Claude Code need a persistent terminal environment. Allowing them to take over the compact command interface would undermine the unified file-navigation experience.

**Status:** Product direction accepted. Exact terminal embedding implementation remains undecided.

## 2026-08-24 — Terminal Sessions Are Filesystem Context

**Decision:** Terminal sessions should retain and expose the filesystem location from which they were launched.

**Reason:** The product is organized around locations. A terminal session is more useful when represented as `CODEX · MyApp` than as another anonymous `PowerShell` window.

This also creates the possibility of showing active sessions directly beside directories in the filesystem view.

## 2026-08-24 — Support External Terminals

**Decision:** Users should be able to launch terminal applications in their preferred external terminal instead of being forced to use embedded terminal tabs.

**Reason:** The application should improve terminal organization without replacing terminal preferences users already have.

**Status:** Accepted principle. Detection/configuration of external terminals is an implementation question.

## 2026-08-24 — Filesystem-Centered Workspace Direction

**Decision:** The product may extend beyond a traditional file manager into a filesystem-centered Windows workspace that organizes files, shell commands, CLI applications, and active terminal sessions around filesystem locations.

**Reason:** This follows naturally from the unified GUI/shell model and addresses the practical problem of terminal-window sprawl.

**Status:** Direction accepted; scope should remain controlled as the design develops.

## 2026-08-24 — User-Controlled Sidebar Locations

**Decision:** The sidebar should primarily contain explicitly assigned locations rather than Explorer standard places.

## 2026-08-24 — GUI and Command-Line Balance

**Decision:** Do not weaken mouse navigation to force terminal use.

**Principle:** The GUI gets you there; the command line gets you there faster.

## 2026-08-24 — Persistent vs. Transient Context

**Decision:** Permanent UI is for persistent user context; commands are for transient context. Recent directories, standard Windows places, drives, `/where` results, searches, disk analysis, and archive operations should generally be summoned rather than permanently shown.

## 2026-08-24 — Small Language, Large Capability

**Decision:** The application's simple language should be built around `/` for actions and `@` for context/references, while avoiding unnecessary new syntax.

**Reason:** Users should be able to learn the application naturally through repeated patterns rather than memorizing a proprietary command language.

## 2026-08-24 — Interface Teaches Its Language Through Use

**Decision:** Command discovery, selection states, Location aliases, argument hints, and restrained contextual tips should teach users the command language during normal work.

**Reason:** The product should be self-learning rather than tutorial-dependent.

## 2026-08-24 — Full Shell Compatibility Is Non-Negotiable

**Decision:** The simple command layer must sit on top of a real shell and must not replace or restrict ordinary shell commands.

**Reason:** The application should provide an approachable path into terminal use without limiting users once they become proficient.

**Boundary:** `/commands` may provide rich application UI; ordinary shell commands retain authentic shell behavior.

## 2026-08-24 — Context References Should Cooperate With Shell Commands

**Decision:** Where safe and technically feasible, `@` references should be resolvable inside ordinary shell commands.

**Reason:** References become a useful filesystem shorthand rather than a separate isolated language.

## 2026-08-24 — Slash Discovery Instead of a Required Separate Command Palette

**Decision:** Typing `/` in the command area should provide discovery/autocomplete for application commands. A separate command palette is not currently a core requirement.

**Reason:** Command discovery belongs directly in the interaction surface users are learning and avoids duplicating UI systems.

## 2026-08-24 — Confirm Core Terminal Workspace Features

**Decision:** Persistent CLI tabs, terminal split panes, preferred external-terminal support, contextual session names, and filesystem-linked active-session awareness are confirmed product capabilities.

**Reason:** Together they directly address terminal-window organization while reinforcing the filesystem-centered workspace model.

## 2026-08-24 — Confirm Core Utility and Recovery Features

**Decision:** `/where`, `/unzip`, `/tidy`, `/disk`, `/recent`, `/places`, `/drives`, archive preview, collision handling, supported undo, and operation history are confirmed directions.

**Reason:** These features fit the product's core model and can be expressed through the same small command language without requiring permanent UI clutter.

## 2026-08-24 — Keep Dual File Panes and Git Integration Proposed

**Decision:** Do not yet promote dual-pane file browsing or Git-specific UI to core requirements.

**Reason:** Dual file panes may duplicate workflows already handled cleanly by references and commands, while Git integration risks narrowing a general Windows filesystem workspace into a developer-focused IDE.

## 2026-08-24 — Known Interactive Tools Open Immediately in Terminal Tabs

**Decision:** Commands matched by known interactive-tool rules should open immediately in persistent terminal tabs.

**Reason:** Interactive tools such as coding agents, REPLs, SSH sessions, and TUIs need a full terminal environment. Starting them in the compact command area and moving them later would create unnecessary visual transitions and inconsistent behavior.

## 2026-08-24 — Interactive Tool Registry Is Built-In and User-Extensible

**Superseded on 2026-08-25:** For version one the built-in registry ships, but persistent user-owned interactive rules and the `/interactive` command are excluded (see "`/interactive` Is Out of Version One" below). The shipped `InteractiveCommandRegistry` is built-in only. User-extensible rules remain a possible future direction. The original decision below is retained for history.

**Decision:** Interactive command routing uses both a built-in registry and a user-owned registry.

Resolution order:

```text
1. Built-in interactive rules
2. User-added interactive rules
3. Explicit user launch choice
4. Normal shell execution
```

Users should be able to add or remove their own rules through the GUI and a slash command such as `/interactive`.

**Reason:** No hard-coded list can remain complete as new CLI/TUI applications appear. User extensibility keeps routing predictable without relying on fragile automatic guessing.

## 2026-08-24 — Interactive Rules May Be Argument-Sensitive

**Decision:** The interactive registry may distinguish invocation forms using simple argument rules.

**Example:** `python` may launch an interactive REPL, while `python script.py` should normally execute in the compact shell.

**Constraint:** The rule format should remain simple and should not evolve into another scripting language.

## 2026-08-24 — Unknown Commands Default to the Real Shell

**Decision:** If a command is not matched by a built-in or user-defined interactive rule, execute it normally through the real shell.

**Reason:** Predictable fallback behavior is preferable to aggressive process heuristics.

If the result is inconvenient, the user can mark that command as interactive for future launches.

## 2026-08-24 — `@` Means Reference

**Decision:** `@` means **Reference**, not merely filesystem context or folder.

A reference identifies an addressable object the workspace already knows about.

Examples include:

```text
@thisfolder
@parent
@selection
@projects
```

**Reason:** This definition is broad enough to represent useful workspace objects without requiring separate syntax for every object type.

`@` identifies; it does not execute. Actions belong to `/` commands or the real shell.

## 2026-08-24 — `/` Means Action / Application Command

**Decision:** `/` identifies an application-owned action or command.

Examples:

```text
/where
/unzip
/tidy
/disk
```

The basic application grammar is therefore:

```text
/action @reference
/action @source @destination
```

## 2026-08-24 — No Additional Syntax Sigils by Default

**Decision:** Do not introduce another special syntax character unless `/`, `@`, and the underlying real shell fundamentally cannot express a required capability.

**Reason:** The workspace language should remain deliberately small and should not evolve into a proprietary programming language.

Advanced variables, scripting, operators, pipelines, and similar concepts belong to the real shell.

## 2026-08-24 — Minimal Built-In Reference Set

**Decision:** Version one should expose only three built-in references:

```text
@thisfolder
@parent
@selection
```

User-assigned Locations automatically create additional references such as `@projects`.

**Reason:** The command language should prioritize simplicity, readability, and easy self-learning over a large built-in vocabulary.

## 2026-08-24 — Exclude `@last`

**Decision:** Do not include `@last` in the initial reference language.

**Reason:** "Last" is ambiguous: it could mean the last command, result, selection, source, destination, or location. Ambiguous shorthand conflicts with the product's readability goal.

## 2026-08-24 — Avoid Reference Synonyms

**Decision:** Do not provide multiple built-in names for the same concept.

For example, prefer:

```text
@thisfolder
```

instead of also supporting:

```text
@here
@cwd
@current
@folder
```

**Reason:** One readable term is easier to discover, remember, and teach through the interface.

## 2026-08-24 — Persistent Terminal Tab Creation

**Decision:** Create a persistent terminal tab for known interactive tools, known long-running processes, user-defined interactive rules, or an explicit user request to launch a command in a tab.

Finite commands remain in the compact command area regardless of how much output they produce.

Unknown commands default to normal shell execution.

Explicit shell launches such as `pwsh` and `cmd` create standalone terminal tabs.

**Principle:** A terminal tab is a persistent process container, not an expanded output window.

## 2026-08-24 — `/` and `@` Apply Only in the Files Command Bar

**Decision:** The application-owned `/` action syntax and `@` reference syntax are interpreted in the Files workspace command bar.

Ordinary shell commands entered there may still use `@` references, which are resolved before the command is passed to the real shell.

Hosted terminal tabs do not receive this preprocessing.

**Reason:** This keeps the convenience layer powerful without interfering with the native behavior of Codex, Claude Code, SSH, shells, REPLs, or other terminal applications.

## 2026-08-24 — Terminal Tabs Own Independent State

**Decision:** After launch, each persistent terminal tab owns its own terminal process state and working directory.

The Files tab may navigate elsewhere without changing a running terminal session.

**Principle:** Connected, not coupled.

## 2026-08-24 — Closing a Terminal Tab Ends Its Live Process

**Decision:** Closing a persistent terminal tab ends the live process hosted by that tab.

If the process is still running, require confirmation. If it has already completed, close the tab without unnecessary confirmation.

## 2026-08-24 — Graceful Termination Before Forced Termination

**Decision:** The Terminal Service should request graceful termination first, allow a short opportunity for clean shutdown, and use forced termination only when necessary.

**Reason:** Closing a workspace tab should not unnecessarily bypass cleanup behavior provided by the hosted application.

## 2026-08-24 — App Exit Ends Hosted Terminal Processes

**Decision:** Closing the application while terminal processes are running should show one consolidated confirmation. After confirmation, all hosted terminal sessions are ended using the same graceful-first shutdown policy.

The application should not silently detach processes and leave them running invisibly.

## 2026-08-24 — Live Terminal Sessions Do Not Survive Restart in Version One

**Decision:** Version one does not attempt to preserve or reconnect live terminal processes across application restarts.

Workspace metadata may persist, and previously used tools may be restarted as new processes in their previous launch directories.

Application-level session resumption offered by tools such as coding agents remains the responsibility of those tools.

**Principles:**

> Persist workspace context, not live processes.

> Version one should prefer obvious behavior over clever persistence.

## Proposed — App-Owned Interactive Terminal Sessions

**Superseded on 2026-08-25:** The terminal-host spike and production implementation validated the **shell-as-root** model instead. A hosted terminal tab's root process is PowerShell; the interactive tool runs as a child. When the tool exits, the PowerShell prompt returns; the tab closes when the root shell exits. See `ARCHITECTURE.md` §"Terminal Tab Hosting and Lifecycle", `ENGINEERING-GUARDRAILS.md` §"Terminal Lifecycle Guardrails", and the CLAUDE.md invariants. The tool-as-primary-process direction below is retained only for history.

Current direction: interactive-tool tabs host the launched application as the primary process. When that process exits, the tab becomes inactive and preserves its output rather than falling through to a hidden underlying shell.

This remains proposed until terminal-host implementation details are validated.

## Proposed — Preserve Completed and Failed Terminal Output

Completed or crashed processes should leave their terminal tabs open for inspection until the user closes them. Completed tabs close normally without a termination warning.

Exact status visuals remain unresolved.

## Proposed — Hosted Session Owns Its Child Process Boundary

Closing a hosted terminal session should attempt to shut down its associated process tree/session as a unit rather than intentionally orphaning children.

This requires Windows/ConPTY implementation validation before becoming a strict guarantee.

## Proposed — Duplicate Terminal Sessions Are Allowed

Multiple sessions using the same tool and launch location are allowed. Duplicate labels may use a numeric suffix.

## Proposed — Terminal Tab Names Describe Launch Context

Initial names use tool plus launch context, such as `CODEX · MyApp`. They should not continuously change merely because the hosted process changes its internal working directory.

User renaming and application-supplied titles remain unresolved.

## Proposed — External Terminal Lifecycle Is Externally Owned

When the workspace launches the user's preferred external terminal, that terminal owns its lifecycle afterward. Closing the workspace should not terminate externally launched terminal sessions.

## Proposed — Launch-Folder Changes Do Not Kill Sessions

Moving, renaming, or deleting a session's original launch folder should not automatically terminate the hosted process. The workspace may indicate that the stored launch context is stale.

## Proposed — Sleep and Hibernate Do Not Count as Exit

No special persistence architecture is required for sleep or hibernate in version one. If Windows preserves the application and process state, hosted sessions remain attached.

## 2026-08-24 — Interactive Tool Is the Primary Hosted Process

**Superseded on 2026-08-25:** Reversed in favor of the **shell-as-root** model, which the spike and production terminal-host validated and which the architecture, engineering guardrails, and CLAUDE.md invariants require. PowerShell is the root process of a hosted terminal tab; the interactive tool runs as a child of it; when the tool exits the shell prompt returns; the tab closes when the root shell exits. The superseded decision below is kept for history.

**Decision:** Version-one interactive terminal tabs host the launched interactive application as the primary process rather than keeping a hidden shell underneath it.

**Reason:** This avoids a tab silently changing identity after a tool exits and keeps the Files command bar's enhanced language clearly separate from raw hosted terminal applications.

## 2026-08-24 — Attached Child Processes Belong to the Hosted Session

**Decision:** Attached child processes are treated as part of the hosted terminal session for shutdown purposes.

Graceful shutdown is attempted first; force termination is a fallback for attached processes that remain.

Intentionally detached background processes are outside the guaranteed hosted-session boundary.

## 2026-08-24 — Preserve Completed and Failed Terminal Output

**Decision:** Normal completion and failure both leave the terminal tab open until the user closes it.

Use the conceptual states:

```text
● running
○ completed
! failed
```

Completed tabs close normally without confirmation. Failed tabs preserve their output and show exit status when available.

## 2026-08-24 — Duplicate Terminal Sessions Are Allowed

**Decision:** Multiple sessions for the same tool and launch location are allowed. Duplicate titles may use a numeric suffix.

## 2026-08-24 — Terminal Tab Names Describe Launch Context

**Decision:** Default terminal-tab names use `TOOL · launch-context` and do not continuously change with internal directory changes.

Manual renaming is not required for version one.

## 2026-08-24 — External Terminals Are Externally Owned

**Decision:** Once the application launches a preferred external terminal, that external terminal owns its own lifecycle.

Closing this workspace does not terminate or manage externally launched terminal sessions.

## 2026-08-24 — Launch-Folder Changes Do Not Terminate Sessions

**Decision:** Renaming, moving, or deleting a terminal session's original launch folder does not automatically end the running session.

## 2026-08-24 — Sleep and Hibernate Are Not Session Exit

**Decision:** Version one does not introduce a special persistence layer for sleep or hibernate. If Windows preserves the process state, sessions remain attached.

## 2026-08-24 — Undo Applies Only to App-Owned Filesystem Operations

**Decision:** The guaranteed undo system journals filesystem mutations executed by the application itself.

Arbitrary shell commands are excluded because their side effects cannot be reliably inferred or reversed.

## 2026-08-24 — `/undo` Is a Version-One Command

**Decision:** `/undo` reverses the most recent app-owned operation that remains safely undoable.

Keep the version-one syntax simple: no numbered undo, `@last`, or force flags.

## 2026-08-24 — `/history` Is a Version-One Command

**Decision:** `/history` provides a visual bird's-eye view of app-owned filesystem operations, when they occurred, and whether each remains reversible.

**Reason:** Undo is more trustworthy when users can see what the application changed and what can still be reversed.

## 2026-08-24 — Command Recall and Operation History Are Separate

**Decision:** Up/Down arrows navigate Files command-bar entry history exactly as originally typed.

`/history` is reserved for the app-owned filesystem operation journal.

**Principle:** Command history remembers what the user typed. Operation history remembers what the application changed.

## 2026-08-24 — Operation History Persists, Undoability Does Not

**Decision:** `/history` persists across application restarts, but undo/restore guarantees are limited to the current application session.

Previous-session entries remain visible as informational history only.

**Reason:** Persistent history preserves accountability and context, while session-scoped undo avoids unsafe reversals after external filesystem changes.

**Principle:** Persist the record of what happened; do not persist the promise that it can still be undone.

## Proposed — History Retention Is Automatic

Current direction: users should not have to manually manage or routinely clear operation history.

The application should automatically prune old history using a reasonable default policy.

Optional advanced settings may expose retention by age, entry count, or both.

## Expected V1 — Retain the Most Recent 50 Operations

**Expectation:** Version one keeps a rolling history of the most recent 50 app-owned filesystem operations.

One user action equals one history entry even when that action affects many files.

The oldest entry rolls off automatically when the limit is exceeded.

No retention configuration is required in version one.

A Clear History action may be available in Settings, but users should not need to manage the journal as routine maintenance.

**Note:** 50 is a product default that may be tuned after real-world use; the rolling bounded-history model is the more important decision.

## 2026-08-24 — Undo Never Silently Overwrites

**Decision:** If undo encounters a filesystem collision, ask the user how to resolve it.

Supported resolution choices should include Replace, Keep Both, Skip, and Cancel Undo, with Apply to All for bulk conflicts where appropriate.

Replace is never the default-selected action.

Partial undo outcomes must be represented accurately in operation history.

**Principle:** Undo should not create new data loss while attempting to reverse an earlier action.

## 2026-08-24 — Normal Delete Respects Windows Recycle Bin Behavior

**Decision:** Normal app-owned deletion follows the user's Windows Recycle Bin behavior/settings where the filesystem and Windows support it.

The application does not create a separate proprietary trash system or hidden backup solely for deletion undo.

If recoverable deletion is unavailable, the UI/history must not falsely present that deletion as safely undoable.

## 2026-08-24 — Files Workspace Supports Virtual Locations

**Decision:** The Files workspace may contain first-class virtual locations in addition to physical filesystem directories.

Virtual locations must not pretend to have ordinary filesystem paths when they do not.

## 2026-08-24 — Recycle Bin Is a Virtual Workspace Location

**Decision:** Recycle Bin is exposed through a readable Files workspace view rather than through the raw Windows `$Recycle.Bin` storage hierarchy.

Users may browse recycled items, use `@selection`, and perform appropriate app-owned actions such as Restore.

Underlying behavior should respect Windows-native Recycle Bin semantics.

**Principle:** Present Windows concepts in the form users understand; do not expose implementation details merely because they exist on disk.

## 2026-08-24 — Version-One Undo Scope Is Intentionally Narrow

**Decision:** Undo focuses on simple app-owned operations that can be reversed predictably, primarily move and rename plus Windows-native delete/restore cases where recoverability is reliable.

History and undo do not have identical scopes.

## 2026-08-24 — `/unzip` Is Not Undoable

**Decision:** Archive extraction may be recorded in `/history`, but `/undo` does not attempt to remove/reverse extracted contents.

**Reason:** The original archive remains intact, while transactional extraction rollback adds unnecessary version-one complexity.

## 2026-08-24 — `/tidy` Is Not Undoable

**Decision:** `/tidy`, if integrated into this workspace, is recorded as an operation but does not participate in `/undo`.

The workspace form is expected to target a folder hierarchy/reference. The existing Tidy application remains a separate implementation and may need to be rebuilt for integration.

For complex organizational transformations, preview/confirmation is preferred over transactional undo.

## 2026-08-24 — Copy Is Not Guaranteed Undoable in Version One

**Decision:** Copy is outside the guaranteed version-one undo set because deleting the destination later may be unsafe if the copied item has changed.

**Principle:** Undo is for simple direct reversals. Preview is the preferred safety mechanism for complex transformative operations.

## 2026-08-24 — Files Command Routing Is Deterministic

**Decision:** Input beginning with `/` routes to application-owned command handlers. Other input is treated as shell input after supported workspace-reference resolution.

Known interactive tools may route into hosted terminal tabs.

## 2026-08-24 — Application Commands Are Not PowerShell Translations

**Decision:** Slash commands such as `/move`, `/undo`, or `/history` are implemented as structured application behavior rather than rewritten into shell commands.

## 2026-08-24 — Do Not Hand-Roll PowerShell Grammar

**Decision:** The application does not attempt to parse/reimplement full PowerShell syntax.

If deeper shell-aware parsing becomes necessary, use a mature integration/parser approach rather than a custom partial grammar.

## 2026-08-24 — Resolve Only Known Workspace `@` References in Shell Input

**Decision:** In ordinary shell input, recognized workspace references may be resolved. Unknown `@something` tokens generally pass through untouched to preserve legitimate shell syntax.

Slash-command arguments may use stricter application-owned validation.

## 2026-08-24 — Interactive Routing Must Not Depend on AI

**Decision:** AI is not the authority for deciding execution target.

Known interactive tools use deterministic registry/routing rules. Heuristics may later provide advisory or fallback behavior but should not silently override deterministic routing.

## 2026-08-24 — Multi-Item References Expand as Multiple Arguments

**Decision:** A known workspace reference resolving to multiple items expands into multiple safely quoted shell arguments. The application does not infer whether the destination command semantically supports them.

**Principles:**

> Enhance the shell; do not reimplement it.

> Routing should be deterministic before it is clever.

> Application commands are owned by the application. Shell commands remain shell commands.

## 2026-08-24 — Command Bar Remains One Line

**Decision:** The Files command bar does not expand into a persistent terminal pane. Larger output uses the main workspace surface.

**Principle:** The command bar reports. The workspace explains.

## 2026-08-24 — Most Recent Command Result Remains Inspectable

**Decision:** The compact result indicator and View action for the most recently executed finite command remain available until the next command is actually executed. Typing or editing the next command does not remove the previous result.

This prevents useful output from disappearing immediately while preserving the one-line command-bar model.

## 2026-08-24 — View Commands Use Closeable Workspace Views

**Decision:** Commands whose purpose is to display information, such as `/history`, `/where`, or `/disk`, may open a closeable interactive view over/in place of the file-hierarchy workspace.

Closing the view restores the previous Files view/state.

## 2026-08-24 — `/history` Is Not a Shell Transcript

**Decision:** `/history` remains an app-owned operation journal. App-owned history entries may expose useful structured details/results, but arbitrary shell stdout/stderr is not persisted there.

## 2026-08-24 — No Multi-Result Shell Output Buffer in Version One

**Decision:** Version one retains only the most recent finite-command output for immediate inspection.

That output remains available until another command is executed. It has no timer-based expiration, is not preserved merely as a recent-output stack, and is not expected to survive application restart.

Up/Down command recall can reproduce old command text but does not preserve old command output.

## 2026-08-24 — Rich Commands Are Files Workspace Views

**Decision:** Rich informational commands use temporary views inside the Files workspace rather than modal overlays or automatically-created tabs.

The underlying Files location/state is preserved and simple Back navigation returns to it.

Version one supports zero or one temporary rich view at a time rather than a deep nested view stack.

## 2026-08-24 — Workspace Views Answer in English

**Decision:** Command syntax remains symbolic while resulting workspace views use readable English labels.

Examples:

```text
/history       → Files · History
/where python  → Files · Where — python
/disk          → Files · Disk
```

**Principle:** Commands use symbols. The interface answers in English.

## 2026-08-24 — `@selection` Always Means Filesystem Selection

**Decision:** Rich workspace views do not redefine `@selection`.

`@selection` always refers to the selected filesystem item(s) in the preserved underlying Files context.

**Principle:** Rich views contain controls and results. Files contains filesystem selection.

## 2026-08-24 — History Rows Are Not Selectable Filesystem Entities

**Decision:** `Files · History` entries are acted on through explicit controls such as Details, Undo, or Restore rather than becoming selectable `@selection` targets.

## 2026-08-24 — Rich Results Navigate Into Files to Establish Selection

**Decision:** Results in views such as `Files · Where` or `Files · Disk` may expose actions such as Go to or Open.

If a result needs to become a filesystem selection, the action navigates/reveals it in the real Files hierarchy and establishes selection there.

A clickable rich-view result is not automatically a Files selection.

## 2026-08-24 — Command Bar Keeps Underlying Files Context in Rich Views

**Decision:** The Files command bar remains usable while a temporary rich view is open.

Filesystem references such as `@thisfolder` and `@selection` continue to resolve against the preserved underlying Files state.

## 2026-08-24 — No Peek Files Requirement in Version One

**Decision:** Version one does not require a permanent split pane or dedicated Peek Files mechanism solely because a rich view temporarily hides the hierarchy.

Back navigation restores the preserved Files view immediately. A peek mechanism can be reconsidered only if real usage demonstrates a need.

## 2026-08-24 — Strong Keyboard Support Is a Product Requirement

**Decision:** Core workspace and rich-view workflows must be practical with either keyboard or mouse.

The hybrid Files/terminal design must not make rich informational views effectively mouse-only.

## 2026-08-24 — UI Focus Does Not Redefine `@selection`

**Decision:** Filesystem selection, UI focus, and command-bar focus are distinct concepts.

`@selection` continues to mean actual selected filesystem items only.

## 2026-08-24 — Rich-View Keyboard Focus Targets Actions

**Decision:** Rich-view keyboard navigation should focus explicit actionable controls rather than visually selecting entire result/history rows.

Expected baseline behavior:

```text
↑ / ↓   move among primary actions/results
Tab     move among controls
Enter   activate focused control
Esc     return to Files
```

This keeps keyboard focus visually distinct from filesystem selection.

## 2026-08-24 — Prefer Conventional Navigation Keys in Version One

**Decision:** Version one favors familiar Arrow/Tab/Enter/Esc navigation rather than adding a separate Vim/TUI-style navigation language.

The exact shortcut for jumping directly to command-bar focus remains open.

## 2026-08-24 — Space Focuses the Command Bar From Neutral Workspace Surfaces

**Decision:** Pressing Space while focus is on a neutral Files or rich-view surface moves focus to the bottom command bar.

Space retains its normal behavior inside editable fields, the command bar itself, and focused controls that legitimately consume Space such as buttons or checkboxes.

**Principle:** From any neutral workspace surface, press Space and type.

## 2026-08-24 — `/run` Is the App-Owned Execute Command

**Decision:** Use `/run` rather than `/execute` as the simple workspace command for launching a target.

Examples:

```text
/run tool.exe
/run @selection
/run @projects\tool.exe
```

Native PowerShell execution syntax remains available for power users.

## 2026-08-24 — Relative App-Command Targets Resolve From Current Files Location

**Decision:** A relative target in an app-owned command resolves against the current underlying Files location.

Therefore `/run tool.exe` means run `tool.exe` from the current Files folder without requiring `/run @thisfolder\tool.exe`.

The explicit `@thisfolder` form remains supported.

## 2026-08-24 — References Can Compose With Child Paths

**Decision:** Workspace references may be used as path anchors inside app-command arguments, such as:

```text
/run @projects\build.exe
/run @thisfolder\tools\helper.exe
```

## 2026-08-24 — `/run` Does Not Search the Whole Computer Implicitly

**Decision:** If `/run tool.exe` cannot resolve `tool.exe` relative to the current Files location, it fails clearly rather than performing an implicit system-wide search.

The UI may suggest `/where tool.exe` as the next action.

**Principle:** `/run` is explicit about the action while allowing the target to stay simple.

## 2026-08-24 — Raw Paths Preserve PowerShell Semantics

**Decision:** The application does not assign new Files-specific behavior to raw shell/path syntax.

The language boundary is:

```text
/   = app-owned action
@   = workspace reference
raw shell/path syntax = PowerShell
```

Navigation and execution using normal PowerShell forms remain uninterrupted, including `cd`, relative paths, `.\`, and `&`.

Users who want simpler workspace behavior use app actions and references such as `/run tool.exe` or `@projects`.

**Principle:** References and actions simplify the workspace; raw paths remain the shell's language.

## 2026-08-24 — Architect for Pluggable Shell Backends

**Decision:** The Files command bar communicates with the underlying shell through a shell adapter/backend boundary.

The app-owned `/` action and `@` reference layers remain independent of the selected shell backend.

## 2026-08-24 — PowerShell Is the Guaranteed Version-One Shell

**Decision:** Version one ships and is tested with PowerShell as the command-bar shell.

Other shells are not a version-one support requirement even though the architecture leaves room for them.

## 2026-08-24 — Shell Switching Must Be Explicit

**Decision:** The application should not automatically change shells based on folder or project context.

If additional shell backends are added later, shell selection should be explicit and predictable.

**Principle:** Design the command bar around a shell boundary; do not make the entire workspace a PowerShell implementation.

## 2026-08-24 — Each Files Tab Owns Its Navigation History

**Decision:** Back/Forward filesystem history is independent per Files tab.

## 2026-08-24 — Back Dismisses a Rich View Before Navigating Files

**Decision:** If a rich view is open, Back closes/dismisses it and restores the underlying Files state. Only when no rich view is open does Back move to the previous filesystem location.

Esc may also dismiss the active rich view.

## 2026-08-24 — Rich Views Are Not Forward-History Entries

**Decision:** Forward operates only on filesystem navigation history and never restores a rich view that was dismissed with Back/Esc.

Rich views such as History, Where, Disk, and command-output views are reopened by their commands, not by navigation history.

## 2026-08-24 — Up Is Filesystem-Only

**Decision:** Up navigates to the parent directory of the current underlying Files location. It does not participate in rich-view behavior.

**Principle:** Rich views are invoked, not visited.

## 2026-08-24 — GUI Open Behavior Remains Windows-Familiar

**Decision:** Single click selects. Double-click/Enter invokes the Windows-defined/default Open behavior for the item. Folders navigate normally.

The app does not redefine every file type's GUI Open semantics.

**Boundary:** GUI Open respects Windows; `/run` expresses explicit execution intent.

## 2026-08-24 — Use a Minimal App-Owned Context Menu

**Decision:** The primary right-click menu is intentionally compact rather than reproducing Windows Explorer's large context menu.

Initial direction:

```text
Open
Rename
Copy
Cut
Copy Path
Delete
Properties
```

Visual separators/grouping may be used without adding menu depth.

## 2026-08-24 — Do Not Rebuild the Command Bar as Menus

**Decision:** Do not add every command-bar capability to right-click menus or grow broad file-type-specific context menus by default.

Common actions use direct GUI/keyboard interactions. The command bar carries the long tail.

Submenus should be avoided unless future usage demonstrates a clear need.

**Principle:** Do not bury capability in menus. Give common actions direct interactions and let the command bar carry the long tail.

## 2026-08-24 — Keep `@thisfolder` as the Canonical Current-Location Reference

**Decision:** Do not replace `@thisfolder` with shorter alternatives such as `@here`, `@cwd`, or `@folder`.

**Reason:** `@thisfolder` is explicit and reads clearly in source/destination commands such as `/unzip archive.zip @thisfolder`.

## 2026-08-24 — Autocomplete Is the Speed Layer for Long Readable Tokens

**Decision:** Longer readable commands/references should be made fast through autocomplete rather than abbreviated into less obvious vocabulary.

Typing partial references such as `@t` should surface `@thisfolder`; typing `@` should expose the small known-reference set.

**Principle:** Readable when seen. Fast when typed.

## 2026-08-24 — Keep Completion Limited to `/` and `@`

**Decision:** App autocomplete/discovery is limited to `/` commands and recognized `@` references. Tab completes those app-owned tokens; ordinary shell input retains shell-native completion behavior.

**Decision:** Version one does not add custom Tab cycling through files in the current Files folder.

**Decision:** For app suggestions, Tab completes, Arrow keys browse, Esc dismisses, and Enter executes/submits.

**Decision:** Known workspace `@` references are resolved by the app, but unknown/non-reference PowerShell `@` syntax remains available to the shell.

> We autocomplete what we invented. The shell completes what it owns.

## 2026-08-25 — Known Command-Bar References Win Over PowerShell Splatting

**Decision:** In the Files command bar, a token that matches a known workspace reference (`@thisfolder`, `@selection`, or a user-defined Location name) is always resolved as that reference, even when the same token would also be valid PowerShell splatting (for example `@selection` read as splatting a `$selection` variable). Only tokens that match no known reference pass through untouched to the shell.

A user who needs PowerShell splatting for a variable whose name collides with a known reference uses an independent terminal tab, where the Files command language (`/` and `@`) does not apply (CLAUDE.md invariants; the Files command-bar language is not applied inside independent terminal tabs).

**Reason:** The overlap between Filekin's readable references and PowerShell splatting is rare in the command bar, and resolving known references predictably is more valuable there than preserving an uncommon splatting form. Terminal tabs remain a full, unmodified PowerShell surface for power users.

**Principle:** In the command bar, the names we invented win; the terminal tab stays pure shell.

## 2026-08-24 — `@selection` Always Means the Full Selection

**Decision:** `@selection` resolves to every currently selected filesystem item and never silently collapses to the first item.

## 2026-08-24 — Commands Declare Cardinality and Type Requirements

**Decision:** Each app-owned command determines whether it accepts zero, one, or multiple targets and what target types are valid.

Examples:

```text
/run @selection      multi-target friendly
/info @selection     multi-target friendly
/where python        single-query behavior
/history             no selection required
/unzip @selection    type-restricted
```

Invalid target counts/types produce clear validation rather than changing reference meaning.

## 2026-08-24 — `/run` May Launch Multiple Selected Items

**Decision:** `/run @selection` can launch multiple selected targets. Large batches may require confirmation.

## 2026-08-24 — `/where` Is Not a Generic Multi-Selection Command

**Decision:** `/where` primarily accepts one search query/tool/app name and does not reinterpret a multi-item `@selection` as multiple searches by default.

**Principle:** References describe what is selected. Commands decide whether that input is valid.

## 2026-08-24 — `/copy`, `/move`, `/rename`, and `/delete` Are Core File Commands

**Decision:** Version one includes these app-owned filesystem commands:

```text
/copy
/move
/rename
/delete
```

They exist so keyboard-driven users can manipulate files/folders directly from the command bar.

## 2026-08-24 — `/copy` Requires a Destination

**Decision:** `/copy` means immediate source-to-destination filesystem copy, not clipboard copy.

Clipboard behavior remains `Ctrl+C`.

## 2026-08-24 — `/move` Supports Selection-to-Destination Workflows

**Decision:** `/move @selection @destination` is a first-class command-bar workflow and may operate on multiple selected filesystem items.

## 2026-08-24 — `/rename` Remains Available but Simple

**Decision:** `/rename` is part of the command vocabulary even though F2 remains faster for common single-item rename.

Advanced bulk rename language is not required for v1.

## 2026-08-24 — `/delete` Uses App-Owned Windows-Native Delete Behavior

**Decision:** `/delete @selection` follows the previously established Windows Recycle Bin behavior/settings where supported and is not a permanent-delete shortcut.

## 2026-08-24 — No `/paste` Command Requirement

**Decision:** Do not add `/paste` solely to duplicate Ctrl+V. Immediate command-line file transfer uses `/copy` or `/move` with explicit source and destination.

**Principle:** The command bar should be able to operate the filesystem, not just launch utilities.

## 2026-08-24 — Keep Both `/where` and `/find`

**Decision:** `/where` and `/find` are separate version-one commands.

`/where` discovers the related program/tool locations associated with a named application or executable.

`/find` searches for matching filesystem items within the current Files location or an explicitly supplied scope.

Examples:

```text
/where python
/find config.json
/find config.json @projects
```

Both may render as temporary rich Files views.

**Principle:** `/where` discovers a program's footprint. `/find` searches a filesystem scope.

## 2026-08-24 — `/info` Is a Confirmed Version-One Rich Command

**Decision:** `/info` provides focused filesystem inspection for single items, folders, and multi-selections.

## 2026-08-24 — `/info` Prioritizes Universally Useful Metadata

**Decision:** Prioritize name, type/extension, full path, size, created time, and modified time. Add type-specific metadata only when meaningful.

## 2026-08-24 — Folder and Selection Size Are Core `/info` Use Cases

**Decision:** `/info @thisfolder` calculates aggregate folder size/counts. `/info @selection` calculates aggregate size/counts for the complete selection.

Large recursive calculations must not block the Files workspace and may display progress/calculating state.

## 2026-08-24 — Expensive Metadata Is On Demand

**Decision:** Expensive information such as checksums should be calculated only when requested.

## 2026-08-24 — Keep Native Windows Properties Available

**Decision:** `/info` does not recreate every Windows property page. A Windows Properties action may open the native system dialog for advanced functionality.

**Principle:** `/info` answers what is useful to know about this filesystem target right now.

## 2026-08-24 — `/places` Is Confirmed for Version One

**Decision:** `/places` opens a rich view of standard Windows/user folders such as Home, Desktop, Documents, Downloads, Pictures, Music, and Videos when available.

It provides system-standard destinations without cluttering the personalized Locations sidebar.

## 2026-08-24 — `/drives` Is Confirmed for Version One

**Decision:** `/drives` opens a rich view of available filesystem drives/volumes with concise identifying/storage information and direct navigation.

It is a navigation/discovery surface rather than a full disk-management interface.

## 2026-08-24 — Locations Sidebar and System Views Serve Different Roles

**Decision:**

```text
Locations sidebar = user/project-specific persistent locations
/places           = standard Windows/user locations
/drives           = machine drives/volumes
```

**Principle:** Keep personal locations persistent; summon system locations when needed.

## 2026-08-24 — `/recent` Is Out of Version One

**Decision:** `/recent` is not included in the v1 slash-command vocabulary.

The combination of tabs, Back/Forward, Locations, `/places`, `/drives`, `/find`, and direct filesystem navigation is expected to make destinations easy to reach without another Recent surface.

A future Recent/workspace-resumption feature may be reconsidered from actual usage rather than designed speculatively.

## 2026-08-24 — `/disk` Is Out of Version One

**Decision:** Remove `/disk` from the v1 slash-command vocabulary.

Do not replace it with `/space`, `/storage`, or `/usage` merely to preserve the feature.

`/drives` owns drive discovery and concise capacity/free-space information. `/info` owns size inspection for files, folders, and selections.

Whole-drive storage-consumption analysis may be reconsidered later from demonstrated need.

## 2026-08-24 — `/interactive` Is Out of Version One

**Decision:** Remove `/interactive` from the v1 user-facing command vocabulary.

Interactive-tool detection, routing, lifecycle management, and registry behavior remain part of the terminal architecture.

A future advanced override/registration mechanism may exist if real usage demonstrates a need, but users should not need a slash command for core interactive-process behavior.

**Principle:** Interactive-process support is infrastructure, not user-facing command vocabulary.

## 2026-08-24 — `/tidy` Is Confirmed for Version One

**Decision:** `/tidy` ships in v1 as an app-owned loose-file organization command.

Examples:

```text
/tidy @desktop
/tidy @downloads
/tidy @thisfolder
/tidy D:\MessyFolder
```

## 2026-08-24 — Desktop Is an Ordinary Tidy Target

**Decision:** Remove the original standalone utility's Desktop icon-positioning/resorting behavior from the Files implementation.

Desktop is simply another filesystem location that can be targeted through `/tidy`, including via the standard-location model exposed by `/places`.

## 2026-08-24 — Tidy Is Conservative and Non-Recursive by Default

**Decision:** Tidy organizes loose files directly within the supplied folder into deterministic file-type categories. It leaves existing subfolders alone, does not recursively redesign the hierarchy, leaves unknown types in place, and never silently overwrites conflicts.

## 2026-08-24 — Mandatory Tidy Confirmation Is Not Yet Decided

**Decision:** Do not currently require or forbid a pre-execution Tidy confirmation/preview. Resolve this separately as part of the command safety model.

## 2026-08-24 — Tidy Does Not Require Undo in Version One

**Decision:** `/tidy` remains outside the required v1 `/undo` operation set.

**Principle:** `/tidy` organizes loose files in a specified folder; it does not redesign an existing hierarchy.

## 2026-08-24 — `/tidy` Does Not Require Confirmation in Version One

**Decision:** Normal `/tidy <folder>` execution begins immediately when the user executes the command.

Do not show a mandatory preview or "Are you sure?" step.

Tidy remains safe through conservative deterministic behavior: it handles loose known file types, leaves existing folder structure and unknown files alone, never silently overwrites conflicts, and reports skipped items afterward.

The compact command result remains available with a `View` action for the detailed rich result.

**Product intent:** Tidy should feel unusually fast: one explicit command turns a messy folder into an organized one.

## 2026-08-24 — Batch Operations Prefer Partial Success

**Decision:** App-owned batch operations process independent valid targets even when other targets fail or require attention.

Do not make an entire batch fail merely because one unrelated target has a conflict.

## 2026-08-24 — Conflicts Are Isolated for Attention

**Decision:** Unresolved targets appear in the active rich result/conflict view while successful targets remain completed.

## 2026-08-24 — Esc/Back Skips Remaining Conflicts

**Decision:** Leaving an active conflict view with Back/Esc skips unresolved targets and closes the view. It does not roll back completed work.

Avoid calling this action simply `Cancel` once partial work has completed.

## 2026-08-24 — Completed Partial Results Remain Inspectable

**Decision:** After unresolved targets are skipped, the command result may remain as:

```text
⚠ Moved 9 of 12 · 3 skipped    View
```

`View` opens the completed result for inspection.

**Principle:** Batch operations make progress wherever they safely can. Problems are isolated for attention rather than blocking unrelated work.

## 2026-08-24 — Copy/Move Collisions Offer Replace, Keep Both, or Skip

**Decision:** Destination-name conflicts during explicit `/copy` and `/move` operations expose:

```text
Replace
Keep Both
Skip
```

`Keep Both` automatically generates a safe unique filename rather than requiring manual rename input.

## 2026-08-24 — Batch Collision Choices May Apply to Remaining Compatible Conflicts

**Decision:** The rich conflict view may provide an `Apply choice to remaining conflicts` control. It applies only to compatible destination-name collisions, not unrelated error types.

Avoid six separate action/all-action buttons.

## 2026-08-24 — Tidy Skips Collisions Without Interrupting

**Decision:** `/tidy` does not stop for destination-name conflicts. It skips the conflicting target and reports it afterward.

## 2026-08-24 — Replacement Should Respect Recoverability Where Practical

**Decision:** Because Replace destroys the existing destination target, use Windows-native recoverability where technically supported rather than silently making replacement more permanent than necessary.

**Principle:** Explicit transfer conflicts ask what the user wants. Automatic organization skips what it cannot place safely.

## 2026-08-24 — Standard Privileges Are the Default

**Decision:** The Files application and default PowerShell command-bar backend run unelevated.

## 2026-08-24 — App-Owned Commands Request Elevation Per Need

**Decision:** When an app-owned operation requires administrator permission, isolate that target and offer `Retry as administrator` / `Skip` while allowing unrelated targets to complete.

Elevation uses normal Windows UAC.

## 2026-08-24 — Advanced Elevated PowerShell Mode Is Allowed

**Decision:** Advanced settings may allow power users to choose an Elevated PowerShell backend/session.

Standard remains the default, elevation invokes Windows UAC, and the UI persistently indicates the elevated state.

## 2026-08-24 — Elevated Shell Does Not Rewrite Slash-Command Safety

**Decision:** App-owned `/` commands retain their app-owned safety, deletion, conflict, and recovery semantics even when an elevated PowerShell session exists.

Raw PowerShell entered into an explicitly elevated shell retains normal elevated PowerShell behavior.

**Principle:** Safe app commands stay safe. Raw shell power stays raw shell power.

## 2026-08-24 — Locked Files Are Retry or Skip

**Decision:** App-owned operations never force-unlock a file or kill another process merely to complete the operation.

Locked/in-use targets become `Retry` / `Skip` attention items while unrelated batch work continues.

## 2026-08-24 — Read-Only Is Respected, Not Treated as a Universal Error

**Decision:** Read-only files can still be opened, read, copied, inspected, found, and normally moved where Windows permits.

If an operation must modify/replace/delete a read-only target, surface `Continue` / `Skip`. Continue authorizes handling the attribute only as necessary for the requested operation.

Do not unnecessarily remove the resulting file's read-only state.

## 2026-08-24 — Network Authentication Remains Windows-Owned

**Decision:** Network-share availability/authentication/access failures surface as ordinary attention states. Files does not implement a separate credential manager.

## 2026-08-24 — Protected Locations Use Existing Elevation Flow

**Decision:** Protected targets use `Retry as administrator` / `Skip` and normal Windows UAC.

## 2026-08-24 — ACL Editing Is Outside Version One

**Decision:** Ownership, ACL, inheritance, and advanced permission editing remain in Windows Properties or raw PowerShell.

**Principle:** Files handles ordinary permission problems clearly; Windows remains the authority for security, credentials, and access control.

## 2026-08-24 — Long Filesystem Work May Delegate to Task Tabs

**Decision:** The app may automatically move substantial `/copy`, `/move`, `/unzip`, `/tidy`, and exceptionally large delete work into dedicated task tabs.

Users do not need a background flag or routine delegation prompt.

## 2026-08-24 — Delegation Is Intelligent, Not Duration-Only

**Decision:** Operation type, size, item count, recursive scope, elapsed time, and interaction needs may inform delegation. Exact thresholds are implementation details.

## 2026-08-24 — Inspection Commands Stay in Their Rich Views

**Decision:** `/info`, `/find`, `/where`, `/places`, and `/drives` do not become task tabs merely because work takes time. Their rich views can update progressively.

## 2026-08-24 — Run Work Uses Terminal/Process Tabs

**Decision:** Long-running/interactive `/run` work follows terminal-process routing rather than filesystem task tabs.

## 2026-08-24 — Completed Task Tabs Remain Inspectable

**Decision:** A task tab transitions to a completed result and remains until the user closes it rather than disappearing automatically.

## 2026-08-24 — Task Cancellation Does Not Imply Rollback

**Decision:** Cancel stops remaining work while completed independent work remains completed unless a specific operation explicitly provides transactional rollback.

**Principle:** Work goes to the surface best suited to its lifetime.

## 2026-08-24 — WPF Selected as the Version-One Desktop Framework

**Decision:** Build the Windows desktop application using C#, modern .NET, and WPF.

WPF is selected for maturity, Windows integration, documentation/ecosystem depth, and suitability for the product's unusual combination of file management and terminal/process behavior.

## 2026-08-24 — Stock WPF Styling Is Not the Product Design

**Decision:** Do not ship the application using default/stock WPF styling as its visual identity.

WPF is an implementation framework. The UI must follow the modern terminal/developer-tool visual direction defined by the product.

## 2026-08-25 — Expanded Command Shell Has No Output Controls

**Decision:** The expandable command-shell region shows only the command, its output text, and the `Collapse` action (with Esc also collapsing). It must not carry a shell-selector dropdown, a copy-output icon, a delete/clear-output icon, a pop-out control, or any similar chrome.

**Reason:** A UI mockup and an earlier UI/UX design sheet drew those controls on the expanded output, but `ENGINEERING-GUARDRAILS.md` (§"No Speculative UI Chrome") and `UX-DESIGN.md` (§"UI Control Discipline") both forbid exactly those command-bar/output controls. The normative guardrails win over the exploratory mockup.

**Principle:** Empty space is preferable to unexplained controls. A control needs an approved Filekin behavior to exist.

## 2026-08-25 — File Rows Use Terminal Type Codes, Not Explorer Chrome

**Decision:** The Files hierarchy renders rows in Filekin's visualized-terminal language: compact textual type codes (`DIR`, `MD`, `PY`, `ZIP`, `IMG`, …) with directories marked by a trailing `/`, and traditional large file-type icons minimized. It does not use Windows Explorer-style per-file icons paired with verbose type names such as "Visual Studio Solution".

**Reason:** `UX-DESIGN.md` (§"Design Direction", §"Main Filesystem View", §"File Representation") states the filesystem should feel like a visualized terminal rather than Explorer. A mockup leaned Explorer; the confirmed direction is the terminal type-code language.

**Principle:** The filesystem is a visualized terminal, not an Explorer clone.

## 2026-08-25 — Files Toolbar Has No View Toggle or Overflow Menu in V1

**Decision:** The Files content toolbar does not include a grid/list view toggle or a `...` overflow menu in version one. The list is the single Files presentation. Lightweight status such as the item count and free-space indicator may remain because it aids comprehension.

**Reason:** A grid view reintroduces the large icons the design minimizes, and an undefined `...` menu is speculative chrome. `ENGINEERING-GUARDRAILS.md` (§"No Speculative UI Chrome") requires each control to map to an approved behavior. Either control can return later only with a defined purpose and an explicit UX decision.

**Principle:** Add controls for approved behavior, not because file managers usually have them.

## 2026-08-25 — Terminal Tab Names Use `Tool · Location`

**Decision:** Hosted terminal tab titles use the `TOOL · launch-context` form (for example `Claude · filekin`, `Codex · MyApp`), not a generic `Terminal: Tool` / `PowerShell` form. This confirms the mockup's `Terminal: …` labels against the established naming.

**Reason:** `UX-DESIGN.md` (§"Session Identity") explicitly prefers `CODEX · MyApp` over a row of generic `PowerShell` labels, and existing decisions ("Terminal Sessions Are Filesystem Context", "Terminal Tab Names Describe Launch Context") already fix this form. The tab name describes launch context and does not continuously track the hosted process's internal working-directory changes.

**Principle:** A terminal tab says what it is and where it started, not merely that it is a terminal.

## 2026-08-25 — No Active-Sessions Group in the Sidebar

**Decision:** The Files sidebar does not include a persistent "ACTIVE sessions" group in version one. Every active terminal session is represented solely by its tab in the tab strip. The sidebar holds only user `@` Locations and the built-in `/places` / `/drives` surfaces.

**Reason:** The tab strip already lists every active session, so a second always-present list in the sidebar would duplicate it and consume the deliberately sparse sidebar. This narrows the earlier "compact ACTIVE sessions area" idea in `UX-DESIGN.md` (§"Sparse Navigation Sidebar"). The separate, still-open idea of surfacing session indicators beside directory rows (§"Filesystem Session Indicators") is unaffected.

**Principle:** Do not show the same live sessions in two permanent places.

## 2026-08-25 — Visual Identity: Blue Accent, Dark Default Theme

**Decision:** The default accent color is a developer-friendly blue (approximately `#4F9CE8` in dark mode and `#1F6FB8` in light mode, tuned per theme so contrast holds on both grounds). The application's default theme is **dark**. Light and follow-the-system options remain available through the appearance preferences that `ARCHITECTURE.md` already anticipates. The accent is intended to become a user-selectable setting in a later version; blue is the shipped default.

The accent is used as a restrained spark — focus, selection, the command-bar `›`, running-session dots, active sidebar Location, and folder rows in the Files hierarchy. The rest of the listing stays neutral. Semantic status colors (green success, amber warning, red failure) are a separate set and are never replaced by the accent.

**Reason:** In the rendered Files preview, blue read as the most balanced choice across both light and dark modes and fits the terminal/developer-tool character. Dark-first suits the product's audience and aesthetic. (An earlier orange direction was explored and set aside in favor of blue.)

**Principle:** A calm neutral ground carries the tool; the accent is a small, consistent spark, not the surface.

## 2026-08-25 — Files Hierarchy Sorts by Clicking Column Headers

**Decision:** The Files hierarchy is sortable by clicking a column header (Name, Type, Modified, Size). The clicked column becomes the sort key; clicking it again reverses direction, and a small caret on the active column shows the direction. Headers keep the terminal visual language (monospace, quiet, restrained) rather than an Explorer-style heavy column bar, and are keyboard-accessible (focusable; Enter/Space sorts) per the strong-keyboard requirement. Directories group before files by default. Do not add an Explorer-style column-chooser context menu or a separate "Sort by" dropdown in version one; clickable headers are the sole sort control.

**Reason:** Sorting is already an approved Files behavior (`UX-DESIGN.md` §"Main Filesystem View" lists items as Sortable). Clicking a header is the discoverable, standard mechanism. Sorting is interaction behavior and is independent of the terminal visual language, so the two do not conflict.

**Principle:** Terminal look, familiar behavior. The aesthetic governs how rows are drawn, not whether the list can be sorted.

## 2026-08-25 — Files Listing Hides Only Protected OS Items (Hidden+System)

**Decision:** The Files hierarchy omits only protected operating-system items — entries carrying both the `Hidden` and `System` attributes ("super-hidden"). At the user-profile root these are the legacy per-user compatibility junctions (Application Data, Cookies, Local Settings, My Documents, NetHood, PrintHood, Recent, SendTo, Start Menu, Templates): reparse points that deny traversal and cannot be opened. Everything Explorer's "show hidden items" view would show is listed, including plain-`Hidden` folders such as `AppData` and dot-prefixed names (`.ssh`, `.config`). There is no separate show-hidden toggle in version one; plain-hidden items are always shown.

**Reason:** Those junctions appeared in the listing but could not be opened and are absent from both Explorer and the terminal, which confused navigation. `Hidden+System` is exactly the attribute combination Windows uses to keep them out of normal listings (Explorer's "hide protected operating system files"), and it cleanly separates the useless junctions from useful hidden folders like `AppData` that the owner wants visible.

**Principle:** Show what the user can actually use; hide only the OS's own compatibility clutter.

Custom styling/control templates and appropriate modern resources are expected where needed.

**Principle:** WPF is the machinery underneath the interface, not the visual identity.

## 2026-08-24 — UI Thread Must Remain Responsive

**Decision:** Filesystem scans, recursive calculations, file operations, hashing, archive work, and shell/process I/O must not block the WPF UI thread.

Large item collections must use virtualization.

## 2026-08-24 — Keep Core Logic Separated From WPF Where Practical

**Decision:** Command parsing, reference resolution, Tidy logic, operation models, task/history models, and similar core behavior should not be unnecessarily coupled to WPF controls.

This is maintainability architecture, not a promise of cross-platform v1 support.

## 2026-08-24 — Use a Hybrid .NET + Windows API Filesystem Architecture

**Decision:** Use standard modern .NET filesystem APIs for ordinary file work and selective Windows-native/Shell APIs for Windows-owned behavior such as Recycle Bin, file associations, known folders, UAC, and native Properties.

## 2026-08-24 — Windows APIs Are Infrastructure, Not UI

**Decision:** Do not use Explorer/Shell UI as the product interface merely because Windows APIs are used underneath.

The WPF UI remains fully custom and follows the established terminal/developer-tool design.

## 2026-08-24 — Add Explicit Engineering Guardrails for Coding Agents

**Decision:** Implementation guidance must prohibit speculative features, generic AI-generated UI, unnecessary abstraction, dependency bloat, swallowed errors, fake-complete stubs, and preference-driven rewrites of stable code.

**Principles:**

> Reliable and simple beats clever.

> When the specification is clear, implement the specification. Do not invent the product while coding it.

## 2026-08-24 — Files Command Bar Working Directory Follows Files

**Decision:** The Files command bar uses the current visible Files location as its working directory and follows Files navigation.

It does not maintain a hidden independent working directory.

## 2026-08-24 — Independent PowerShell Work Uses Terminal Tabs

**Decision:** Users who want a separate PowerShell working directory/session launch `powershell`, which routes to a hosted terminal tab.

The terminal starts from the launch location and becomes independent thereafter.

Multiple terminal tabs may coexist with separate process state and working directories.

## 2026-08-24 — Files Command Bar Is Not a Mini Independent Terminal

**Decision:** The Files command bar is the command interface for the current Files workspace, with `/` app commands and PowerShell input sharing the visible Files context.

**Principle:** The Files command bar belongs to Files. Terminal tabs belong to themselves.

## 2026-08-24 — Use a Persistent PowerShell Runspace for the Files Command Bar

**Decision:** The Files command bar should use a persistent hosted PowerShell runspace for finite PowerShell execution rather than starting a new PowerShell process per command.

This preserves session state such as variables, aliases, modules, and PowerShell location.

## 2026-08-24 — Synchronize Files With Runspace Filesystem Location

**Decision:** Files navigation updates the command-bar runspace filesystem location, and filesystem `cd` / `Set-Location` changes update the visual Files location.

Do not rely on changing the entire application process working directory.

## 2026-08-24 — ConPTY Owns Real Terminal Sessions

**Decision:** Interactive shells/CLIs/TUIs use hosted terminal tabs backed by Windows ConPTY rather than being forced through the finite command-bar result path.

Terminal tabs become independent after launch.

## 2026-08-24 — Non-Filesystem PowerShell Providers Remain Unresolved

**Decision:** Do not make Files display non-filesystem PowerShell providers such as `HKLM:\` as filesystem locations.

The exact v1 behavior—reject in the Files command bar or delegate/offer a terminal tab—must be decided separately.

## 2026-08-24 — Require an Early PowerShell/ConPTY Technical Spike

**Decision:** Before broad implementation, build a small prototype validating persistent runspace state, bidirectional filesystem-location synchronization, native finite commands, interactive routing, and ConPTY handoff.

**Principle:** Use the PowerShell runspace for Files-aware shell state; use ConPTY for real terminal sessions.

## 2026-08-24 — Files and Command-Bar Location May Never Diverge

**Decision:** The visible Files hierarchy and the Files command-bar PowerShell location must always represent the same filesystem-backed location.

## 2026-08-24 — Non-Filesystem PowerShell Locations Delegate to a Terminal Tab

**Decision:** If the Files command bar attempts to enter a non-filesystem PowerShell provider such as `HKLM:\`, that context is opened/promoted into an independent terminal tab.

The Files command bar remains synchronized with its visible filesystem location.

Do not let the command bar remain in a non-filesystem provider while Files displays an unrelated folder.

**Principle:** If Files cannot represent the shell location, the shell location does not belong in the Files command bar.

## 2026-08-24 — Terminal Tabs Host PowerShell as the Root Shell

**Decision:** Version-one terminal tabs use ConPTY with PowerShell as the root shell. Interactive tools such as Codex, Claude, Python, and SSH are launched inside that shell rather than being the root terminal process.

## 2026-08-24 — Interactive Tool Exit Returns to PowerShell

**Decision:** When an interactive child tool exits, the terminal tab remains and returns naturally to its PowerShell prompt.

## 2026-08-24 — Root Shell Exit Closes the Terminal Tab

**Decision:** When the terminal tab's root PowerShell process exits, the terminal tab closes.

Do not invent a persistent dead-terminal/restart screen.

Unexpected root-shell termination may produce a brief non-blocking status/error indication.

## 2026-08-24 — Terminal Tabs Inherit Files Location Only at Launch

**Decision:** A new terminal starts in the current Files location, then becomes independent. Files navigation and terminal navigation do not synchronize after launch.

## 2026-08-24 — Tool-Launched Tabs Prefer Tool-Oriented Titles

**Decision:** When an interactive tool causes terminal creation, title the tab for the tool/context (for example `Claude · App`) rather than exposing only the PowerShell implementation detail.

## 2026-08-24 — Independent Terminal Input Uses Normal Shell Semantics

**Decision:** Files `/` commands and `@` references are command-bar concepts and are not automatically imposed on terminal-tab input.

**Principle:** Tools run inside the shell; when the tool exits, the shell remains. When the shell exits, the tab closes.

## 2026-08-25 — Use a Shared Workspace Surface Host

**Decision:** Files hierarchy, rich views, and task views share hosting/lifecycle infrastructure where useful, but remain explicit surface types.

## 2026-08-25 — Rich Views Must Remain Distinct From the File Hierarchy

**Decision:** Rich views reuse infrastructure, not filesystem visual grammar. They remain visibly command-driven inspection/result surfaces such as `Files · Info` and `Files · Find`.

## 2026-08-25 — Task Tabs Share the Rich-View Visual Family

**Decision:** Task tabs reuse the rich-view design language and primitives for typography, metadata, progress, attention states, actions, and spacing. Task tabs retain their independent persistent lifecycle.

**Principle:** Rich views and task tabs share a visual language, but not the same lifecycle.

## 2026-08-25 — Use Hybrid JSON + SQLite Persistent Storage

**Decision:** Use `settings.json` for human-readable preferences and saved Locations, and SQLite (`state.db`) for transactional operation/history/undo state.

## 2026-08-25 — Store App Data Under the Actual Product Name

**Decision:** Persistent data lives under `%AppData%\<AppName>\`, where `<AppName>` is the final product name.

Do not hard-code a generic `%AppData%\Files\` directory unless `Files` is ultimately chosen as the product name.

## 2026-08-25 — Advanced Users May Inspect/Edit/Back Up Settings

**Decision:** `settings.json` is intentionally understandable and editable by advanced users.

Use descriptive stable keys, validation, safe recovery, and no secret/token storage.

## 2026-08-25 — Do Not Use the Registry as the Primary Settings Store

**Decision:** Ordinary product configuration remains file-based for transparency and portability.

## 2026-08-25 — Protect Configuration Writes

**Decision:** Settings writes should be atomic where practical, and malformed settings should not be silently destroyed/replaced before recovery or inspection is possible.

**Principle:** Human-facing configuration stays readable. Transactional application state stays reliable.

## 2026-08-25 — Rebuild Tidy Natively in C#

**Decision:** Implement `/tidy` as a new internal C#/.NET `TidyEngine`.

Do not invoke or bundle the existing standalone Tidy executable as the implementation.

## 2026-08-25 — Legacy Desktop Icon Sorting Is Not Part of Files

**Decision:** The old utility's Desktop icon rearrangement/resorting behavior is excluded from the new app.

Desktop remains only a filesystem target for `/tidy @desktop`.

## 2026-08-25 — Tidy Reuses Shared File Operation Services

**Decision:** `TidyEngine` owns classification/organization intent but reuses shared file-operation infrastructure for moves, conflicts, permissions, progress, and results.

## 2026-08-25 — Tidy Classification Is Deterministic

**Decision:** v1 uses explicit known-type categorization rather than AI-driven file classification.

**Principle:** Rebuild the useful behavior, not the old application's implementation.

## 2026-08-25 — Ship Both Installer and Portable ZIP

**Decision:** Version one provides both a traditional Windows installer and a portable ZIP.

Both are built from the same self-contained .NET application payload.

## 2026-08-25 — Use Self-Contained .NET Deployment

**Decision:** Users should not need to install the matching .NET runtime separately.

## 2026-08-25 — No Microsoft Store Requirement

**Decision:** Microsoft Store distribution is not part of the v1 release plan.

## 2026-08-25 — Prefer a Simple Traditional Installer

**Decision:** Prefer a simple maintainable installer technology such as Inno Setup unless future requirements justify a more complex toolchain.

## 2026-08-25 — Paid Code Signing Is Not a Version-One Requirement

**Decision:** The project may ship unsigned direct-download releases initially.

Do not use self-signed certificates as fake public trust.

## 2026-08-25 — Updates Are User Controlled

**Decision:** The app may notify users that an update exists, but users choose `Update` or `Later`.

Do not silently force update installation.

## 2026-08-25 — Portable Does Not Automatically Mean Portable User Data

**Decision:** The portable ZIP still uses `%AppData%\<AppName>\` for settings/history by default.

A true "data travels beside the app" mode is a separate future feature if needed.

**Principle:** Offer a normal installer for convenience and a portable build for control.

## 2026-08-25 — Open-Source Project Under GPLv3

**Decision:** The project is intended to be free and open-source software licensed under GNU GPLv3.

The source code should be developed publicly so others can inspect, build, modify, fork, and contribute to the project under the GPLv3 terms.

The choice of GPLv3 is intentional: distributed derivative versions should remain subject to the GPL's source-sharing/copyleft requirements rather than allowing the community codebase to be redistributed as a closed-source derivative.

## 2026-08-25 — Official Builds Remain Free; Donations May Support Development

**Decision:** The intended model is free access to the source and official application releases, including installer and portable builds.

Development may be supported through optional donations.

Donations do not unlock source code, features, or required functionality.

Charging for software remains legally possible under GPLv3, but a paid distribution model is not the current product plan.

## 2026-08-25 — Keep Community Licensing Simple Initially

**Decision:** Do not introduce contributor licensing agreements, dual-licensing infrastructure, or other licensing complexity without a future concrete need.

When the public repository is established, include the actual GPLv3 license and normal community-development files.

## 2026-08-25 — Official Product Name Is Filekin

**Decision:** The application is named **Filekin**.

Use `Filekin` for product branding, executable/package naming, AppData storage, repository naming, installer/portable releases, and new project documentation.

Use `Files` only when referring to the visual filesystem workspace/surface inside Filekin, not as the application name.

**Category description:** `Filekin — a keyboard-first Windows file manager + terminal.`

## 2026-08-25 — Files Command Output Uses an Adaptive / Hybrid Model

**Decision:** The Files command bar does not own a permanently visible output console.

Output presentation depends on the result:

```text
no meaningful output
→ transient status

small useful output
→ temporary inline result

substantial output
→ compact summary + View
→ Files · Output rich view

large error
→ concise inline failure + View details

interactive command
→ independent terminal tab
```

The Files hierarchy should not resize or jump merely because a command was executed.

The command bar itself remains minimal: current path, prompt/separator, and command text. Enter is the primary execution action; routine play/star/refresh/dropdown controls are not part of the default command-bar design.

**Principle:** Output only occupies space when there is output worth occupying space.

## 2026-08-25 — Sidebar Uses `@` Locations and `/` Surface Navigation

**Decision:** The Files sidebar remains titled `LOCATIONS`.

User-defined Locations use a restrained `@` marker and no Explorer-style content icons.

Below the custom Locations, expose `/places` and `/drives` directly so mouse-first users can discover those Filekin surfaces without using the command bar.

`/drives` changes the main Files view to the Drives surface. Individual drives are **not** listed or expanded in the sidebar.

`/places` likewise changes the main Files view to the Places surface.

Navigation language:

```text
@ = named user Locations
/ = Filekin surfaces
```

The sidebar must not become a second filesystem hierarchy.

## 2026-08-25 — Substantial Finite Command Output Uses Expandable Command Shell

**Decision:** Filekin v1 uses an expandable shell-output region attached to the Files command bar for substantial finite command output.

Collapsed:

```text
✓ Completed · 14 lines                         View
```

Expanded:

```text
<command output>
14 lines                                      Collapse
```

`Esc` collapses the output.

Normal finite shell output does not open a `Files · Output` tab by default. Rich views remain available for Filekin-native structured/enhanced information. Interactive CLI programs continue to open independent terminal tabs.

## 2026-08-25 — Visible Controls Require a Defined Need

**Decision:** Filekin does not add decorative, conventional, or speculative UI controls simply to make a surface look complete.

Every visible control requires a defined function, demonstrated workflow need, and intentional interaction placement.

For the Files command bar specifically, do not add shell dropdowns, trash/clear-output controls, pop-out controls, copy icons, run buttons, refresh buttons, favorites, or other unexplained chrome unless separately approved.

Current output-specific actions remain:

```text
collapsed output → View
expanded output  → Collapse
Esc              → Collapse
```

Empty space should remain empty when no control is needed.
