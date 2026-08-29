# HANDOFF.md — Filekin

## Purpose

This is the short live state shared by coding agents: current phase, immediate next task, genuine
blockers, standing contracts, and current known problems. Git and `HANDOFF-ARCHIVE.md` hold finished
session history. Do not turn this file back into a changelog, test ledger, or implementation diary.

Read `AGENTS.md` and `ENGINEERING-GUARDRAILS.md` before this file, then read the master specifications
relevant to the task. Keep this file comfortably under 500 lines.

## Current state

Filekin is in production implementation, one confirmed v1 surface at a time. The public repository is
`https://github.com/mefisme/filekin`; `main` is the default branch. The repository owner is an admin
bypass actor for the protected-branch rules, so a direct push can succeed while GitHub reports
`Bypassed rule violations`.

Implemented product areas include:

- the Files hierarchy and its persistent PowerShell command bar;
- ConPTY-backed terminal tabs;
- `/recycle`, `/places`, `/drives`, `/settings`, `/info`, `/unzip`, `/zip`, `/tidy`, and `/where`;
- `/copy`, `/move`, `/rename`, `/toss` (`/trash`, `/delete`), `/go`, `/run`, `/ext`, and `/location`;
- command completion, `@` references, saved Locations, themes, archive/tidy preferences, interactive
  tool rules, and the Windows user-PATH editor.

The latest app UX checkpoint is commit `b72c90e` (`fix(app): refine where and keyboard navigation`),
pushed to `origin/main`. Cooperative coordination now has committed Core, persistence/MCP, provider
inspection, and app-runtime foundations through `f7d94e`. Durable app conclusions are:

- `/where` discovery, PATH editing, drive probing, progressive results, focus, and row actions were
  reviewed and remediated. The matcher bounding rules under **Standing contracts** remain load-bearing.
- Space returns focus from non-text Files/rich-view controls to the command bar. Enter remains the
  primary action. Recycle, Places, Drives, Tidy, and Where have explicit keyboard behavior; Tidy rows
  show selection while moving with Up/Down and its buttons are tabbable.
- Natural Window-level Tab traversal includes the sidebar and top-level controls. Sidebar Up/Down only
  changes its highlight; Enter opens the highlighted Location or slash surface. It must not navigate
  merely because the highlight moved.
- Clicking the path immediately left of the command bar copies the current Files path. About is a real
  button. Text selection uses the accent without hiding the selected text.

## Immediate next task — cooperative agent coordination

The owner paused `/history` and `/undo` and made subscription-backed Codex/Claude coordination the
active development phase. History/undo remains confirmed v1 work; its settled specification is
preserved below and resumes when the owner returns to it.

### Settled product boundary

- An agent project is bound to one folder and initially supports Codex plus Claude Code.
- Each installed native tool authenticates directly to the user's own subscription. Filekin does not
  require paid model API keys, store AI credentials, purchase extra usage, or consume reset credits.
- A provider prompt to switch from included allowance to API billing or usage credits pauses for the
  user. Filekin never enables or confirms metered overage automatically.
- Both agents clock in. Only one owns the working-tree lease and active turn; the other waits without
  receiving model prompts.
- Filekin, not either model, reads non-secret provider usage state. Codex uses App Server account rate
  limits. Claude Code uses status-line five-hour/seven-day fields after the first response populates
  them. Missing data is `unknown`, never zero.
- Completion and budget handoffs are cooperative. Filekin requests a safe stop and structured handoff
  while allowance remains; it does not kill an agent or auto-approve a destructive/security prompt.
- If both agents are low, logged out, usage-unknown when no safe choice exists, awaiting approval, or
  otherwise blocked, the project pauses visibly.
- Codex reads `AGENTS.md`; Claude Code reads `CLAUDE.md`; both may reference shared project context and
  shared skill resources. Bootstrap changes are previewed and never silently overwrite existing files.
- Live leases, messages, budget snapshots, and handoffs are app-owned transactional state. MCP is the
  shared coordination surface. Markdown remains inspectable project memory and optional export.
- Plugins may package provider-specific wrappers around shared skills/scripts/MCP. Connector accounts
  retain their own authentication, permissions, prices, and limits; they are not AI subscription auth.

### Implementation order

1. **Implemented foundation:** provider-neutral Core models and `AgentProjectCoordinator` transitions
   cover clock-in, separate usage windows, freshness/safety selection, one-writer leasing, targeted
   messages, cooperative handoff, missing-handoff attention, blocking, completion, and restart lease
   invalidation. The safety threshold is an explicit policy input, not a hidden product default.
2. **Implemented Codex transport foundation:** the local App Server client proves ChatGPT-managed
   authentication, separate account rate-limit windows, thread start/resume, turn start, and
   turn-completed parsing. A native ephemeral thread start has been verified without sending a model
   turn; normal Codex approval and sandbox configuration remains authoritative.
3. **Implemented Claude inspection foundation:** the native CLI client confirms Claude.ai auth without
   retaining account identity, rejects inherited environment-selected API/cloud/gateway billing,
   parses documented status-line five-hour/seven-day windows, and lists structured background-session
   lifecycle and blocked states. Usage remains unknown before the first model response. Current Agent
   View documentation provides an explicit shared-checkout opt-out through
   `worktree.bgIsolation: "none"`; Filekin must preview that choice and preserve its one-writer lease.
4. **Implemented persistence and MCP boundary:** `SqliteAgentProjectStore` owns schema-versioned
   transactional project, participant, usage-window, lease, message, and handoff tables in app-owned
   `state.db`. Transactional updates reserve the SQLite writer before reading so separate MCP processes
   cannot lose each other's changes. Restart reconciliation persists lease invalidation. The
   project-scoped `Filekin.Mcp` stdio executable exposes only clock in, read state, message,
   submit/accept handoff, and report blocked/completed; its project/provider identity is fixed at
   process launch and its structured output omits native session identifiers.
5. **Implemented Claude paid-billing refusal safeguard:** before any Claude CLI process starts, the
   project-scoped adapter checks inherited billing/auth/provider variables and the applicable user,
   shared-project, and project-local Claude settings, honoring `CLAUDE_CONFIG_DIR`. Provider selectors,
   credential/endpoint/profile/federation variables, and `apiKeyHelper` cause a refusal. The streaming
   inspection decodes names but never credential values, clears its temporary byte buffer, and fails
   closed on unreadable, malformed, or oversized settings. The CLI must then independently report
   Claude.ai first-party authentication.
6. **Implemented app-owned coordination runtime:** `AgentCoordinationRuntime` persists restart
   reconciliation before permitting project work, refreshes provider facts, records unavailable
   providers without mistaking inspection failure for a stopped writer, creates immutable MCP launch
   identities bound to the project/provider/actual `state.db`, and applies initial selection,
   handoff requests, and provider-confirmed stop transitions transactionally. It does not dispatch
   native turns or define the provisional shared session adapter. A token-free stdio integration
   proves Codex-identity message persistence and Claude-identity pickup without invoking either model.
7. **Implemented narrow Claude background adapter:** the opt-in adapter wraps the user's unmodified,
   separately installed `claude --bg` CLI. It preflights the billing refusal guard plus native
   Claude.ai/first-party auth, supplies only Filekin's fixed project MCP server, preserves normal
   permissions, parses native launch/lifecycle state, and verifies that Agent View reports the canonical
   shared checkout. The `worktree.bgIsolation: "none"` override is an in-memory `--settings` value; it is
   previewable and requires explicit project consent but never writes Claude settings. A failed checkout
   validation requests a native stop and exposes cleanup failure for manual review. Process-boundary
   tests use a fake CLI and consume no provider tokens.
8. **Exact next task — disposable native relay:** after explicit owner approval to spend normal
   subscription usage, run one deliberately small Codex → Claude → Codex coordination relay in a
   disposable project. Prove native session launch, shared-checkout binding, normal permission blocking,
   MCP message/handoff pickup, lifecycle stop evidence, lease transfer, and no concurrent writers. Stop
   and record exact evidence or failures before building UI, bootstrap preview, broader workspace reads,
   plugins/connectors, or additional providers. Do not use `bypassPermissions`, `-p`, the Agent SDK, API
   billing, terminal injection, or screen scraping.

### Standing implementation contracts

- `Filekin.Core` contains no WPF, provider SDK, process, JSON-RPC, or MCP implementation types.
- Provider responses become provider-neutral immutable snapshots at the infrastructure boundary.
- Keep separate usage windows separate; do not invent a universal quota or predict next-turn cost.
- A provider stop event without a structured handoff becomes `NeedsAttention`; never activate the
  partner with guessed context.
- An MCP handoff/completion report does not prove the provider stopped and therefore does not release
  the writer lease. Only the app-owned provider lifecycle transition can release or transfer it.
- The working-tree lease is cooperative state, not an OS lock. Parallel writing is excluded from the
  first slice; a future parallel mode requires separate Git worktrees.
- `state.db` agent schema version 1 is normalized rather than one serialized state blob. Preserve
  `PRAGMA user_version` migration checks and the writer-reservation-before-read rule.
- MCP processes receive one project GUID and provider identity at launch. They must not accept either
  identity from tool calls, expose native session identifiers, or run restart reconciliation on
  startup. Reconciliation belongs to the app before it starts new coordination activity.
- `AgentCoordinationRuntime.StartAsync` must complete persisted restart reconciliation before project
  preparation, MCP launch configuration, or lease changes. Provider refresh precedes selection; a
  failed refresh records `Unavailable` but never releases an active writer. MCP configurations are
  inert values and do not start providers. Ordinary Filekin startup performs reconciliation only and
  must never dispatch an agent or request shared-checkout consent.
- Agent edits are external filesystem activity and do not enter Filekin `/history` merely because
  Filekin coordinated the agent.
- Keep normal interactive terminal tabs unchanged. Agent coordination must not intercept ordinary
  terminal keys or depend on VT-screen scraping.
- Agent coordination is lazy and strictly opt-in. Plain file-manager/terminal use must not initialize
  agent-project state, probe Codex or Claude, start MCP/provider processes, show AI setup/consent, or
  reinterpret ordinary `codex`/`claude` terminal commands as coordinated projects.
- Store no secrets in `settings.json`, SQLite, project files, logs, or handoffs.
- Claude inspection is bound to the agent-project folder and refuses before process launch when inherited
  variables or applicable user/shared/local settings could select separately billed authentication. It
  honors `CLAUDE_CONFIG_DIR`; settings parsing decodes names only, clears temporary bytes, and fails closed.
- Claude coordination runs only Anthropic's unmodified native binary. The user installs it and signs in
  through Anthropic's own flow; Filekin never bundles it, handles credentials, intermediates billing,
  removes an authentication method, or implies Anthropic endorsement. Background dispatch is opt-in.
- Shared-checkout consent belongs behind the explicit future command/action that creates or enables one
  Filekin agent project. It never appears during ordinary Filekin startup. The future setup UI may
  persist that consent in Filekin's transactional state and reuse it for that project's coordinated
  sessions; each adapter launch must still receive programmatic evidence of consent. Filekin passes the
  setting inline and never writes `.claude/settings*.json` merely to enable coordination. The exact
  command name remains an owner decision.
- Treat each recorded implementation task as a separate owner checkpoint. Complete one task, update
  this handoff with the exact next task, report, and stop. If the owner says to stop mid-task, update
  this handoff with the precise completed state and resume point before ending the turn.

### Current non-blocking product questions

- What command or setup action creates/opens an agent project?
- How does a user attach coordination to an existing project as-is, and which optional bootstrap files
  are proposed without modifying or replacing its current agent instructions?
- Can the user provide the opening work prompt directly, and if so how is it combined with Filekin's
  coordination contract and delivered to whichever agent is selected first?
- What conservative handoff threshold should ship after live validation?
- Is readable handoff export always written or optional?
- Which plugin/connector management comes after the first relay?

These do not block the Core coordinator or provider spikes. Do not invent their UI while building the
foundation.

### Claude subscription and background conclusion — no development blocker

The former blanket policy/worktree blocker is stale against Anthropic's current official documentation.
Its Claude Code legal guidance now explicitly allows a platform to run the unmodified binary while each
end user authenticates with their own subscription or provider credential and is billed directly under
their own agreement. The platform must not modify Claude Code, remove native authentication choices,
pay for/resell/intermediate usage, collect credentials, or imply Anthropic endorsement. Filekin is a
free, noncommercial GPLv3 local application and its confirmed architecture satisfies those technical
conditions: users separately install and authenticate Claude Code, and Filekin rejects separately billed
overrides without reading secrets. The Help Center also explicitly recognizes third-party app, Agent SDK,
and `claude -p` usage drawing from subscription limits under the currently paused billing change.

`claude --bg` is an official native background-session interface, carries MCP configuration, and uses
the normal Claude Code authentication and permission model. Agent View now documents
`worktree.bgIsolation: "none"` for an explicitly shared checkout. That setting removes Claude's automatic
worktree protection, so Filekin must make it an informed opt-in, verify the session checkout, and enforce
its cooperative one-writer lease. These sources are sufficient to continue implementation and local
testing. Commercial-Terms wording for products remains a public-release compliance note rather than a
code blocker; do not invent a paid API fallback or weaken the native-binary/own-account boundary.

The owner posted the concrete Filekin architecture/permission question through Anthropic's official
Discord on 2026-08-29. Anthropic's automated system escalated it to a human support agent who will reply
by email. The private conversation identifier is retained by the owner, not this public repository. The
pending response is useful supporting evidence but does not pause the narrow adapter work. When it
arrives, preserve the exact substantive answer, author/official role, and date here without publishing
private account or support identifiers; do not turn an anonymous community opinion into an Anthropic
policy decision.

### Phase-zero done means

- Core coordinator transitions and safety rules are exhaustively testable without either vendor tool.
- Codex and Claude adapter spikes report supported, unsupported, auth, usage, and lifecycle states
  honestly and never select paid API billing implicitly.
- A restart cannot retain an unverified stale writer lease.
- The MCP coordination vocabulary and persistence model are fixed by tests.
- One real subscription-backed round trip hands useful work Codex → Claude → Codex without concurrent
  writes, credential access, terminal screen scraping, forced termination, or automatic approvals.

Authoritative implementation evidence:

- `https://learn.chatgpt.com/docs/app-server`
- `https://learn.chatgpt.com/docs/pricing`
- `https://code.claude.com/docs/en/cli-usage`
- `https://code.claude.com/docs/en/configuration`
- `https://code.claude.com/docs/en/env-vars`
- `https://code.claude.com/docs/en/statusline`
- `https://code.claude.com/docs/en/hooks`
- `https://code.claude.com/docs/en/sessions`
- `https://code.claude.com/docs/en/agent-view`
- `https://code.claude.com/docs/en/worktrees`
- `https://code.claude.com/docs/en/legal-and-compliance`
- `https://support.claude.com/en/articles/15036540-use-the-claude-agent-sdk-with-your-claude-plan`
- `https://github.com/modelcontextprotocol/csharp-sdk`

## Paused v1 task — `/history` and `/undo`

Build the durable app-owned filesystem operation journal and its two v1 commands. Read PRODUCT.md
**Visible Operation History**, FEATURES.md **`/undo`** through **Narrow Undo Scope**, UX-DESIGN.md
**Operation History UX** through **Undo Conflict UX**, ARCHITECTURE.md **Current Topic 4**, and the
corresponding confirmed entries in DECISIONS.md before implementation.

### Settled behavior

- `/history` is a Files rich view, not terminal text and not command-bar recall. It shows what Filekin
  changed, when, useful source/destination context, outcome, and current reversibility.
- History survives restart. Undoability does not. On startup, every prior-session row is informational.
- Retain the newest **50 user-level operations**. ARCHITECTURE.md's later `state.db` paragraph still
  says “approximately 100”; that is stale against PRODUCT/FEATURES/UX/DECISIONS and the existing
  `InMemoryOperationJournal`, all of which converge on 50.
- **One invocation is one top-level entry**, even for a bulk command or `/unzip a.zip b.zip c.zip`.
  Per-item successes and failures belong inside that entry. This is already specified and is not an
  owner question.
- `/undo` reverses the most recent app-owned operation that remains safely undoable. It has no v1
  count, id, force flag, `@last`, redo, or arbitrary shell-command support.
- Record only mutations Filekin actually performed. Partial commands with successful mutations are
  one accurately described entry. Ordinary PowerShell and hosted-terminal commands are excluded.
- Expected operation treatment:
  - move and rename: history plus session undo;
  - `/toss`: history plus Restore only when the exact Windows Recycle Bin item is reliably known;
  - copy: history, informational in v1;
  - tidy: history, never undoable in v1;
  - zip/unzip: history plus their existing session undo;
  - PATH/settings edits: remain outside the filesystem operation journal.
- Undo never silently overwrites. A destination collision offers Replace, Keep Both, Skip, or Cancel
  Undo; bulk conflicts may add Apply to All. Replace is never the default. Partial undo is recorded as
  partial, not as a successful full reversal.
- Use transactional SQLite `state.db` for durable journal state. Keep ordinary settings in readable
  `settings.json`.
- `/history` must follow the existing rich-view keyboard contract: visible selection/focus, Up/Down
  row traversal, Tab access to row actions and Back, Enter for the focused primary action, Space back
  to the command bar, and Esc back to Files.

### Resolved archive-edit safety

If a file created by `/zip` or `/unzip` changed after Filekin wrote it, Undo pauses and clearly asks
whether to:

- **Keep the edited file** and continue with a partial Undo (safe default); or
- **Move the edited file to the Recycle Bin** and continue Undo.

Cancel Undo remains available, and a bulk conflict may offer Apply to All. Filekin must record enough
output metadata to detect the edit and must report the final partial/full result accurately. It never
silently permanently deletes an edited output. Because undoability is session-scoped and payloads are
opaque JSON, this payload detail does not require migrating prior-session undo data.

### `/undo` command versus `/history` row actions

These intentionally have different reach:

- `/undo` is the fast command and reverses the latest app-owned action that is still safely undoable.
- `/history` exposes Undo/Restore on **every individual current-session action that Filekin can still
  reverse safely**, including older actions. That per-row recovery is a central purpose of the rich
  history view, not something restricted to the newest entry.

Before showing or executing a row action, Filekin must evaluate that operation's present safety and
dependencies. If later filesystem activity makes it unsafe, the row remains in history but its action
is unavailable and the view explains why. Never offer an action merely because the row was undoable
when first recorded.

### Existing seams and implementation traps

- `Filekin.Core/Operations/JournalEntry.cs`, `IOperationJournal.cs`, and
  `InMemoryOperationJournal.cs` are the current platform-neutral seam. The existing entry cannot
  distinguish “never undoable,” “undoable now,” “undone,” “failed undo,” and “partial undo”; extend
  the model explicitly instead of overloading one Boolean.
- `ShellViewModel.Archive.cs` owns today's in-memory zip/unzip journal and result-line Undo. Preserve
  that fast action while routing it and `/undo` through one authoritative journal/undo coordinator.
- Multi-archive unzip currently records inside its per-archive loop. Move recording outside the loop
  so the confirmed one-invocation/one-entry rule holds.
- `AppCommandResult` already carries affected paths, relocations, failures, and whether the filesystem
  was touched. Wire app-owned file commands at the common dispatch/result boundary rather than adding
  unrelated recording code to every view.
- A failed command that changed nothing gets no entry. A failure after partial writes must refresh and
  record the mutations that actually happened; do not invent detail the result does not contain.
- A `/toss` history payload must identify Filekin's own recycled item reliably enough that Restore
  cannot choose an older unrelated item with the same original path.
- SQLite work stays off the WPF UI thread and uses transactions for record/status changes and pruning.
- There is no `Filekin.App` test project. Keep journal state transitions, retention, serialization,
  undo selection, and dependency rules in testable Core/Infrastructure types; use only focused manual
  WPF verification.

### Done means

- The resolved archive-edit behavior and the distinct `/undo` versus per-row `/history` behavior are
  recorded in DECISIONS.md and reflected in the implementation.
- The SQLite store survives restart, prunes transactionally to 50 top-level entries, and strips all
  previous-session undo promises on startup.
- `/history`, `/undo`, result-line archive Undo, and row actions share one authoritative state model.
- Move, rename, toss/restore where reliable, copy, tidy, zip, and unzip follow the settled treatment
  above, including bulk and partial-success cases.
- Undo collision choices and partial outcomes are represented accurately.
- Tests cover persistence, restart demotion, pruning, one-entry bulk behavior, undo ordering,
  successful/failed/partial undo, and corrupt/unavailable database handling.
- Release build, desktop tests, format verification, and `git diff --check` pass. Do not overtest with
  LiveView or automate foreground input unless the owner explicitly asks; the owner will perform the
  final interaction pass. Always rebuild the normal Release executable for handoff.

## Navigation decision

Files Back/Forward is **not** the next task. The owner does not want visible Back/Forward buttons in
the Files hierarchy because that chrome feels too much like File Explorer. Do not add them.

The specifications still describe nonvisual per-Files-tab Back/Forward history. Whether Alt+Left,
Alt+Right, and mouse XButton navigation should eventually exist without visible buttons is deferred
and requires a separate owner confirmation. Do not mix it into `/history` or `/undo`; operation
history and filesystem navigation history are different systems.

## Remaining v1 scope after history/undo

- File context menu and file clipboard workflow (Open, Rename, Copy, Cut, Copy Path, Delete,
  Properties). The current-path copy button does not copy a selected file's path.
- Complex-operation previews, interactive collision handling, UAC elevation, and locked-file flows.
- `/find` needs its own product discussion; it is deliberately distinct from `/where`.
- Task tabs, terminal panes, virtual Files locations beyond Recycle Bin, folder sizes, preferred
  external terminal, and stronger contextual terminal names.
- Accessibility exposure for Files/sidebar rows and terminal text.

Deliberately not v1: `/recent`, `/disk`, and `/interactive`. AI-assisted filesystem interpretation
has no approved interface; do not invent one.

## Standing contracts — do not change without an owner decision

### Keyboard and focus

- From a focused hosted terminal, Filekin claims only Ctrl+Tab / Ctrl+Shift+Tab, Ctrl+Shift+T, and
  Ctrl+Shift+W. Ordinary Tab, arrows, Escape, Ctrl+C without a selection, and Y/N belong to the shell.
  Ctrl+Shift+C/V remain terminal-local; Ctrl+C copies only when a terminal selection exists.
- Space returns non-text Files/rich-view focus to the command bar. Enter is the primary action.
- Sidebar Up/Down highlights without navigation; Enter navigates. Esc returns to Files.
- Terminal tab headers are not yet keyboard focusable, and Left/Right does not move between them.
  If implemented later, handle arrows only while a tab header has focus; never intercept terminal
  content arrows.
- Rich-view lists are not WPF focus scopes. Rows need both selection and keyboard-focus visuals, and
  their visible view owns a deliberate Tab cycle so focus does not fall into hidden Files content.

### Files, shell, and Windows

- `/run` is the only launch command; there is no `/open`. It resolves relative paths from Files, then
  PATH/PATHEXT. Folders are refused. GUI/document targets launch through Windows; console targets use
  a terminal. `/ext` remains separate.
- Known `@` references beat PowerShell splatting. Unknown `@` tokens pass through. Terminal tabs get
  no `/` or `@` preprocessing.
- Location management is the sidebar plus `/location add|set|rename|remove`; do not reopen its grammar.
- `WindowsUserEnvironmentWriter` writes HKCU Environment and broadcasts with `SMTO_ABORTIFHUNG`.
  Do not replace it with `Environment.SetEnvironmentVariable`, which destroys `REG_EXPAND_SZ` and can
  block for many seconds on non-pumping windows.
- `/info` is a field sheet using the Windows Property System, not a folder listing or per-format parser.
- Sidebar is not an Explorer tree. Do not add Quick Access, This PC, automatic special folders, an
  expandable Drives tree, or speculative navigation chrome.

### `/where`

Only a Query-strength match may teach aliases. Learn names from paths, never display names. A shortcut
target teaches only when it is an executable. A short learned word must match a complete name; only a
joined name of at least six characters may match inside another. Never learn publisher, architecture,
or folder-role words; cap the alias set. Scan cache/extension/plugin folders only beneath an already
matched program directory, never as roots. These rules prevent an unbounded whole-machine cascade.

### Architecture

- `Filekin.Core` never references WPF.
- Terminal layering remains raw bytes in `ITerminalSession`, VT state in `TerminalEmulator`, drawing
  and input in `TerminalControl`, session state in `TerminalTabViewModel`, collection/selection in
  `ShellViewModel`, and focus/confirmation in `MainWindow`.
- ConPTY input/output channels are serviced independently. Drain output through teardown and close the
  pseudoconsole only after graceful shutdown attempts. A plain text control is not a terminal.

## Current known problems

- Accessibility is the largest quality gap: Files/sidebar automation names expose view-model type
  names, and the terminal grid is not exposed as useful text.
- No `Filekin.App` test project exists. App-only focus and visual behavior need a small manual pass;
  platform-neutral logic belongs in Core where it can be tested.
- Real Recycle Bin round-trip tests are marked `RequiresInteractiveShell` and intentionally filtered
  from hosted CI. Do not weaken them or infer capability at runtime.
- Terminal selection is drag-only; there is no word/line click selection or Shift-click extension.
  Tab overflow is unresolved, and tab headers lack Left/Right keyboard navigation.
- Files selection is not preserved across re-sort. Esc stops a running command only while the command
  bar has focus.
- `/drives` can show drive-letter volumes, not MTP devices. Reconnecting network mappings may require
  window reactivation before refresh.
- Command classification tokenizes on whitespace and is not quote-aware, although raw input still
  executes unchanged.
- Recycle Bin Restore/Delete verb matching is English-only.

## Validation

```text
dotnet build Filekin.sln -c Release -m:1 --no-restore
dotnet test Filekin.sln -c Release --no-build --no-restore -m:1
dotnet format Filekin.sln --verify-no-changes --no-restore
git diff --check
```

CI excludes `TestCategory=RequiresInteractiveShell`; the desktop suite does not. A running Filekin
instance locks the normal Release output, so ask the owner to close it rather than killing an unknown
instance. For the current phase, rebuild the normal Release executable and let the owner perform the
final UI interaction pass.

## Other open product questions

These do not block `/history` and `/undo`:

- Should the Files command-bar runspace load the user's PowerShell profile or remain clean/predictable?
- What terminal text should be exposed to assistive technology in v1?
- How should a selected file path be copied before the context-menu/clipboard work exists?
- How should terminal-tab overflow work?
- Should hosted terminal profile loading become a user setting?
