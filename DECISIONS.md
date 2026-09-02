# Decisions

This document records important product decisions and, more importantly, why they were made.

Decisions can be revisited as the product develops.

## Consolidated Product Decisions Through 2026-08-28

This section records current decisions by domain. Superseded discussion is intentionally omitted; Git
retains its chronology. Detailed behavior belongs in `PRODUCT.md`, `FEATURES.md`, and `UX-DESIGN.md`,
while implementation boundaries belong in `ARCHITECTURE.md` and `ENGINEERING-GUARDRAILS.md`.

### Identity, scope, and visual direction

- The product is **Filekin**, a free GPLv3 Windows file manager and terminal. Official builds stay free;
  community support may include donations without complicating licensing.
- Version one uses C#/.NET/WPF, self-contained installer and portable ZIP. Microsoft Store distribution
  and paid signing are not required; updates remain user-controlled.
- WPF is infrastructure, not visual identity. Filekin uses a compact terminal/developer-tool language,
  dark by default, with user-selectable light/system theme and accent palette.
- Avoid Explorer cloning, fake shells, hacker novelty, generic AI dashboards, decorative controls,
  speculative features, and abstractions without a present responsibility.
- The UI thread never owns expensive filesystem, provider, hashing, archive, or process work.

### Files, command language, and references

- Files and its persistent PowerShell command-bar runspace always share one filesystem location.
  Navigation updates the runspace; filesystem `cd` may update Files. Non-filesystem providers move to an
  independent PowerShell terminal rather than splitting the context.
- Slash commands are deterministic app actions, not PowerShell translations. Known `@` tokens are
  app-owned references; unknown ones pass through for shell semantics. No additional sigils or synonyms.
- The minimal built-ins include `@selection` and `@thisfolder`. Selection always means the full Files
  selection and never changes because a rich-view row has focus. Commands declare cardinality/type.
- `/` and `@` preprocessing exists only in the Files command bar. Terminal input is ordinary shell input.
- Completion is Tab-requested, transient, and limited to app slash commands and known references.
  Discovery comes from described completion rather than a required command palette.
- The command bar remains one line. Finite output uses transient status, small inline output, an
  expandable shell region, or a rich view according to structure; it is never a permanent console.
- App command views speak plain English. The newest result remains inspectable; there is no multi-result
  output buffer in v1.

### Terminal and shell lifecycle

- PowerShell is the guaranteed v1 Files shell behind an abstraction; shell switching must be explicit.
- Interactive shells and tools run in real ConPTY terminals. A terminal hosts PowerShell as root;
  children run inside it, child exit returns to PowerShell, and root-shell exit closes the tab.
- A terminal inherits the Files folder once, then owns independent process and working-directory state.
  Duplicate sessions are allowed; titles describe intent/location.
- Known interactive programs route immediately. Unknown shell commands remain shell-owned and may offer
  one fresh **Run in terminal** fallback after delayed evidence; no foreground process is migrated.
- Closing a live terminal asks first, attempts graceful shutdown, then bounded escalation. Closing
  Filekin ends hosted terminal roots; completed output may remain until its tab is closed during the
  session, but live processes do not survive restart.
- External terminals are externally owned. Sleep/hibernate is not exit; changing the launch folder does
  not kill an existing terminal.
- Filekin claims only its documented workspace/new-tab/close-tab shortcuts from focused terminal
  content. Ordinary Tab, arrows, Escape, Alt sequences, mouse reporting, Ctrl+C without selection, and
  provider interaction belong to the hosted program.
- Ctrl+C copies only with a terminal selection; Ctrl+Shift+C/V remain terminal-local. Renderer glyphs
  stay pinned to terminal cells.

### Navigation, surfaces, focus, and selection

- The sidebar is sparse: user-assigned Locations plus direct Filekin slash surfaces. It is not Quick
  Access, This PC, an Explorer tree, an expandable drive tree, or an active-sessions dashboard.
- Locations use readable ordered settings and `/location add|set|rename|remove`. App-owned move/rename
  retargets affected Locations; copy does not.
- `/places` is a short known-folder/cloud-root view. `/drives` shows connected drive-letter volumes and
  refreshes on volume changes. `/recent` and `/disk` are not v1.
- Files may later have nonvisual per-tab Back/Forward history, but no visible Explorer-style buttons are
  approved. Rich views dismiss before filesystem history and are not themselves history entries.
- GUI open remains Windows-familiar. A minimal app-owned context menu covers approved file actions;
  command capability is not rebuilt as nested menus.
- File hierarchy, temporary rich views, and persistent task tabs share infrastructure but retain
  different semantics and visual hierarchy.
- Rich views never own filesystem selection. They may navigate back to Files to establish it. Their rows
  show selection and keyboard focus separately and own a deliberate Tab cycle.
- Keyboard support is product behavior: arrows move local row/highlight state, Enter invokes the focused
  primary action, Space returns neutral non-text focus to the command bar, and Esc/Back dismisses the
  current surface according to its lifecycle.
- File rows use terminal-like type codes and sortable columns; only protected Hidden+System items are
  suppressed. The Files toolbar has no speculative view/refresh/favorite/overflow chrome.

### File operations, history, and recovery

- App-owned `/copy`, `/move`, `/rename`, and recoverable delete are deterministic. Delete's canonical
  command is `/toss` with `/trash` and `/delete` aliases; normal behavior uses the Windows Recycle Bin.
  There is no `/paste` requirement.
- Batch operations preserve independent successes and isolate conflicts. A command that began writing
  refreshes Files even after failure. Esc/Back skips unresolved items rather than rolling back completed
  work.
- Copy/move collisions offer Replace, Keep Both, or Skip, with compatible Apply to All. Replace is not
  assumed; Tidy skips collisions without interruption.
- Standard privilege is default. App-owned work may offer per-need Windows UAC retry. Locked targets
  offer Retry/Skip; read-only mutation offers Continue/Skip. Filekin does not force-unlock, kill owners,
  recreate Windows authentication, or provide ACL editing.
- `/history` is app-owned filesystem operation history, not command recall or a shell transcript.
  History persists; undoability is session-scoped. Retain the newest 50 top-level user operations.
- One invocation is one entry. `/undo` chooses the newest currently safe undoable operation; history rows
  may expose any currently safe current-session action. Safety is reevaluated before execution.
- Undo never silently overwrites. It uses exact app-owned evidence, retains partial pending work, and
  reports partial outcomes honestly. Copy/Tidy are informational; Move/Rename, exact Toss, and approved
  archive cases are session-undoable.
- Recycle Bin is a virtual Files location with local row actions. Its items and history rows are not
  ordinary Files selection.

### Confirmed commands and utilities

- `/run` is the only app-owned launcher; there is no `/open`. It resolves relative Files paths before
  PATH/PATHEXT, refuses folders, uses Windows association for GUI/documents, and opens console targets in
  terminals. `/ext` is the separate external escape hatch.
- `/go` navigates Files and accepts an unquoted remaining path, including spaces. Raw paths and ordinary
  shell commands otherwise preserve PowerShell parsing.
- `/info` is a rich field sheet: bare means selection, else folder. It uses the Windows Property System,
  labels the vendor field Company, computes expensive line count/recursive size on demand, reveals
  shortcuts while Windows edits them, and opens native Properties through `SHObjectProperties`.
- `/where` finds a program footprint from real paths and keeps `/find` distinct. Alias learning is
  bounded to strong path-derived evidence. The command-folder editor changes only real Windows user
  PATH, preserves expandable strings, broadcasts with a timeout, and shows one add/remove list.
- `/unzip` defaults to one predictable root folder and supports multiple archives plus explicit
  destination grammar. `/zip` uses matching switches except `-noroot`. Archive preview is default;
  replacement is recoverable; running work outlives the view; supported archive undo uses exact output
  evidence.
- `/tidy` is native deterministic C#, plans loose-file moves into seven categories, leaves subfolders,
  skips active downloads/collisions, and is not undoable. Bare `/tidy` shows a plan unless `-y` or the
  setting skips it. Legacy Desktop icon placement is excluded.
- `/recent`, `/disk`, and `/interactive` are not v1. AI is never required for deterministic filesystem
  work and has no approved filesystem-interpretation interface.

### Settings, storage, Windows, and packaging

- Ordinary configuration and ordered Locations live in readable `%AppData%\Filekin\settings.json` with
  safe writes/recovery. Secrets never do. Transactional history/coordination lives in SQLite `state.db`.
- Settings is a rich view with immediate application and no Save state. Categories own subjects, not
  individual controls. Startup Files location may be Home, a saved Location, or a folder with safe
  fallback when unavailable.
- Themes change palette only, not layout/type. Hosted terminal colors follow the theme; semantic status
  colors remain distinct from accent.
- Built-in interactive routing covers required tools; user rules may add program names but cannot remove
  built-ins.
- .NET handles ordinary filesystem work; selective Windows APIs own Recycle Bin, associations, known
  folders, UAC, environment broadcast, and Properties. Windows integration never dictates Explorer UI.
- Tidy reuses shared file-operation infrastructure. Installer and portable builds use the same
  self-contained payload. Portable distribution does not implicitly move user data beside the EXE.

## 2026-08-28 — Cooperative Agent Coordination Becomes the Active Phase

**Decision, owner:** pause `/history` and `/undo` implementation without changing their settled v1
design. Build provider-neutral coordination for Codex and Claude Code, validate it with real provider
interfaces, then add UI one checkpoint at a time.

## 2026-08-28 — Native Subscriptions, No Automatic Metered Usage

**Decision, owner:** Filekin runs each installed, unmodified provider tool through the user's own
subscription authentication. It stores no provider credentials and never silently selects API billing,
usage credits, reset credits, alternate endpoints, or cloud-provider billing.

Codex uses App Server with ChatGPT-managed authentication. Claude uses documented CLI background
sessions, status-line data, lifecycle hooks, and MCP after project-scoped validation proves first-party
Claude.ai authentication and refuses billing redirection. Any separately billed continuation pauses for
the user.

## 2026-08-28 — One Writer; Structured Coordination; Inspectable Memory

**Decision, owner:** exactly one agent owns the cooperative working-tree lease; the partner waits without
model prompts. Provider usage windows remain separate and guide safe selection/handoff without pretending
the next turn's cost is known. Unknown or unsafe state pauses rather than guessing.

Live participants, leases, usage, messages, and handoffs are app-owned transactional state exposed
through a project-bound MCP server. Markdown remains inspectable project memory and optional export.
Filekin previews any bootstrap and preserves existing `AGENTS.md`, `CLAUDE.md`, and skill resources.

A handoff does not release the lease. Only app-owned provider-stop proof may release or transfer it.
Parallel writing is outside the first slice and would require separate Git worktrees.

## 2026-08-28 — Reconciliation Precedes Coordination

**Decision:** `AgentCoordinationRuntime` persists restart reconciliation before preparation, MCP launch
configuration, or lease changes. Failed provider inspection cannot release a writer. The runtime owns
state sequencing but never dispatches a native turn; `AgentRunService` owns provider lifecycle.

Ordinary Filekin startup reconciles only. Coordination remains lazy and explicit; normal file-manager,
terminal, `codex`, and `claude` use does not initialize or opt into agent projects.

## 2026-08-29 — `/undo` and `/history` Have Different Reach

**Decision, owner:** `/undo` reverses the newest app-owned operation that is currently safe.
`/history` may offer Undo/Restore on any currently safe current-session row. Safety and dependencies are
reevaluated before showing or running the action; an unsafe operation remains visible with an
explanation.

If a `/zip` or `/unzip` output was edited, Undo offers Keep Edited (safe default), Recycle Edited, or
Cancel, with optional compatible Apply to All. It never silently destroys edits and records partial
outcomes honestly.

## 2026-08-31 — Low Allowance Is a Warning, Not an Unbreakable Wall

**Decision, owner:** each project may explicitly allow work on low/unknown included allowance. The
setting defaults off. Filekin still displays every provider window and requests handoff early; it never
buys or enables extra usage. An agent still must be present before receiving a turn.

## 2026-08-31 — Filekin Owns Why the Turn Moves

**Decision:** when Filekin requested a handoff, its pending reason wins over the agent's label. A blocked
agent may still submit useful handoff content. An unsolicited handoff is recorded as work completion.
User Stop never turns into a handoff even if one is supplied.

## 2026-08-31 — Folder Permission Scope Is Explicit

**Decision, owner:** setup offers three answers: **Use app settings** (default), **Plan / read-only**,
and **Trust (auto)**. The first sends no provider permission/sandbox choice. Plan / read-only starts
Claude in plan mode and Codex in a read-only sandbox. Trust (auto) starts Claude in auto mode and Codex
in a workspace-write sandbox scoped to the approved project folder. The recorded answer remains visible
and can be changed while no agent session is running. None is a bypass: Filekin never passes
`bypassPermissions`, answers approvals, or widens consent without asking again.

## 2026-08-31 — Start the Relay Partner on Demand

**Decision:** do not keep the second provider running idle. Start it only when a handoff names it.
Clock-in while another agent owns the turn is valid; duplicate clock-in by the lease owner and any state
overwrite remain invalid.

A normal completed turn returns the lease and leaves the project usable. Only a required handoff that
never arrives becomes `NeedsAttention`, cleared by an explicit user action after the lease is gone.

## 2026-08-31 — `/agents` Is the Folder-Bound Control Room

**Decision, owner:** `/agents` opens/selects one persistent `Agents · <folder>` task tab. An
unconfigured folder shows setup; a configured one shows its control room. Files remains permanent,
multiple project tabs may coexist, and closing one closes only presentation state—never the project,
provider, or lease.

The control room shows the cross-provider facts no CLI owns: objective, separate allowance windows,
presence, active lease, messages, handoffs, and lifecycle actions. It uses Filekin's compact
keyboard-first language rather than decorative AI dashboard chrome.

## 2026-08-31 — Initial Selection and Stop

**Decision, owner:** automatic start prefers the present agent with more safe allowance; the user may
choose one explicitly, and an unsafe explicit choice pauses rather than silently choosing another.
Work may begin with one agent.

Stop is cooperative, keeps the project, and becomes a resumable pause. Filekin does not force-kill an
agent to manufacture a clean state.

## 2026-09-01 — Context Persists Until the Provider Clears It

**Decision, owner:** handoff, completion, and a new objective do not silently clear provider context.
The provider's own `/clear`, typed in its attached CLI, deliberately clears that agent. Filekin's
internal saved-session-id reset is a different operation and has no UI until separately decided.

## 2026-09-01 — Find Unwatched Sessions

**Decision, owner:** when a provider can have a running session Filekin is no longer watching, Filekin
finds and handles it rather than telling the user to restart the app. The control room reports the
unwatched session and exposes Session/End when applicable. Provider inspection is rate-limited and runs
only when it can change the row.

This is currently Claude-specific by architecture: Claude background sessions outlive Filekin; a Codex
thread under Filekin's private App Server does not. Moving Codex to the shared daemon remains a separate
decision.

## 2026-09-01 — Controls State Their Current Action

**Decision, owner:** control-room labels, status text, enablement, tooltip/help text, and visuals agree.
Unavailable controls are visibly dimmed, use disabled text, and lose the pointing-hand cursor. **End**
is enabled only for a live session.

Superseded in part on 2026-09-01 by *Every Row States Two Facts* below: one shared **Resume CLI** label
hid that the two providers do opposite things, so the CLI control now names the action it will perform.

There is one start action, not competing Start/Continue buttons. Its current label explains whether it
will start, continue, or require a new objective, and its implementation decides from authoritative
project/provider state rather than making the user solve lifecycle bookkeeping.

## 2026-09-01 — Every Row States Two Facts

**Decision, owner:** an agent row answers two separate questions and never merges them. **CONNECTION**
says whether the tool is running (`Running`, `Not connected`, `No answer`); **WORK** says what the job
is doing (`Not started`, `Stopped`, `Waiting`, `Working`, `Handing over`, `Finishing`, `Done`,
`Needs you`, `Stopping`). Being present without a turn and waiting for one read the same, so both are
`Waiting`; the status sentence uses these words too.

`Done` is the finishing agent's fact, not the project's. Painting a finished job across every row made
an agent that never took a turn claim the work. An agent is therefore `Not started` until it holds the
turn on the objective in hand, `Stopped` once it has held one and is not running, and `Done` only when
its own turn completed. That needs a persisted per-agent fact, cleared by a new objective, because a
saved conversation is memory of any job in that folder and cannot answer it. A single column had to omit one of them, so a finished or stopped agent read
as though it were still connected. Running is running: it does not matter whether Filekin started the
session, is watching it, or was closed while the tool carried on.

Running is therefore read from live sessions — one Filekin holds, one open in a terminal tab here, or
one the tool reports for itself — never from stored connection state, which can outlive the window that
wrote it.

The CLI control names its current action: **Open CLI** while a session is running, **Resume CLI** only
where resuming is genuinely possible, and **Go to CLI tab** when this window already has that CLI open.
Resuming is offered only for an unfinished job whose session has run in this Filekin window: closing a
CLI stops the work and the person can pick it back up, while a freshly started Filekin has run nothing
and offers nothing to reopen. There, **Continue** is the way back in, and it carries the same saved
conversation on. A promise to resume that Filekin cannot keep is what made a disconnected agent look
like one whose work was still waiting. Claude never offers to resume: it can only be opened while it
is running.

The start action follows the same rule. **Continue** is shown only while a session is running, because
"continue" with nothing running left a person asking what there was to continue. With nothing running
it says **Start work**, which still carries a saved conversation on, and says so in its help text.

## 2026-09-01 — The Terminal Is the Session

**Decision, owner:** remove the custom Agent Session transcript UI. The provider-specific CLI action
opens the exact native provider session in a specially marked ordinary Filekin ConPTY terminal tab:

- Claude resolves the live background handle and runs `claude attach <id>`.
- Codex runs `codex resume` with the project MCP overrides only when Filekin's App Server no longer
  owns the thread; a second client on a live thread is refused.

The provider CLI owns transcript, questions, approvals, input, and `/clear`. Filekin does not parse VT
output, synthesize input, or maintain a second weaker transcript. The provider-neutral event/service
transport may remain for headless relay lifecycle, but it has no custom app screen.

Closing the terminal ends its root shell after normal confirmation; a Claude background session can
remain alive, so **End** asks Claude to stop cooperatively. A resumed Codex CLI is the provider process;
it is registered as the existing worker, and **End** closes that exact terminal because Codex has no
separate cooperative session-stop command. When Filekin closes, it asks providers
what remains live across saved projects, treats an unknown answer honestly, and offers to keep agents
running, end agent sessions, or cancel. Terminal sessions end with the window.

When the provider CLI returns to PowerShell, the tab remains open but stops being an active agent
session. Filekin learns this from an internal command-completion signal, never by parsing provider
output, and immediately reconciles the control room.

**Reason:** the provider already supplies the complete interactive frontend, while Filekin's unique
value is cross-provider coordination. One session should have one authoritative interaction surface.

## 2026-09-01 — Activity History Is an Inline Disclosure

**Decision, owner:** the Agent Control Center uses one vertically scrolling page. The historical
**Activity log** sits at the bottom as an inline disclosure, collapsed when a project tab first opens.
Expanding it lengthens that same page; it does not open a modal or introduce a nested scrollbar, and
new events do not force it open. The disclosure state may be remembered per project tab for the life
of the window. Current project status remains a separate, always-present fact above the controls.
