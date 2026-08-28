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

**Superseded on 2026-08-27:** Archive extraction is now session-undoable; see **“Archive
Replacement Is Recyclable and Archive Operations Are Undoable”** below. The original decision is
retained for history.

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

## 2026-08-26 — Recycle Bin Uses Local Action Selection

**Decision:** `Files · Recycle Bin` rows support single and Shift/Ctrl multi-selection so Restore and Delete forever can act on a clearly identified set. These actions live in one compact selection action bar; per-row action buttons are not used. Empty remains a separate whole-bin action.

Recycle Bin selection is local action-targeting state inside that rich view. It does not redefine filesystem `@selection`, which continues to refer to the preserved underlying Files selection.

Mouse and keyboard operate one conventional extended-selection model rather than separate modes. Click or an unmodified navigation key replaces the selection; Shift+click or Shift+navigation extends a range from the anchor; Ctrl+click or Ctrl+Space toggles the targeted item; and Ctrl+navigation moves the keyboard focus without changing the selected set. A thin focus outline identifies that keyboard row separately from the filled selection highlight. Moving or clicking the mouse restores hover feedback after keyboard navigation suppressed a stationary-pointer hover.

The filesystem path row is hidden while the Recycle Bin rich view is open. Its breadcrumb, folder item count, and external-terminal action all describe the preserved underlying Files location, not the visible virtual view. The Recycle Bin header owns its total item count, while the status bar owns the local selected-item count. The command bar remains visible and continues to resolve filesystem references against the preserved Files context.

**Reason:** Matching far-right per-row buttons to filenames is unnecessarily error-prone, and selection-level actions provide a clear path to bulk restore/delete without changing the command language or Files selection semantics.

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

**Decision:** `/places` opens a rich view of standard Windows/user folders such as Desktop, Documents, Downloads, Pictures, Music, and Videos when available.

It provides system-standard destinations without cluttering the personalized Locations sidebar.

## 2026-08-24 — `/drives` Is Confirmed for Version One

**Decision:** `/drives` opens a rich view of available filesystem drives/volumes with concise identifying/storage information and direct navigation.

It is a navigation/discovery surface rather than a full disk-management interface.

## 2026-08-26 — Places Stays Short and Includes Registered Cloud Roots

**Decision:** `/places` has one fixed common section containing Desktop, Documents, Downloads, Pictures, Music, and Videos when they resolve. It does not include Home/user profile. A second optional section lists cloud-storage sync roots registered for the current Windows user, using the provider/account names and paths supplied through Windows. Multiple configured accounts may appear separately. Filekin does not hardcode vendors or guess conventional cloud-folder names; a provider mounted as a drive appears through `/drives`.

Places and available drives are direct navigation actions: single-click or Enter opens the target and dismisses the temporary rich view. `/drives` shows assigned but unavailable network/removable/optical drives as disabled rows with a concise status rather than hiding them. Available drive rows show root, label, type, free/total capacity, and a restrained usage bar when capacity is known.

**Reason:** Places should be a super-simplified destination picker, not a second user-profile browser. Windows sync-root registration covers providers and multiple accounts without vendor-specific heuristics. Showing unavailable assigned drives preserves useful discovery while preventing dead navigation actions.

## 2026-08-26 — `/drives` Refreshes Live When a Volume Arrives or Leaves

**Decision:** While the `/drives` view is on screen it re-enumerates in response to the Windows `WM_DEVICECHANGE` volume broadcast, so plugging in a USB drive or memory card, inserting a disc into an existing optical drive, or mapping and unmapping a drive letter updates the list without the user leaving and returning to the window. The refresh runs only while the Drives view is open, never inside the window procedure, and coalesces the burst of broadcasts a single insertion produces before re-enumerating.

Filekin registers for nothing: Windows broadcasts volume events to all top-level windows. Devices that never receive a drive letter — a phone connected over MTP, for example — are not volumes and remain out of scope for `/drives` entirely.

**Reason:** A drive row that says `No media` after the user has just inserted the media is wrong, and a row that cannot be opened because the view is stale is worse than not showing it. Removable storage is exactly the case where the assigned-but-unavailable row exists, so the view has to notice when that state changes.

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

A durable user-configurable interactive-app registry is required after the hosted terminal-tab behavior is complete. Users must be able to add commands that should open in terminal tabs when Filekin's built-in rules do not recognize them. The canonical configuration should support executable and, where necessary, argument-sensitive rules.

The authoring surface is deliberately deferred until real terminal tabs expose the workflow clearly. Hand-editable configuration, a Settings editor, and an app command such as `/registerapptab <appname>` are candidates; do not select the final surface or command name yet. Multiple surfaces may eventually edit the same underlying configuration.

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

## 2026-08-26 — External Escape Hatch Is `/ext` (Plus a Button), Not Explorer

**Decision:** The External Terminal Escape Hatch (UX-DESIGN.md) is surfaced as both a command and a GUI button (owner choice). The command is **`/ext`**, not `/terminal` — the command bar is already a terminal-backed surface, so "external" is the meaningful distinction. Bare `/ext` opens the user's external terminal at the current folder; `/ext <program> [args]` launches that program as an independent external process at the folder (for example `/ext code`). A small command-prompt icon button in the Files path row performs the bare-`/ext` action. Typing an interactive tool name (`powershell`, `claude`, …) is a separate, embedded hosted-terminal-tab path, not `/ext`.

A dedicated "open the current folder in Windows Explorer" command (`/reveal`) was considered and **rejected**: Filekin is the file manager, so it must not push users back into Explorer. Anyone who truly wants it can run `/ext explorer`; it is not promoted.

**Reason:** `/ext` keeps the escape hatch in Filekin's `/`-command language, reads clearly against the embedded terminal, and generalizes to any external app without a command per target. Excluding a first-class Explorer command keeps the product's identity as the Explorer replacement intact.

**Principle:** Give people a clean way out to their own tools; don't advertise the tool you're replacing.

## 2026-08-26 — The Delete Command Is `/toss`, Not `/delete`

**Partly superseded 2026-08-27** — see "Recoverable Delete Answers to `/toss`, `/trash`, and
`/delete`" below. `/toss` remains the primary name; the exclusion of `/trash` and `/delete` as
ways to reach it does not.

**Decision:** The app-owned delete command is named **`/toss`** (throw it in the trash). It is no longer `/delete`, and `/trash` is no longer the delete command. A resolved multi-item `@selection` deletes every target; all targets are validated to exist first.

**Reason:** The command's value over PowerShell's `del`/`rm` is that it is **recoverable** — it goes to the Recycle Bin. `/toss` is short, plain English, and carries the "set aside, not yet emptied" connotation (it sits in the bin until emptied), which matches recoverable delete. `/delete` sounds permanent and is generic. `/trash` was considered but is problematic as the *delete* verb here because it is more naturally read as "open the trash" (noun) — see below. `/bin` was rejected (a developer reads `/bin` as the binaries folder); `/rbin` rejected (a cryptic abbreviation). Length was explicitly not the deciding factor (UX-DESIGN.md "Readability Over Abbreviation" — speed comes from autocomplete).

**Safety:** A `/toss` targeting items **outside** the current folder (easy to hit by accident, since a leading `/` or `\` jumps to the drive root) prompts for confirmation. Deleting the current folder itself (`@thisfolder`) or selected items in it does not prompt. The Recycle Bin remains the recoverability net.

**Opening the Recycle Bin — `/recycle` (decided 2026-08-26):** the owner first proposed a verb/noun split (`/toss` = delete, `/trash` = open the bin), then agreed the risk was real — "trash" is commonly a *delete* verb (macOS "Move to Trash"), so `/trash <file>` reads like a delete and would conflict with opening the bin. Resolved: **`/recycle`** opens the Recycle Bin view (unambiguous, never a delete verb). It is a Files rich view (`Files · Recycle Bin`) listing name / original location / date deleted / size with a per-item **Restore**; Back or Esc returns to Files. Implemented over the Windows shell (`Shell.Application`) via `IRecycleBin` / `WindowsRecycleBin`, integration-tested with a recycle→list→restore round trip. Known limitation: the restore verb is matched by its English name, so restore is not yet localized.

**Principle:** Name the command for the safe thing it does; make length a job for autocomplete.

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

## 2026-08-26 — Saved Locations Use an Ordered, Readable JSON Schema

**Decision:** The first `settings.json` schema stores saved Locations as an ordered array:

```json
{
  "locations": [
    { "name": "Projects", "path": "D:\\Projects" }
  ]
}
```

The array order is the sidebar order. `name` is both the visible short name and the case-insensitive command-bar reference name; Filekin supplies the leading `@`. Names contain letters, numbers, `_`, or `-`, and may not replace the intrinsic `@thisfolder`, `@selection`, or `@parent` references. Paths are absolute filesystem paths; they are not required to be online at load time because removable and network destinations may be temporarily unavailable.

User-defined Locations are checked before convenience aliases for Windows known folders, so an explicit saved `@downloads` refers to the user's saved destination. Intrinsic workspace references still always win.

Unknown JSON fields are retained across a load/save cycle. A malformed file is left unchanged and Filekin starts with no saved Locations while reporting the problem. Invalid individual entries are ignored without discarding valid siblings. File replacement is performed through a same-directory temporary file.

**Reason:** The schema stays obvious to people editing or backing it up, preserves sidebar ordering without a second field, and lets saved Locations and command-bar references share one source of truth.

## 2026-08-26 — Locations Are Managed Through `/location` and the Sidebar

**Decision:** Location is the user-facing object; its `@name` reference is created automatically. Version one uses one grouped app command:

```text
/location add projects @thisfolder
/location set projects D:\Work\NewProjects
/location rename projects client-work
/location remove client-work
```

`add` requires a new name. `set` requires an existing Location and changes only its saved path. `rename` changes only the name/reference. `remove` deletes only the saved Location pointer and never deletes or changes the target folder. Relative paths resolve from the current Files location, like other app-owned commands.

Mouse users use the sidebar `+` to add a Location. Existing entries expose Edit and Remove through a compact context menu; the editor can change name and path together in one saved update. The editor states explicitly that Remove affects the saved Location, not the folder.

**Reason:** `/location` matches the visible LOCATIONS concept and scales coherently across the lifecycle. A command such as `/newref` would expose the implementation concept, would not naturally cover editing/removal, and would blur user Locations with intrinsic references such as `@selection`.

## 2026-08-26 — Startup Files Location Is User-Selectable

**Decision:** Filekin opens the Files workspace at the current user's profile folder by default. Settings exposes **Open Files at launch**, with Home, saved `@Locations`, and an explicitly browsed filesystem folder as choices. This is an intentional preference; version one does not automatically restore the last viewed folder.

When a saved Location is selected, later path changes to that Location affect the next launch destination, and renaming the Location keeps the preference aligned. A removed, missing, or unavailable target falls back to Home for that launch with a non-blocking notice. An unavailable path preference is preserved rather than silently cleared.

Filekin does not implement this by editing PowerShell profiles. PowerShell's `Set-Location` affects a runspace, and `pwsh -WorkingDirectory` controls a newly launched PowerShell process; neither is the owner of Filekin's Files startup preference.

**Reason:** A project-focused user should be able to start directly in their working folder without changing every PowerShell host on the machine or relying on implicit last-session restoration.

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
## 2026-08-26 — Ctrl+Tab Switches Workspaces and Is the Only Key Filekin Takes From a Terminal

**Decision:** `Ctrl+Tab` moves to the next workspace and `Ctrl+Shift+Tab` to the previous one. The
order matches the tab strip: the permanent Files workspace first, then the live terminal tabs,
cycling at both ends.

This is the single keystroke Filekin claims while a terminal tab has focus. It is handled at the
window before terminal input, and marked handled so neither the hosted shell nor WPF's own
control-tab navigation receives it. Every other key — including `Tab`, `Shift+Tab`, `Ctrl+C`,
`Escape`, and `Y`/`N` — still belongs to the hosted shell, preserving the terminal-lifecycle
guardrail against intercepting normal terminal input.

`Ctrl+Tab` does nothing when no terminal tab exists, and is ignored while an in-app confirmation is
waiting for an answer.

## 2026-08-26 — The Terminal Renderer Pins Every Glyph to Its Cell

**Decision:** `TerminalControl` draws text as `GlyphRun`s with explicit per-cell advance widths.
It never lets the font advance the pen across a run.

A shaped text run advances by the font's own advance width, which is almost never the whole-pixel
cell width the grid uses. The difference is small per character and invisible in isolation, but it
accumulates inside a run: Cascadia Mono at 14 px advances 8.203 px against a 9 px cell, which put
the caret 31.9 px — about four columns — past the last drawn character after 40 characters.

A terminal is a grid, not a paragraph. Cell position is authoritative, and the renderer states it
for every glyph. Combining marks take a zero advance on top of their base glyph, and a cluster the
font cannot supply falls back to a laid-out text object drawn at the same cell origin, so an
unsupported glyph costs one text layout instead of breaking the grid.

The cell width rounds to the nearest pixel rather than up, so columns stay close to the font's real
metrics instead of being stretched. Whole-pixel cells are kept because backgrounds and the caret
must land on device pixels.
## 2026-08-26 — Terminal Copy and Paste Keys

**Decision:** In a terminal tab, `Ctrl+C` copies **only when a selection exists**. With nothing
selected it passes through to the hosted shell as the interrupt byte, which is the only way to stop
a running program. `Ctrl+Shift+C` always copies. `Ctrl+V` and `Ctrl+Shift+V` both paste, and
`Shift+Insert` continues to paste.

Binding `Ctrl+V` costs full-screen editors their `Ctrl+V` (vim's visual-block mode). This matches
the Windows Terminal default and was accepted deliberately; if it becomes a problem it belongs in
settings rather than as a silent rebinding.

Terminal text is selected by dragging. Selection is stored in absolute line indices so it stays over
the same text while new output scrolls the screen, and it is dropped when the user types, when the
buffer switches to or from the alternate screen, and when the tab changes.

This applies to the terminal surface only. The Files command bar and the expandable command-output
region are ordinary text controls where `Ctrl+C`, `Ctrl+V`, `Ctrl+X`, and `Ctrl+A` keep their normal
Windows meaning.

## 2026-08-26 — Terminal Tab Shortcuts

**Decision:** `Ctrl+Shift+T` opens a new terminal tab at the current Files location, and
`Ctrl+Shift+W` closes the selected terminal tab. Closing a tab whose root shell is still alive shows
the same in-app confirmation as the tab's close button, so a mistyped shortcut cannot silently kill
a running job.

`Ctrl+W` was rejected: PSReadLine binds it to `BackwardKillWord`, and it is `unix-word-rubout` in
readline, the tty WERASE key over ssh, the window prefix in vim, and search in nano. Claiming it
would break delete-previous-word in every terminal tab.

`Ctrl+Shift+letter` is the safe namespace because the hosted shell cannot distinguish it from plain
`Ctrl+letter` anyway — Filekin does not implement the kitty keyboard protocol — so claiming these
combinations takes nothing away from the shell.
## 2026-08-26 — Alt Shortcuts and Mouse Reporting Belong to the Hosted Program

**Decision:** A terminal tab forwards Alt shortcuts and mouse activity to the program running inside
it, because full-screen tools define their own bindings and do their own scrolling.

**Alt keys.** Windows reports an Alt combination as a system key and never raises a text-input event
for it, so the terminal resolves the real key itself and sends the traditional Escape-prefixed form:
Escape then the character for a printable key, Escape then the ordinary byte for Enter, Backspace,
Tab and Escape, and the existing modifier parameter for cursor and function keys. The character
comes from the user's current keyboard layout rather than an assumed US mapping. `Alt+F4` and
`Alt+Space` stay with Windows.

**Mouse.** When a program enables mouse tracking (DECSET 1000, 1002 or 1003) the terminal reports
presses, releases, wheel and motion to it, in SGR form when the program asked for it (DECSET 1006)
and the legacy form otherwise. The terminal's own wheel scrollback only applies when no program has
asked for the mouse. Holding **Shift** overrides tracking so the terminal's own text selection stays
reachable, which is the same escape hatch other terminals provide.

Evidence: a raw ConPTY capture confirmed conhost forwards the mouse-mode requests only once the
client has put its input handle in virtual-terminal mode, and that Filekin's reports arrive at the
program correctly encoded (`ESC[<64;74;16M` for a wheel-up at column 74, row 16).

## 2026-08-26 — Settings Is a Rich View, Not a Dialog

**Decision:** Settings opens as a rich view over the preserved Files workspace, in the same family as
`/recycle`, `/places`, and `/drives`. The sidebar footer entry and the `/settings` command open the
same surface; Esc or Back dismisses it; the Files location, selection, and `@selection` underneath
are untouched; the command bar stays usable.

**Reason:** Filekin already has exactly one mechanism for a temporary surface over Files, and it
already carries the dismissal, focus-restore, and state-preservation behaviour Settings needs. A
modal window would have been a second mechanism with its own lifecycle for no gain, and a
keyboard-first product should not send the user to a dialog to change a preference they can also
reach by typing.

Settings is reached from the sidebar footer rather than the `/surfaces` list because it is not a
Files destination — nothing in it navigates the filesystem hierarchy.

## 2026-08-26 — Settings Apply Immediately

**Decision:** Every choice in Settings writes `settings.json` the moment it is made. There is no
Save button, no Apply, no Cancel, and no dirty state. A failed write reports the reason inline and
leaves the previous value in force; a theme applied optimistically for instant feedback is reverted
if its write fails.

**Reason:** The durable file and the running UI must never disagree — the same rule the Location
catalog already follows. A Save button introduces a third state (chosen but not saved) that has to
be reconciled on dismissal, on window close, and on a concurrent edit of the same file.

## 2026-08-26 — One Owner for `settings.json`

**Decision:** A single `UserSettingsService` holds the in-memory settings document. The Location
catalog and the Settings surface both read and mutate through it, and each mutation is a whole-file
write of that one snapshot.

**Reason:** Two writers each rebuilding the document from their own fields silently discard the other
half. The Location catalog previously constructed a fresh `FilekinSettings` from its own list, which
would have erased the theme, accent, startup target, and interactive programs on the next Location
edit.

## 2026-08-26 — A Theme Is a Palette and Nothing Else

**Decision:** Filekin ships **Dark**, **Light**, and **Follow system**. Dark is the default; Follow
system resolves to dark or light from the Windows app-mode preference, re-resolving live when Windows
changes it. A theme changes colours only — never a font, a metric, a spacing, or a layout. Both token
sets define exactly the same keys, so a theme swap needs no style edits.

**Reason:** Owner instruction, 2026-08-26: "The themes should just be color changes to everything
that's colored." Keeping the two dictionaries key-for-key identical is what makes that checkable: a
colour that only exists in one of them is a bug, and a hard-coded colour anywhere outside them is a
half-themed surface.

The light grounds, lines, and text come from the light half of the original Filekin Files colour
study, whose dark half is the palette already shipping. The two sets are the same design, not two
independent guesses.

## 2026-08-26 — The Accent Is User-Selectable

**Decision:** Filekin ships six accents — **Blue** (default), Teal, Green, Orange, Pink, and Purple —
each with a dark and a light variant tuned for its ground. The accent drives the spark colour, its
ink, the dim and hairline washes, and the directory colour in the Files listing. It never replaces
the semantic status colours, so nothing here is red (Bad), amber (Warn), or the green used for Good.

This supersedes the note in *Visual Identity: Blue Accent, Dark Default Theme* (2026-08-25) that made
accent selection a later version. Blue remains the shipped default.

**Reason:** Owner instruction, 2026-08-26. The shades are muted rather than saturated so the same
accent reads correctly on both grounds — the restraint is what lets one choice serve light and dark.

Accents are stored by name, and a name this build does not recognise falls back to blue **without
being rewritten**, so an accent added by a newer build survives being opened by an older one.

## 2026-08-26 — A Hosted Terminal Follows the Theme

**Decision:** A terminal tab's ground and default text follow the active theme, and its caret and
selection follow the accent. The sixteen ANSI colours are **not** accent-tinted; they keep their
standard meanings, with a darkened set used on a light ground.

**Reason:** A terminal renders raw cells and never reads the resource dictionary, so it has to be
repainted explicitly. Leaving it dark inside a light window would be exactly the half-applied theme
the palette rule is meant to prevent. The ANSI colours stay standard because a program that asks for
red means red; the light set only darkens them, because the standard bright colours are chosen for a
dark ground and vanish on a light one.

## 2026-08-26 — Users May Register Their Own Interactive Programs

**Decision:** The interactive registry accepts user-added program names from Settings. They add to
the built-in rules and can never remove one; built-ins are listed in Settings so the user can see
what is already covered.

This supersedes "Version one has no user-defined interactive rules."

**Reason:** Owner instruction, 2026-08-26. The built-in list deliberately does not try to enumerate
every interactive program, which leaves `vim`, `htop`, `nano`, and every in-house tool running down
the finite path. A user rule is a plain executable name, so routing stays deterministic and keeps the
2026-08-24 rule that interactive routing must not depend on heuristics or AI.

A user rule is not argument-sensitive: `vim file.txt` is still an editor. Only the shipped Python
rule inspects arguments.

## 2026-08-26 — Settings Categories Own Subjects, Not Controls

**Superseded in part on 2026-08-27:** Archives became a fifth category because archive behavior is a
new subject. The rule that categories own subjects rather than individual controls still stands.

**Decision:** The Settings rail lists **Appearance**, **Startup**, **Terminal**, and **Advanced**. A
new preference joins an existing category; the rail grows only when a genuinely new subject arrives.
Categories are text only — no glyphs.

**Reason:** `UX-DESIGN.md` names "bloated Settings screens" as an explicit anti-pattern, and a rail
that grows one row per setting becomes one. Four words need no icons, and decorative glyphs beside
them would be the "random excessive icons" the same list rules out.

Categories are added when their subject is actually built. Operation history, updates, and the
default-shell preference are anticipated by the specifications but have no implementation yet, so
they have no empty shells waiting for them.

## 2026-08-26 — Command Completion Is Tab-Requested and Transient

**Decision:** Typing alone never opens command-bar chrome. Tab explicitly requests Filekin completion
while the caret is in a matching app-owned `/` command token or known `@` reference token. A unique
match completes immediately. An ambiguous match first extends the shared prefix, then opens a compact
overlay containing the matching token and a concise description; reference descriptions show their
resolved destination when available.

While the overlay is open, Up/Down changes the highlighted suggestion, Tab accepts it, and Esc
dismisses the overlay without changing the draft. Enter always executes the text already in the
command bar rather than silently accepting the highlight. With no overlay open, Up/Down retains the
existing command-history behavior. Unknown `@` syntax and ordinary shell text are not claimed.

**Reason:** Completion should make Filekin's readable language fast and discoverable without turning
the command bar into an IDE field or adding motion on every keystroke. Requiring Tab makes the list an
explicit request, while descriptions teach commands at the moment the user asks for them.

## 2026-08-26 — `/run` Is the Only Launch Command

**Decision:** `/run <target> [arguments]` is the single app-owned command for starting a file or an
application. There is no `/open`. Double-click and Enter in the Files list keep their existing
Windows-association behavior; `/run` is the command-language expression of the same intent.

**Reason:** Owner decision, 2026-08-26. Two commands that both start something would have to explain
their difference to every user, and the difference — association versus execution — is not one the
user is thinking about at the moment they want a program to start.

## 2026-08-26 — `/run` Resolves the Visible Folder First, Then `PATH`

**Decision:** A relative `/run` target is looked for in the visible Files folder first, then through
the ordinary Windows `PATH` and `PATHEXT` lookup. Absolute paths, `@location\child` references,
shortcuts, and associated documents all resolve directly. A name that resolves nowhere is still
handed to Windows shell execution, and its failure is reported inline.

This supersedes "If `/run tool.exe` cannot resolve `tool.exe` relative to the current Files location,
it fails clearly rather than performing an implicit system-wide search" (2026-08-24) and the
`Try: /where tool.exe` suggestion in `UX-DESIGN.md`.

**Reason:** Owner decision, 2026-08-26. `PATH` is not a system-wide search — it is the list Windows
itself consults, so honouring it is the opposite of crawling the machine. Without it, a PATH-installed
entry point such as `snapmap-midi` would need its full path typed for `/run` while the same bare name
already works in the command bar. Filekin still never enumerates installed applications.

## 2026-08-26 — `/run` Routing Is Decided by File Metadata, Not by Watching the Process

**Decision:** `/run` chooses where a target runs **before** creating the process, from deterministic
metadata:

- a registered interactive program, or a `.bat`, `.cmd`, `.com`, `.ps1`, or `.py` file, or an `.exe`
  whose PE subsystem is `WindowsCui` → a **hosted Filekin terminal tab**;
- everything else — GUI executables, shortcuts, associated documents → an **independent external
  launch** through Windows shell execution;
- a folder → **refused** with a clear message, because Files owns folder navigation. Filekin does not
  quietly open Explorer.

**Reason:** The spike proved that no supported API attaches a running process to a pseudoconsole, so
routing has to happen at creation time. The PE subsystem byte is the same fact Windows itself uses to
decide whether a program needs a console, so reading it is deterministic metadata rather than a
heuristic — and it means a console tool such as `snapmap-midi` works with `/run` without the user
registering it in Settings first.

`/ext` stays distinct and unchanged: bare `/ext` opens the preferred **external** terminal at the
Files folder, and `/ext program args` launches an explicitly independent external process.

## 2026-08-26 — The Terminal Fallback Is Offered Once, After Two Seconds, and Is Always a Fresh Start

**Decision:** An unknown raw shell command still begins in the finite persistent runspace. If — and
only if — its executable is a concrete Windows console target, and it is still running after two
seconds, the command bar shows one offer:

```text
tool is still running. Run it again in a terminal tab? Y/N
```

`Y` stops the runspace invocation and starts the **same command again as a fresh process** in a
hosted terminal tab. `N` or `Esc` leaves it running and changes the status to
`tool is still running · Esc to stop`; a later `Esc` stops it. The offer is made at most once per
command, and never after the user has already stopped the command.

PowerShell cmdlets and functions are never offered, because they do not resolve to a console image.

**Reason:** The spike established that a running process cannot be promoted into a pseudoconsole, so
the honest action is a fresh relaunch and the prompt must say so. Two seconds is long enough that a
finite command answers before anything appears, and short enough that a tool waiting for input does
not look frozen. Restricting the offer to a resolved console image keeps it out of the way of the
ordinary cmdlets that make up most command-bar traffic.

## 2026-08-27 — Bare `/info` Describes the Selection, Then the Folder

**Decision:** `/info` with no target describes the current selection. With nothing selected, it
describes the visible Files folder. Only when there is neither does it explain itself.

**Reason:** Owner decision, 2026-08-27. The specifications only ever showed `/info` with an explicit
target, which left the most common inspection — "what is this thing I just clicked?" — needing two
words. Selection first matches what the user is looking at.

## 2026-08-27 — Type-Specific Metadata Comes From the Windows Property System

**Decision:** Image dimensions, media duration, and executable product/version/company are read
through the Windows Property System (`SHGetPropertyStoreFromParsingName` → `IPropertyStore`). Filekin
does not write per-format parsers. Friendly type names come from `SHGetFileInfo` with `SHGFI_TYPENAME`
— the same text Explorer shows. Executable architecture is read from the PE header, which `/run`
already reads.

**Reason:** Owner decision, 2026-08-27, on the guardrail "prefer standard .NET and Windows APIs over
custom reinvention". A per-format parser set would mean choosing which formats Filekin supports and
being wrong about it forever; the property system means a codec Windows learns about later works
without a Filekin change.

Verified before adoption: a throwaway probe read image dimensions, `.wav` duration, `cmd.exe`
company/version, and a shortcut's target through one property store, **on a thread-pool (MTA)
thread** — which is where inspection runs, since it must never touch the UI thread.

## 2026-08-27 — Filekin Shows "Company", Never "Publisher"

**Decision:** The company name inside an executable is labelled **Company**. Filekin does not use the
word "Publisher" and does not verify Authenticode signatures in v1. Real signature checking stays
with the Windows Properties dialog.

**Reason:** Owner decision, 2026-08-27. A company name is a string anyone can write into their own
file. Printing it under the word "Publisher" would tell the user Filekin had checked something it had
not — a claim about trust, made falsely. A verified-publisher row is a separate piece of work with a
real signature check behind it, not a relabelling.

## 2026-08-27 — Encoding Is Free, Line Count Is Not

**Decision:** The Info sheet shows a text file's **Encoding** immediately and puts **Lines** behind a
`Count` action, beside `SHA-256` and its `Calculate`.

**Reason:** Owner decision, 2026-08-27, was that both should wait for a click. Encoding turned out to
cost nothing: deciding whether a file is text at all already reads its first 8 KB, and the byte-order
mark is in those bytes. Hiding an answer Filekin already has would be theatre. Counting lines reads
every byte of the file, so it stays an explicit request. The rule the user learns is unchanged:
expensive work waits to be asked for.

A file is treated as text when its first 8 KB contains no NUL byte. Encoding is reported from the
byte-order mark, or as `UTF-8` / `8-bit text` from whether the bytes form valid UTF-8 sequences; no
specific legacy code page is ever guessed at.

## 2026-08-27 — Recursive Size Is Honest, Bounded, and Abandonable

**Decision:** The recursive scan behind `/info` on a folder or selection:

- reports progress on a **250 ms timer**, never once per file;
- **never follows reparse points** — a junction, symlink, or cloud placeholder counts as one link and
  is not walked into;
- records folders it could not read and says `Some folders could not be read` beside a total that is
  therefore partial;
- **stops when the sheet closes**.

**Reason:** Owner decision, 2026-08-27. Each rule answers a way the feature could lie or hurt.
Per-file updates would flood the dispatcher on a large tree. Following a junction would count the
same files twice, or loop forever. Silently skipping an unreadable folder would present a total that
quietly omits whole subtrees — a partial answer that says it is partial is worth more than a refusal,
which is what any scan of `C:\` would otherwise become. And a scan nobody is looking at is pure waste.

## 2026-08-27 — Filekin Reveals a Shortcut; Windows Edits It

**Decision:** `/info` on a `.lnk` shows **Target**, **Arguments**, and **Start in**, read through
`IShellLink`. Filekin has no shortcut editor and no launch-configuration UI.

**Reason:** Owner decision, 2026-08-27. "Where does this shortcut point?" is a real everyday question
and nothing else in Filekin answers it. Editing one is a different job: the command bar already
launches a program with arguments directly, and the native Properties dialog already edits Target,
Arguments, Start in, and compatibility. Building a second editor would turn `/info` from inspection
into shortcut management.

`IShellLink::Resolve` is deliberately never called — it can show UI and search the network for a
missing target. The stored path is read raw.

## 2026-08-27 — Info Is a Field Sheet, Not a Listing

**Decision:** The Info rich view is a label/value sheet: a fixed label column, the value, and an
optional action on the right. It has no hover highlight, no hand cursor, and no navigation. Rows
carrying live scan totals are mutated in place rather than rebuilt.

**Reason:** The workspace-surface guardrail says rich views must not all collapse into one template,
and must not look like filesystem folder listings. Places and Drives are lists of destinations to
choose from; Info describes one thing. Rebuilding the row collection on each scan tick would also
throw away the row the keyboard is on — the defect Places and Drives already had to fix.

`/info` is deliberately **not** a sidebar entry: it needs a target, so it belongs to the command bar.

## 2026-08-27 — Windows Properties Uses `SHObjectProperties`, Not the `properties` Verb

**Decision:** The Windows Properties escape hatch calls `SHObjectProperties(hwnd, SHOP_FILEPATH, path,
null)`. It does **not** call `ShellExecuteEx` with the `properties` verb. The Filekin window handle is
passed as the owner so the dialog cannot be lost behind the app.

**Reason:** Found by the owner in live use, 2026-08-27, and then measured rather than guessed at. The
`properties` verb resolves a path through ordinary file-system parsing, which the user profile
folder's own properties handler refuses:

```text
target                     ShellExecuteEx "properties"     SHObjectProperties
a file                     works                           works
D:\github\filekin          works                           works
C:\Users\<user>            FAILS — ERROR_CANCELLED (1223)   works
C:\Users                   works                           works
C:\                        works                           works
```

The failure surfaced as the shell's own "Unspecified error" box, which named no cause. The user
profile folder is the single most common thing a file manager is asked about, so the one broken case
was the one that mattered. `SHObjectProperties` is the API documented for invoking the Properties
command on a filesystem object, and it handled every case.

`WindowsPropertiesDialogTests` pins this against the real shell, in the CI-excluded
`RequiresInteractiveShell` category, with the user profile folder as its first case. A change back to
the verb would pass every other target and break that one again.

## 2026-08-27 — Archive Extraction Always Has One Predictable Folder by Default

**Decision:** Normal `/unzip` extraction creates exactly one new folder in the destination. An
archive with one wrapper directory reuses that wrapper; loose archive contents receive a directory
named after the archive. `-noroot` or the preview's `Into a folder` toggle explicitly removes it.

**Reason:** This states the existing “avoid redundant directory nesting” product rule positively.
The user can predict the result without first inspecting whether an archive happens to carry its own
wrapper directory.

## 2026-08-27 — `/unzip` Supports Multiple ZIP Archives and Explicit Destination Grammar

**Decision:** The grammar is:

```text
/unzip [-noroot] [-skip] [-overwrite] [-y] <archive...> [destination]
```

The destination may be a path, `@thisfolder`, or a saved `@Location`, and need not exist yet. Multiple
archives are planned and processed independently; a failure in one does not block the others. Version
one opens ZIP only. Recognized archive extensions such as `.7z` or `.rar` receive an unsupported-format
error rather than the misleading claim that they are not archives.

**Reason:** Extraction can safely apply the established partial-success rule, while adding non-ZIP
formats would require a third-party dependency and therefore a separate product decision.

## 2026-08-27 — `/zip` Is a Version-One Command With No Switches — Superseded

**Superseded the same day** by "`/zip` Takes the Same Switches as `/unzip`, Minus `-noroot`" below.

**Decision:** `/zip <item...> [name.zip]` creates a ZIP archive. It accepts no switches. Its preview
owns whether a single source keeps its outer folder and whether an existing output is replaced.

**Reason:** `/zip` decides one output file, and its optional second argument already names that
decision. Adding command switches for choices the preview makes visibly would enlarge the command
language without improving the common workflow.

## 2026-08-27 — Archive Preview Is the Default and Archives Is a Settings Subject

**Decision:** `/unzip` and `/zip` show a shared preview by default. `/unzip -y` skips it for one
invocation; `/zip` has no command-line override (**superseded** — `/zip -y` now exists). Settings adds an **Archives** category with
`archives.previewBeforeExtracting` and `archives.whenAFileExists` (`skip` or `overwrite`). Choices
apply immediately. Skip and preview-on are the shipped defaults.

This adds a fifth Settings category and supersedes the earlier four-category list: archive behavior
is a genuinely new subject rather than another control within Appearance, Startup, Terminal, or
Advanced.

**Reason:** Extraction may write hundreds of files, so preview is useful before the first mistake,
not as an expert option discovered afterward. The settings preserve a fast path for users whose
preferred defaults are already settled.

## 2026-08-27 — Archive Replacement Is Recyclable and Archive Operations Are Undoable

**Decision:** Overwrite sends an existing destination file to the Windows Recycle Bin before writing
its replacement. A completed `/unzip` or `/zip` offers session-scoped Undo on the command result.
Undo deletes only paths Filekin created, removes created folders deepest-first when empty, and restores
recycled originals after the replacement path is clear.

This supersedes **“`/unzip` Is Not Undoable”** (2026-08-24). Durable `/history` and `/undo` still wait
for SQLite; the current `IOperationJournal` implementation is intentionally in-memory.

**Reason:** The archive plan already supplies exact bookkeeping, so reliable rollback is inexpensive.
Deleting hundreds of files manually after extracting to the wrong place is precisely where Undo has
high practical value.

## 2026-08-27 — Running Archive Work Outlives Its Rich View

**Decision:** Once `/unzip` extraction or `/zip` compression starts, Back/Esc and opening another
rich view dismiss only the archive presentation. The operation continues under Files ownership. A
persistent command-bar task row shows progress and exposes **View** and explicit **Stop** actions;
View reopens the live archive surface. Version one permits one archive operation at a time.

The completed or stopped archive result replaces the live task status. Its session Undo action is
shown only beside that archive result, never beside a later unrelated result.

**Reason:** Closing a temporary view should not unexpectedly interrupt filesystem work. Keeping
progress and control in the command bar lets the user continue working without making archive jobs
invisible or requiring a task tab for every operation.

## 2026-08-27 — `/go` Navigates Files and Does Not Require Quotes Around Spaces

**Decision:** `/go <folder>` is a version-one app command that navigates the visual Files workspace.
Everything after the command name is one folder target, so an ordinary path containing spaces is
valid without quotes:

```text
/go D:\Client Work\Current Project
/go ..
/go @downloads
/go @projects\Current Project
```

Relative paths start from the visible Files folder. Existing workspace references are supported only
when they resolve to exactly one folder. Optional matching single or double outer quotes are accepted
for familiarity. A missing folder, a file, an empty reference, or a multi-item reference reports an
inline error and leaves Files unchanged. A bare path still retains ordinary PowerShell meaning.

**Reason:** PowerShell can navigate paths with spaces, but its quoting and invocation rules are
unnecessarily awkward for a file manager's primary navigation surface. `/go` makes the action
explicit and predictable without redefining raw shell syntax or attempting to parse PowerShell.

## 2026-08-27 — Saved Locations Follow App-Owned Move and Rename Operations

**Decision:** A successful app-owned `/move` or `/rename` automatically rebases every saved Location
whose path is the moved item or lies anywhere beneath it. Location names and order do not change.
Nested Locations preserve their relative suffix under the new destination. `/copy` never retargets a
Location because its original path remains valid.

For example, if `@work` points to `D:\Work` and `@client` points to `D:\Work\Client`, then:

```text
/move @work @archive
```

updates them to `<@archive>\Work` and `<@archive>\Work\Client`. The command result reports how many
saved Locations followed. This guarantee applies to operations Filekin owns; arbitrary PowerShell,
another process, or external filesystem changes are not inferred after the fact.

The filesystem move and `settings.json` cannot be one atomic OS transaction. Filekin performs the
move first, writes all Location rebases as one durable settings mutation, and rolls the filesystem
move back in reverse order if that write fails. A failed rollback is reported explicitly as an
inconsistent state rather than as success.

**Reason:** Locations represent the user's durable conceptual destinations, not disposable copies of
paths. An app-owned operation has exact old/new information and should not knowingly break the
sidebar, command references, or a startup preference that targets the saved Location. Moving these
commands off the WPF thread at the same boundary also enforces the existing performance guardrail for
recursive, network, cross-volume, and Recycle Bin work.

## 2026-08-27 — Recoverable Delete Answers to `/toss`, `/trash`, and `/delete`

**Decision:** `/toss`, `/trash`, and `/delete` all invoke the same recoverable Recycle Bin operation.
`/toss` stays the primary name — it is what the documentation teaches and what the completion list
presents first — while `/trash` and `/delete` are registered aliases of that one command. All three
appear in command completion; the two alias entries name `/toss` in their description. Usage and
failure lines repeat whichever name the user typed. This supersedes the part of the 2026-08-26
decision that excluded `/trash` and `/delete`.

Opening the Recycle Bin view remains `/recycle`, so no alias here is ambiguous.

**Reason:** The 2026-08-26 analysis of which single word is best still holds, but choosing one word
does not require rejecting the others. A user who types `/delete` or `/trash` is unambiguously asking
for the operation Filekin already has, and answering with "Unknown command" is a worse outcome than
accepting a second name. The ambiguity the earlier decision guarded against was `/trash` meaning
*open the bin*; that reading is gone now that `/recycle` owns the view.

**Implementation:** `IAppCommand` gained an `Aliases` list, defaulting to empty. `AppCommandDispatcher`
registers each command under its name and its aliases and throws on any collision between them, so an
alias cannot silently shadow another command. This is a narrow mechanism for confirmed multi-name
operations, not a general synonym facility: adding an alias still requires a product decision.

## 2026-08-27 — `/tidy` Shows a Plan First, and Sweeps Unknown Types into `Other`

**Decision:** Two parts of the confirmed `/tidy` design are superseded.

**1. `/tidy` shows its plan before moving anything.** ARCHITECTURE.md Topic 5X had Tidy start the
moment Enter was pressed, with no preview. It now opens a `Files · Tidy` rich view listing one row
per category — the folder, a few file names, the count, and whether the folder already exists — with
a tick per category. Untick a row and those files stay put. `-y` skips the plan for one run, a Tidy
settings toggle skips it always, and a "Don't show this again" tick on the plan writes that same
setting.

**Reason:** the owner asked for it, and consistency argues for it. `/unzip` already works exactly
this way — preview by default, an Archives settings toggle, `-y` to skip once — so Tidy is now the
shape the same user already knows rather than a second one to learn. The precedence rule is copied
from `ShellViewModel.Archive.cs`: a switch on the command line wins for that run, otherwise the
setting decides, and a control changed inside the surface never writes the setting.

The category ticks are **per run and never persisted**. A remembered category choice would quietly
make Tidy do less than the user expects, for a reason set days earlier and no longer on screen. Only
the preview toggle is stored.

The "Don't show this again" tick is also added to the **archive** preview for symmetry, and in both
surfaces it is bound to the same durable setting that Settings exposes. Without the Settings copy the
tick would be a one-way door: once used, the surface carrying it never opens again, so the control
that would undo it can never be reached. Both ticks apply only when the operation is confirmed —
ticking and then cancelling changes nothing, because the user abandoned the whole action.

**2. Unknown file types go to `Other` rather than staying loose.** ARCHITECTURE.md Topic 5W said
"leave unknown/unclassified file types in place", twice. A folder with a dozen stragglers still loose
does not read as tidied.

The folder is named **`Other`**, not `Misc`: the other six are plain nouns, and `Misc` is a shortened
word (UX-DESIGN.md — "Readability Over Abbreviation").

**Reason, and the risk accepted:** part of Topic 5X's argument for needing no confirmation was that
Tidy only touches files it is sure about. Moving unknown types weakens that leg — but decision 1
above restores a preview, so the two changes settle each other. Nothing is destroyed either way: the
files land in a clearly named folder beside the originals, and the plan lists them before anything
moves.

**Two rules are not extension lookups and outrank the table:**

- A file that is still downloading is **never** moved — `.crdownload`, `.part`, `.partial`,
  `.download`, `.opdownload`, `.tmp`. Moving one breaks the transfer in progress. It is reported as
  "still downloading" rather than swept into `Other`.
- A file with **no extension at all** is left alone. The owner's decision covers unknown *types*, not
  unidentifiable files.

**Classification calls the owner settled:**

- `.iso`, `.img`, `.vhd`, `.vhdx`, `.dmg` → `Archives`. A disc image is a container of packaged
  contents.
- **Project files follow their medium**: `.psd`/`.ai`/`.xcf` → `Photos`, `.prproj`/`.veg` → `Videos`,
  `.flp`/`.aup` → `Audio`. A project file with no obvious medium — `.blend`, `.sln` — is not forced
  anywhere and lands in `Other`.
- `.exe` and `.msi` are always `Installers`, including a portable application. In a downloads folder
  that is nearly always what an `.exe` is.

**Still true from Topic 5W, and unchanged:** loose files only; existing subfolders are left alone and
never descended into; a folder of the same name is reused rather than duplicated; a name collision is
skipped and reported, never overwritten; the result count is per run, not cumulative; and `/tidy`
appears in `/history` without being undoable in v1.

**A running tidy is owned by Files, not by its plan.** Esc or Back detaches the surface without
stopping the move, and a command-bar task strip keeps the title, progress, View, and Stop available —
identical to the archive treatment already shipped. The plan's own second button reads `Cancel` while
it is still a plan and `Stop` once the move is under way.

**Bare `/tidy` organizes the visible folder.** The spec's examples always name a target, but bare
commands acting on the current context are already the Filekin pattern — `/info` describes the
current selection, bare `/unzip` extracts what is selected — and the plan stands in front of it.

## 2026-08-27 — A Command That Began Writing Always Refreshes Files

**Decision:** the Files hierarchy is re-listed after any app-owned command that may have changed the
filesystem, including one that failed part way through a batch. Only a refusal that wrote nothing —
bad arguments, a missing target, a non-filesystem location — leaves the listing alone.

**Reason:** the previous rule re-listed only when the command reported affected paths. A batch such
as `/move a.txt b.txt c.txt out` that moved the first file and then failed on the second reports **no**
affected paths, because the failure escapes before any path is recorded. Files was therefore left
showing items that no longer existed, after an operation the user watched fail. A stale hierarchy
after a partial write is worse than a redundant re-list after a harmless one.

**Implementation:** `AppCommandResult` gained `TouchedFileSystem`. `Fail` leaves it false;
`FailedWhileWriting` — returned from the file-operation base class whenever an `IOException`,
`UnauthorizedAccessException`, or `SecurityException` escapes — sets it true, because those exceptions
can only be thrown once the command has started writing. The command bar refreshes on that flag
rather than on the affected-path count.

**Resolved follow-up, 2026-08-27:** `/copy`, `/move`, and `/toss` now isolate failures per target and
continue with unrelated work, as Topic 5Y requires. `AppCommandResult` distinguishes partial success
from full success and carries completed paths, completed relocations, and individual failures. The
command bar presents the partial result with the warning state (`⚠ 9 moved · 3 failed`), refreshes
Files, and rebases saved Locations for the moves that did complete. A batch in which every target is
invalid remains an error and does not claim a filesystem write. `/rename` remains the confirmed
single-target command, so it has no independent batch targets to isolate.

## 2026-08-27 — `/zip` Takes the Same Switches as `/unzip`, Minus `-noroot`

**Decision:** `/zip [-skip] [-overwrite] [-y] <item...> [name.zip]`. The three switches mean exactly
what they mean for `/unzip`, with the same precedence: a switch wins for that one command, otherwise
the Settings default applies, and a switch never writes the setting. `-noroot` is **not** added; it
describes where extracted files land, which is not a question compression asks, and it is refused by
name so that someone reaching for it hears why rather than getting a generic unknown-switch error.

This supersedes "`/zip` Is a Version-One Command With No Switches" and the `/zip` clause of "Archive
Preview Is the Default and Archives Is a Settings Subject", both from earlier the same day.

**Reason:** the owner asked for consistency, and one specific hole justifies it. The original
reasoning was that `/zip`'s preview makes every remaining choice visible, so switches would only
enlarge the command language. That holds for the collision choice, which the preview does show — but
it cannot hold for `-y`, whose whole purpose is to **not** show the preview. The preview cannot be
the place you go to skip the preview.

The hole was practical, not theoretical: one shared setting, `archives.previewBeforeExtracting`,
governs both commands. Wanting `/zip` to run without a preview while keeping `/unzip`'s was therefore
inexpressible — `/unzip` could opt out per command, `/zip` could only opt out globally, which dragged
`/unzip` with it.

Once `-y` exists, the collision switches follow necessarily rather than as decoration: skipping the
preview removes the only surface where that choice was visible, so a user who skips it needs some way
to state it. The three arrive together or not at all.

**Independent evidence the original decision was wrong:** since `8629d14`, `ZipCompressor` has
refused an existing archive with *"out.zip already exists. Use `-overwrite` to replace it."* — advice
no user could follow, because the parser answered `-overwrite` with *"not switches. Remove
-overwrite."* The application told people to type a switch and then refused that exact switch. This
change makes the message true.

**Note:** `/zip` already had complete collision behavior before this change — `ZipPlan.OutputExists`,
and a `ZipCompressor` that refuses on Skip and recycles the existing archive on Overwrite. Only the
command-line way to say it was missing. This decision exposes existing behavior; it does not add any.

## 2026-08-28 — `/where` Answers a Program's Footprint, and Only From Real Filesystem Paths

**Decision:** `/where <one query>` opens `Files · Where — <query>` immediately and fills it with real,
navigable filesystem locations grouped as executable, installation, user data, configuration, and
shortcut. Exactly one query is accepted; a name containing spaces must be quoted, and `@selection`
is refused by name rather than expanded into several searches.

**PRODUCT.md's `/where` list is narrowed on two points.** It offered "related processes" and
"relevant registry information" as possible results. Neither ships. A running process is not a
location, and it changes between the moment the view is drawn and the moment the user acts on it.
Registry keys are used — App Paths and the uninstall metadata are the fastest authoritative way to
find where a program was installed — but only as a clue to a real path. Filekin shows the path, never
the key. The rest of the list is implemented.

**Discovery is staged and bounded, never a whole-drive crawl:** registrations, Start Menu shortcuts,
the current Windows PATH folders, common install roots, then shallow current-user data and config
roots. Reparse points are not followed. One unreadable source or folder is counted and reported, and
never invalidates the rest. The scan belongs to the view, not to the command bar: the bar is usable
again as soon as the view opens, Stop cancels and keeps what was already found, and Back or Esc
cancels and closes.

## 2026-08-28 — A Friendly Program Name Learns Aliases Once, and Only From Paths

**Decision:** `/where "Visual Studio Code"` has to find `Code.exe` and `.vscode`, so a match may teach
the matcher other names to look for. Three rules bound that, and all three are load-bearing.

1. **Only a query-strength match teaches.** A registration or shortcut found *through* a learned
   alias never teaches another one. Without this the search widens on every hit.
2. **Names are learned from paths, never from display names.** An executable's own name and the leaf
   folder a program was installed into are learned; the display name is not.
3. **A short learned word must be an entire name; only a long joined name may match inside another.**

**Reason — measured, not theoretical.** The first implementation broke all three and was verified
against this machine's real registry and filesystem. `/where "Visual Studio Code"` returned **2862
locations in 20.7 seconds**, including Arturia, ASUS, Ableton, NVIDIA and VLC. The chain was exact:
the registration is named *Microsoft Visual Studio Code (User)*, learning from that display name
taught the alias `user`, `user` matched *NVIDIA User Container*, that taught `nvidia` and `framework`,
and those pulled in most of Program Files. `/where notepad` returned 186 locations including
`.vscode` and `Microsoft.WindowsStore`. After the three rules, the same queries return **12** and
**7** locations in well under a second, and every row belongs to the program asked for.

Regression tests cover each rule: an alias-reached registration must not widen the search, `Code Cache`
must not match a VS Code query, and publisher/architecture/folder-role words (`microsoft`, `amd64`,
`bin`, `application`, `user`) are never learned. A shortcut target teaches only when it is an
executable — a shortcut to `index.html` otherwise taught `index` and claimed every folder so named.

## 2026-08-28 — Filekin Edits the Real Windows User PATH, and Nothing Else

**Decision:** there is no Filekin-only PATH overlay. `/where`'s **Add to PATH** and the Advanced
settings editor both add to the current Windows **user** PATH, which is what Windows-native shells
already read. Filekin never writes machine PATH and never elevates.

Every write is optimistic and immediately undoable, and Undo restores the exact previous string.
Undo refuses if the value changed after Filekin's edit, so it can never erase newer external work.
Unrelated raw entries, including empty segments and `%VARIABLE%` references, are preserved verbatim.

**Filekin does not use `Environment.SetEnvironmentVariable` for this.** It writes `HKCU\Environment`
directly and announces the change itself, because the framework method has two measured faults, both
found by the owner questioning a claim rather than by any test.

*It destroys `REG_EXPAND_SZ`.* Windows normally stores PATH as an expandable value so an entry such
as `%USERPROFILE%\bin` resolves. `Environment.SetEnvironmentVariable` rewrites the value as plain
`REG_SZ` whatever it was before. Proven directly: a probe written as `REG_EXPAND_SZ` came back as
`REG_SZ` after one framework write, and its `%USERPROFILE%` stopped expanding. The text survives and
the meaning does not, so a test that only compares strings passes while every variable-based entry in
a user's PATH silently stops working. `WindowsUserEnvironmentWriter` reads the existing value kind
and writes it back; a brand new value containing `%` is stored expandable.

*Its announcement is slow for no reason.* Changing a user environment variable broadcasts
`WM_SETTINGCHANGE`, and the framework sends it without `SMTO_ABORTIFHUNG`, so every top-level window
that is not pumping messages costs the full one-second timeout. Measured on this desktop, which had
13 such windows:

| step | time |
| --- | --- |
| the registry write alone | 9 ms |
| `SendMessageTimeout` with `SMTO_ABORTIFHUNG` | 0.7 s |
| `SendMessageTimeout` without it, as the framework sends it | 15–16 s |
| `Environment.SetEnvironmentVariable(..., User)`, end to end | 17–20 s |

Filekin sends the same documented broadcast with `SMTO_ABORTIFHUNG` and a short timeout. A complete
add through `WindowsUserPathEditor` went from roughly 15–20 seconds to **431 ms**, and Undo to 400 ms,
with the value, the raw registry text and the value kind all restored exactly.

**The broadcast is still sent, and it is still true that a running terminal will not see the change.**
Those are not in tension. Most programs, `cmd.exe` and PowerShell included, ignore `WM_SETTINGCHANGE`
and keep the environment block they were started with — which is exactly why a new terminal is
needed. Explorer does listen and updates its own block, so programs launched from Explorer afterwards
inherit the new value. The broadcast exists for the listeners; the "reopen your terminal" advice is
what everyone else ignoring it looks like.

The write still runs off the UI thread and the surface still reports progress while it happens, since
neither cost is guaranteed to be small on an unknown desktop.

## 2026-08-28 — The Command-Folder Editor Is Add and Remove, With No Second List

**Decision, owner, superseding the earlier plan:** the Advanced PATH editor offers **add and remove
only**. There is no move-earlier/move-later control, and the read-only machine PATH list is **not**
shown at all.

This overrides two points of the original `/where` plan, which specified reordering and a machine
list "below as read-only context". Both were built, reviewed on screen, and rejected: three buttons
on every row made the page shout, and a second list of sixteen rows nobody can edit doubled the page
for no available action. The order of PATH entries is still honest — the list is shown in real search
order, earliest first — it simply is not editable here. Anyone who needs to reorder or to touch the
machine list has the Windows environment editor.

**The page states what it is.** The section is titled *Run a program by name in a terminal* and says
in its first sentence that these folders are the Windows user PATH variable, because a user who does
not know the term still needs to recognise the setting later, and a user who does know it needs to
know Filekin is not inventing a parallel one. Two subjects on one page — Filekin's settings file and
a Windows setting — are separated by a rule rather than run together.

Section titles across Settings moved from 11px faint to 13px semi-bold at full contrast: at the old
size they did not read as titles.

## 2026-08-28 — A Blocking Probe With a Deadline Gets Its Own Thread

**Decision:** `WindowsDrivesProvider` probes each drive on a `TaskCreationOptions.LongRunning` task
rather than `Task.Run`.

**Reason:** the probes call `IsReady`, `VolumeLabel` and the capacity properties, all of which block,
and the set is given two seconds so one dead network mapping cannot hold up `/drives`. On a
thread-pool thread that deadline measured the wrong thing: a probe that had not been *scheduled* yet
was reported exactly like a drive that had not *answered*. Under load — a parallel test run, or any
busy desktop — that showed the **system drive** as unavailable, which is both wrong and alarming.

This was recorded in HANDOFF.md on 2026-08-27 as a flaky test. It was not flaky; it was a real
defect that a busy machine reproduced. A dedicated thread per drive is the honest cost of putting a
wall-clock limit on a blocking call. The full suite passed three consecutive times afterwards, having
failed on most runs before.
