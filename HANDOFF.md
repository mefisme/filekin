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
inspection, and app-runtime foundations through `5495fa1`. Durable app conclusions are:

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
- Terminal scrollback metrics flow one-way from `TerminalControl` to its scrollbar; only an explicit
  scrollbar `Scroll` event changes the viewport. Do not restore the former TwoWay value binding: when a
  terminal surface was realized again after tab switching, WPF maximum/value coercion could create a
  viewport feedback loop that visibly bounced a Codex CLI until keyboard input reset it.

## Active foundation — cooperative agent coordination

The owner paused `/history` and `/undo` after completing their authoritative Core Undo coordinator and
resumed the provider-neutral coordination foundation. The complete cross-provider relay and live Claude
quota ingestion are proved, so cooperative budget handoff is the immediate task.

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

### Implemented foundation and exact next task

The provider-neutral Core coordinator, SQLite state, narrow project-bound MCP server, app runtime,
Codex App Server transport, Claude inspection/background adapter, paid-billing refusal guard, and
app/MCP companion packaging are implemented. Durable conclusions:

- one writer owns the project lease; handoff submission alone never releases it, and only app-owned
  provider-stop confirmation can transfer it;
- Codex receives a project-unique allow-listed Filekin MCP identity through one-run App Server config;
  Claude receives the same narrow identity through the user's unmodified, authenticated `claude --bg`;
- Claude launch refuses inherited or applicable-settings billing/provider redirection before a model
  turn and requires first-party Claude.ai authentication plus explicit shared-checkout consent;
- native approval/input requests remain human-owned, and neither adapter changes provider permissions,
  enables API billing, injects terminal input, or reads transcripts/screens;
- token-free real stdio tests cover all eight MCP tools, concurrent bidirectional messages, lifecycle
  refusal, and transactional state; packaging places the MCP companion beside Filekin;
- gated live probes proved Codex clock-in/messaging, Claude's structured `StopFailure(rate_limit)`
  pausing the project without a writer, and the full Codex → Claude → Codex relay end to end with real
  subscription-authenticated turns and project-bound MCP. Every probe's disposable checkout stayed empty
  and cleanup left no project changes. That relay injects fixed fresh quota snapshots so it isolates
  native turn/handoff/lease behavior.

- Live Claude quota ingestion is wired and proved: `Filekin.Mcp.exe --status-line ...` is the
  companion's second mode, storing only the parsed five-hour/seven-day windows as that project's usage
  observation, which `ClaudeAgentUsageSource` reads back (gated probe
  `FILEKIN_RUN_LIVE_CLAUDE_STATUS_LINE=1`, against a real `claude --bg` session).
- That quota drives a real decision. `AgentCoordinationPolicy` carries a second, earlier
  `HandoffRequestRemainingPercent` cutoff above the hard `MinimumRemainingPercent` floor, and
  `AgentProjectCoordinator.EvaluateUsageHandoff` asks the active lease owner for a cooperative handoff
  (`RequestHandoff` with `UsageThreshold`) only when its own usage is fresh, known, at or below that
  threshold and the partner has safe headroom to receive the lease. A stale or unknown observation and a
  low partner both leave the active turn untouched rather than guess.
- That check now fires on its own during a long turn. `AgentCoordinationRuntime` keeps a one-shot,
  self-rearming timer per project while a lease owner is `Working`, and each tick is the ordinary gated
  preparation, so it reads the same non-secret facts and can request the handoff without anything else
  asking. It stops the moment the project stops working, so a standing request is never re-asked; the
  default cadence is half `MaximumUsageAge`; an unexpected failure stops that project's pass and is
  recorded in `InTurnRefreshFault(projectId)` until that project's next explicit operation restarts it.
  Timers and faults are both keyed by project, so one project's healthy refresh never clears another
  project's stopped watcher.
- Token-free tests cover threshold selection, stale-observation refusal, the both-low defer, the
  no-lease no-op, idempotent re-evaluation, the runtime wiring, a crossing during a turn, no turn
  meaning no timer, the stop after a request, disposal, fault-then-restart, and two projects where only
  the failed one stops. Neither the decision nor the periodic pass needed a further live probe.

### `/agents` — first slice built

`/agents` is in the app and is the first thing that constructs `AgentCoordinationRuntime`. It is one
adaptive rich view over the current Files folder: setup when the folder has not opted in, the control
room when it has. Today it reads state without opting in (no project created, no provider probed, no
process started; the database opens on first use), **Set up here** is the explicit opt-in that records
the project and objective while writing nothing into the folder and granting no turn, and the control
room shows both agents' turn, connection, and per-window allowance in plain words (`Allowance unknown`,
never a comfortable zero) with the objective and last handoff. The objective may be empty at setup and
saved later. Back and Esc hide the surface only: they must never stop an agent, release the turn, or
end the project, the same rule archive and tidy already follow, and `/agents` reopens it.

`AgentProjectState` now carries `Objective`, `state.db` is schema 3, and the store gained its first
column migration: the additive `CREATE ... IF NOT EXISTS` script cannot alter an existing table, so
`AddMissingColumnAsync` runs after it and the migration test drops the column to prove the path.

### Owner decisions, 2026-08-31 — starting, choosing, and stopping

- **Who starts.** Filekin picks the agent with more allowance left. The user may choose instead; no
  choice means automatic. A chosen agent that is not safe to start pauses with that reason rather than
  quietly starting the other one.
- **One agent is enough to start.** Work does not wait for both. The relay begins when a second agent
  clocks in. `SelectInitialAgent` must therefore stop requiring every participant to be clocked in.
- **Stop always keeps the project**, for any agent, so it can be resumed later. It is a cooperative
  request, not a kill, and a user-requested stop is a resumable pause — never `NeedsAttention`.
- **Watching comes before answering.** The next surface is a read-only Agent Session view. The reply
  box and approvals follow it.

### Exact next task: build the agent run in sections

Each section is a separate owner checkpoint with its own build, tests, and handoff update. Do not merge
them, and do not start a later one early.

**Section 1 — Core: who may start, and stopping without failure. Done.** No UI, and no file is written
into any project folder. What shipped:

- `SelectInitialAgent(state, now, AgentProvider? preferred = null)`. Nothing chosen keeps the
  most-remaining-then-provider-order choice. A chosen agent that is safe is activated; a chosen agent
  that is not safe pauses with a reason naming it, and never quietly starts the other one.
- One clocked-in agent with safe allowance can now start. Selection only refuses when nobody is
  clocked in. The rules that protect a handoff recipient in `CompleteActiveTurn` and
  `EvaluateUsageHandoff` are unchanged.
- `RequestStop(state, provider)` is lease-owner only. It sets that participant to `StopRequested` and
  the project to `StopPending`, and never releases the lease, exactly like `RequestHandoff`.
- `CompleteActiveTurn` handles `StopPending` before the missing-handoff branch: the lease is released
  into `Paused` with a plain resumable reason, a handoff the agent still submitted is kept as
  `LastHandoff`, and the partner is not activated, because stopping is what was asked for.
- `Resume(state)` returns `Paused` to `Ready` and clears the reason and any `StopRequested` turn state.
  It only clears the pause; selection decides again whether anybody may take the turn.
- `StopRequested` is in both `ReconcileAfterRestart` lists, so a restart never assumes a stop it did
  not see finish.
- Runtime: `SelectInitialAgentAsync(projectId, preferred = null, ct)`, `RequestStopAsync`, and
  `ResumeAsync`, all through the same operation gate and all calling `TrackTurn`, so a stopped project
  stops being watched. `ConfirmProviderStoppedAsync` does not refresh a handoff recipient's usage
  during a stop, because no partner is activated.
- Tests: 11 token-free Core tests and 2 runtime tests, including the persisted round trip proving a
  stop keeps the project. Core 401 passed, Infrastructure 286 passed / 4 skipped (gated live),
  solution builds, `dotnet format --verify-no-changes` clean.

Nothing in the app calls stop, resume, or the preferred agent yet. That is Section 2.

**Section 2 — Consent, launch, and the two turn actions. Done, but not yet run against a live
provider.** What shipped:

- Shared-checkout consent is a project fact. `GrantSharedCheckoutConsent` stores the exact approved
  words and when, `state.db` is schema 4 with both columns migrated additively, and a row holding only
  one of them is refused as damaged instead of read as an approval. The `/agents` surface shows the
  same sentence it stores, through one constant, so the words shown and the words stored cannot drift.
- `AgentRunService` is the one component that starts a provider process. It stays out of the
  coordination runtime, because that runtime deliberately never dispatches a native turn. Start
  refreshes allowance, chooses the agent with more left unless the user chose one, launches with the
  project-bound MCP identity, waits for the clock-in, and only then asks for the turn. A start whose
  agent never reports back asks that session to stop and leaves no turn held.
- `NativeAgentSessionLauncher` launches for real: Claude Code through its documented background
  session, Codex through the app server thread and turn. Claude's own lifecycle report is the proof of
  a stop. Codex has no cooperative stop command, so Filekin's request reaches it through the
  coordination state it reads and the turn ends when Codex ends it; interrupting would be a kill, so
  Filekin does not do it.
- Allowance can be recorded before an agent clocks in, so a cold project shows real numbers and can
  choose who to start. It never makes an absent agent look present. Unknown allowance never blocks a
  start; only fresh evidence of being out of allowance does.
- The `/agents` control room now has the approval box, a start-with choice, **Start work**, **Pass the
  turn**, and **Stop**, plus a command-bar strip that keeps a running turn visible and stoppable from
  anywhere, matching archive and tidy. The surface re-reads the project every three seconds while it is
  open or a turn is held; that read touches only the database, and a read that fails stops the watch
  and says so once rather than retrying behind a stale picture.
- Nothing writes a file into the project folder.

Tests: Core 408 passed, Infrastructure 292 passed / 4 skipped (gated live), MCP 13 passed. Solution
builds and `dotnet format --verify-no-changes` is clean.

**Section 2 is done and proven against live Codex and Claude.** Two rounds of live QA were run from
this session against the owner's own subscriptions, in the owner's throwaway folder `D:\github\agent-test`.

**What is proven live, right now:**

| Live check | Result |
| --- | --- |
| `LiveCodexRelayTests` | passes |
| `LiveClaudeStatusLineTests` | passes |
| `LiveCompleteRelayTests` (Codex to Claude to Codex) | passes |
| `LiveAgentRunTests.CodexStartedByFilekin...` | passes; Codex creates the file |
| `LiveAgentRunTests.ClaudeStartedByFilekin...` | passes; Claude creates the file |
| `LiveAgentRunTests.PassingTheTurnStartsThePartner...` | passes; the partner is started on demand and takes the turn |
| `LiveClaudeRelayTests.DepletedClaudeReportsStructuredUsageLimit` | cannot be judged: it only passes when Claude is actually out of allowance |

`LiveAgentRunTests` is new. It drives the real path a person uses: `AgentRunService` plus
`NativeAgentSessionLauncher`, in a real folder, with real providers. Each test is opt-in through its
own switch (`FILEKIN_RUN_LIVE_AGENT_RUN_CODEX`, `..._CLAUDE`, `FILEKIN_RUN_LIVE_AGENT_RELAY`) and the
folder can be moved with `FILEKIN_LIVE_RUN_FOLDER`.

**What live QA found and fixed, in order:**

1. **Nothing was ever written, by either agent.** Both were stopped by their own permission systems and
   Filekin discarded what they said about it. The approval step now carries the owner's answer:
   **Use my own settings** keeps the recorded rule that Filekin sends nothing, **Trust this folder**
   scopes the run to the folder. Never `bypassPermissions`, never an answered approval.
2. **Filekin's own Codex sandbox was the thing breaking Codex.** Naming an explicit `writableRoots`
   produces a root set the Windows restricted-token sandbox refuses to enforce, and then every file
   operation fails before it runs. Codex's plain `workspaceWrite` on the turn's working directory works
   perfectly, and that directory is already the approved folder. Do not add roots back.
3. **The handoff was refused over a label.** Filekin asked for the handoff, so it already knew why; it
   then rejected the agent's submission because the agent guessed a different reason, throwing away the
   written handoff. The reason is now Filekin's own fact, and a blocked agent can still submit one.
4. **The safety threshold was a wall.** Claude at eight percent could not be given the turn at all, so
   the relay could not finish and the owner was offered nothing but "give it to the other one". A
   project can now be set to **work even when allowance is low**, off unless the owner turns it on. The
   agent must still be clocked in, and Filekin still never buys usage or enables metered overage.

`state.db` is schema 6. Tests: Core 418 passed, Infrastructure 295 passed / 7 skipped (gated live),
solution builds, `dotnet format --verify-no-changes` clean.

**Two operational traps, both real:**

- **A `Filekin.Mcp.exe` left running after a live session is a symptom, not the process to kill.**
  A live Claude probe that ended any way except its one success path left a real `claude --bg` session
  alive, and a live Claude session keeps respawning its MCP companion: killing only the companion is
  useless, because it comes straight back. `LiveClaudeRelayTests` now always asks its session to stop
  in a `finally`, and `LiveAgentRunTests` ends the background session by its recorded identity even
  when the run never reached the turn, which is the case the lease-owner stop could not cover. A real
  session later proved that `claude stop` can briefly report stopped/no PID and then be respawned by
  Claude. Filekin now requires that inferred stop to remain true across two polls before releasing the
  lease, but the disposable session itself must be ended through Claude rather than by killing its
  companion. If a companion locks a build, inspect the parent session first.
- **Rebuild the Release MCP after any Core change** before live testing. Agents load
  `src/Filekin.Mcp/bin/Release/net10.0-windows/Filekin.Mcp.exe`, and a stale one silently serves old
  rules. One live failure in this session was caused by exactly that.

**Section 3 — Read-only Agent Session view. The live-QA regressions are fixed; the live Codex re-run
and the owner's interaction pass remain, so do not start Section 4 yet.** One persistent task tab
opens from each connected agent's row in `/agents` and is keyed to the exact project, provider, and
native session Filekin started. It is separate from the Files rich view and normal terminal tabs.
Ctrl+Tab includes it; Ctrl+Shift+W or its close button closes only the view and never stops the
provider.

Provider facts cross one replayable provider-neutral immutable event feed. Codex maps its documented
App Server `turn/*`, `item/*`, and server-request streams into replies, actual tool activity/outcomes,
questions, errors, and status rows. The official App Server documentation is the contract; reasoning
and experimental process events are deliberately omitted. Claude Agent View currently documents
structured background lifecycle/waiting state plus `claude logs <id>`, but no typed background tool
stream. Filekin therefore shows Claude lifecycle and one normalized recent-provider-output snapshot
honestly; it never parses rendered text into invented tool events and never reads transcript/state
files. Project messages and structured handoffs are merged into both relevant session views.

The surface is read-only. There is no reply box and no approval control. A provider request plainly
says answering in Filekin is not built and directs the owner to that provider's own session UI. A
session from an earlier Filekin process still opens with coordination messages/handoffs and says that
its live provider stream is unavailable instead of attaching to a guessed session.

The owner's first live Codex run created the requested dated file but exposed three Section 3
regressions. **All three are fixed, and the token-free suites cover them.** What the fixes settled:

1. **Session identity is app-owned and out of band.** The rejected prompt binding is gone:
   `AgentRunPrompt.BindNativeSession` no longer exists and nothing tells a model what to call itself.
   Instead `AgentProjectCoordinator.RecordNativeSession` and `AgentCoordinationRuntime`
   `RecordNativeSessionAsync` record the session Filekin itself opened, and `AgentRunService` records
   it immediately after the launch and before it waits for the clock-in. `ClockIn` no longer takes an
   identifier at all, so `filekin_clock_in` reports presence only and publishes no `nativeSessionId`
   parameter for a model to fill in. A real-stdio MCP test proves the published schema offers no
   identifier, that supplying one anyway changes nothing, and that the recorded identity survives; a
   prompt test guards against the natural-language binding returning.
2. **A provider lifecycle callback never replaces the recorded identity.** `ReportUsageLimit` used to
   throw when the callback named a different identifier. That guard rested on a false assumption:
   Claude's hook passes `${session_id}`, the conversation session, while Filekin drives the background
   session `id` — the two legitimately differ, so the guard would have thrown away a real limit report
   from a session Filekin started. The callback now establishes an identity only when Filekin has
   none, and the fail-closed report still applies.
3. **A completed project can run another job.** `StartNewObjective` keeps folder approval, the
   low-allowance preference, messages, and handoff history; clears both native session identities,
   connection and turn state; returns the project to `Ready`; and starts no provider. Core and runtime
   tests cover completed-only refusal, the persisted round trip, an unchanged objective, what is kept
   and what is cleared, no provider contact, and no turn watcher.
4. **The app compiled again and shows one identity.** `RestoreAgentsFocus` still referenced the
   `AgentObjectiveSetupBox` that the consolidated Objective control replaced. The session view now
   carries one session identity instead of a provider id plus a coordination alias, because the
   participant's identity is now Filekin's own record of what it opened.

Codex's UTF-8 fix for App Server stdout/stderr is kept, so curly punctuation is no longer mangled.

The earlier Section 3 checkpoint and the current uncommitted hardening build and pass their token-free
suites. The normal Release solution was rebuilt after removing an orphaned Claude worker and MCP helper;
it completes with zero warnings and errors.

**Live state on 2026-08-31, after the fixes.** `LiveAgentRunTests.ClaudeStartedByFilekin...` passed
against the real subscription: Filekin started Claude, Claude clocked in without being told any
identifier, the turn was granted against Filekin's own recorded session, the file was created in
`D:\github\agent-test`, and cleanup left no session or companion behind. **Codex could not be judged:
the account reported `minimum remaining=0%` and the turn failed in six seconds**, so
`LiveCodexRelayTests` fails for allowance, not for the contract.

**Exact next task: the live Codex re-run when its window resets, then the owner's interaction pass.**
Rebuild the Release MCP first, then run `FILEKIN_RUN_LIVE_CODEX_RELAY=1` (`LiveCodexRelayTests`) and
`FILEKIN_RUN_LIVE_AGENT_RUN_CODEX=1` (`LiveAgentRunTests`), and confirm Codex clocks in without being
told any identifier and that the persisted identity is the App Server session Filekin opened. Then let
the owner confirm in the app that the session view shows typed reply/tool/file-change rows with correct
punctuation, and that **New job** accepts a new or unchanged objective and returns a completed project
to Ready before **Start work**. These changes are still uncommitted; commit them at that checkpoint.

**Agent-initiated hand-over shipped after the first real relay attempt (owner decision, 2026-08-31).**
The owner set a relay objective ("take turns, alternate to 10"). It could not run: Claude messaged
Codex to clock in, but a message wakes nobody, and `SubmitHandoff` refused every attempt because
Filekin had not asked first. So the only way to move the turn was a person pressing **Pass the turn**
for every leg. An agent may now hand over on its own; the opening prompt says so plainly, and says
that messaging an idle partner does not start it. Everything else is unchanged: the partner is still
started only when there is something to hand over, and only a proven provider stop moves the lease.

**Usage windows are named by their length.** Codex reports `primary`/`secondary`; Claude reports
five-hour/seven-day. Neither label means anything to a person, so the control room and session view
label each window by its own reported duration ("5 hours", "7 days") and fall back to the provider's
key only when a window arrives without one.

**Ending a session is a per-agent action (owner decision, 2026-08-31).** Stop only ever reached the
turn holder, so idle sessions piled up: a Claude background session stays alive and idle after its
turn, and each live session keeps its own `Filekin.Mcp.exe` companion, which then locks the Release
build. Every agent row now has **End session**, always clickable. It ends that agent's sessions in
the project folder through the provider's own stop, including sessions this window never started; on
the turn holder it is the same cooperative stop as before; for Codex it says plainly that there is
nothing to end, because an App Server turn ends when Codex ends it. `RecordSessionEnded` covers the
case this exposed: a session that ends while holding no turn used to reach `ConfirmProviderStopped`
and fail with "only the active lease owner's proven stop can release its lease". It now only records
that the agent is no longer here.

**Everything that happened reads one way: oldest at the top, newest at the bottom.** The control room
feed used to be newest-first while the session view was oldest-first, which is why the surfaces read
as confusing. Both now grow downwards, both say so on screen, and both follow the newest line unless
somebody has scrolled up to read. Session timestamps carry seconds, because a live run puts many rows
in the same minute. A long detail shows its first lines with the whole text in the tooltip.

**Open: the owner finds `/agents` and the session view cluttered and wants a cleaner, more
professional layout.** The mechanical confusions above are fixed; the visual pass is not done and
needs the owner's eyes on the built app first. Do not redesign it blind, and do not turn it into a
dashboard.

**The agent surfaces have a design grammar now (owner decision, 2026-08-31).** The owner said the
control room and session view were busy, verbose and hard to follow, and asked for one clean design
rather than questions about where controls go. The rules the surfaces now follow, and that later work
must keep:

- **One band answers one question**, in this order: what is happening, what the objective is, who the
  agents are, what you can do, what happened. Each band carries a quiet monospace caption
  (`OBJECTIVE`, `AGENTS`, `WHAT HAPPENED`, `LAST HANDOFF`, `ACTIVITY`).
- **The first line says what is happening and what to do next**, in plain words: "Nobody is working.
  Press Start work." It is the status line, not a note beside the title.
- **Facts on the left of a row, controls on the right.** An agent row is name, what it is doing, what
  it has left, then its own controls. Connection and turn are one phrase, not two columns.
- **No paragraph explains a control.** Everything those paragraphs said lives in each control's own
  help text. Empty space beats an unread explanation.
- **Everything reads oldest at the top, newest at the bottom**, and follows the newest line unless
  somebody has scrolled up.
- **A number is named once**: "Usage left - 5 hours 92% - 7 days 60%".
- **Rows update in place** rather than being rebuilt, so a list somebody has open does not close under
  them every refresh.

**Model and effort are a per-agent choice (owner decision, 2026-08-31).** Each agent row carries one
control showing its choice ("Default", "opus", or "opus - high") that opens a small MODEL and EFFORT
list. It is stored per participant (`PreferredModel`, `PreferredEffort`, `state.db` schema 7) and
passed at launch: Claude Code through its documented `--model` and `--effort` flags; Codex model on
`thread/start` and effort on `turn/start`. Codex's list and supported efforts come from its documented
`model/list`. Claude offers the stable subscription aliases that do not risk usage credits; `best`,
`fable`, and one-million-context aliases are excluded, and Haiku offers no unsupported effort choice.
An install that cannot answer simply offers Default. A running session keeps what it started with,
and Filekin still writes nothing into the user's own tool settings.

**Live relay QA, evening of 2026-08-31 — faults found by running it.** The owner ran
the one-line relay in `D:\GitHub\agent-test` (two agents append one line each to `handoff_text.txt`
to ten entries). What broke, and what the fixes settled:

1. **`filekin_clock_in` returned a bare invocation error.** Core refused a clock-in from the agent that
   already held the turn. Filekin itself starts a new session for a provider whose earlier session is
   gone, so that session met a failure it could not act on. Clocking in again is now allowed and
   leaves the turn state exactly as it is; it can never reset a turn underneath itself.
2. **The turn was handed to an agent nobody was running.** `EnsureHandoffPartnerIsHereAsync` started
   the partner only when the *project record* said it was offline, and a record saying "Ready" outlives
   the session it describes. That decision now reads the live session list, never the record.
3. **Stop could not release a turn held by a session Filekin was not watching.** No report would ever
   arrive, so the project stayed stuck forever. `RequestStopAsync` now asks that tool to end whatever
   it still has open in the folder, and uses its answer as the app-owned evidence: nothing left running
   means the turn belongs to nothing, and the project pauses, resumable. `AgentTurnState.StopRequested`
   also had no words in the UI and read as "Unknown"; it says "Stopping".
4. **A Claude session stopped at its own permission prompt before it could clock in.** Filekin's
   background settings now allow exactly one rule, `mcp__filekin__.*`, so an agent may use Filekin's
   own coordination tools without being asked each time. Codex already had the same narrow allow-list
   through its launch config. This is not a permission bypass: no permission mode is sent, file,
   command and network permissions stay as the owner's own settings have them, Filekin still answers
   no prompt on anybody's behalf, and the consent sentence now says so. Genuine questions from an agent
   are Section 4's job, and this fix keeps Section 4 for real questions rather than plumbing.
5. **Claude `blocked (idle)` was shown as a question even when Claude supplied no question or waiting
   reason.** Agent View's idle state means the response is finished and waiting for more input; it is
   not evidence of a question. Filekin now shows it as idle and asks the one-turn background session to
   stop. A real session also exposed a stop/respawn race, so an inferred no-PID stop must remain stable
   across two normal polls before Filekin releases the turn. Explicit provider terminal states remain
   immediate.
6. **The shared SQLite database became malformed.** The damaged schema could not be read, so the exact
   write that caused it is unknowable. Two risk factors were present: a stale Claude session repeatedly
   respawned an MCP writer after Filekin appeared stopped, and both stores combined WAL with SQLite
   shared-cache mode, a combination Microsoft discourages. Shared cache is removed, and migrations now
   stamp `user_version` only after every additive column succeeds so an interrupted migration retries.
   The damaged database and sidecars are preserved under
   `C:\Users\mfloy\AppData\Roaming\Filekin\corrupt-state-20260831-1955`; live state was reset.

**The provider cleanup fault is still active. The machine was not clean (verified 2026-08-31, 21:00).**
Codex suspected that the `Filekin.Mcp.exe` which reappeared after the 19:55 reset belonged to a Claude
session Filekin believed did not exist. It did. What was found, and what each fact proves:

- **The orphan was real.** `claude agents --json --cwd "D:\GitHub\agent-test"` listed one live
  background session, `1f2aaf2c` (pid 18440), `blocked`/`idle`, started 20:19:35, named
  `Filekin agent-test`, under a `claude daemon` from 19:20:48 that outlived everything around it. Its
  own `--mcp-config` named `Filekin.Mcp.exe --project dedad3ae-12b7-4519-ab73-7d03fdf69614`. So the
  companion that kept coming back was that session's, exactly as suspected.
- **The provider's own stop is not broken.** `claude stop 1f2aaf2c` answered `stopped 1f2aaf2c` and
  exited 0. Afterwards `claude agents --json` returns `[]`, and the session, its daemon, its pty host
  and its `Filekin.Mcp.exe` companion are all gone. `StopBackgroundSessionAsync` and `StopAllAsync` do
  the right thing whenever they are called. Killing the companion is still useless; stopping the
  session removes all four processes at once.
- **The fault is that nothing calls that stop once Filekin goes away.** `AgentRunService.DisposeAsync`
  only cancels the watchers, and says so in its own comment: running work outlives the window.
  `MainWindow.OnClosing` asks only about terminal tabs and never mentions a live agent session. Filekin
  also never reattaches to a session from an earlier process. So a closed or restarted Filekin leaks
  every live Claude session permanently, and each leaked session keeps respawning a `Filekin.Mcp.exe`
  writer. That is the open close-behavior decision below; until it is settled the leak is a certainty,
  not a risk.
- **The 19:55 corruption risk factor was still armed, and it is worse than a locked build.** `dedad3ae`
  no longer exists: live state was reset and a fresh project `9bdbf08f-5604-43d1-a37e-5b6511a8f2d1` was
  created at 21:00. The orphan's companion was therefore a writer from a dead project generation
  opening the current `state.db` read-write and running its schema path against it. That is the same
  shape as the write that damaged the previous database.

**Fixed: a companion pinned to a project that does not exist now refuses to start.** This needed no
product decision, and it removes the corruption risk factor whatever the owner decides about closing.
`SqliteAgentProjectStore.ProjectExistsAsync` answers whether a project is in a state database over a
read-only connection that never creates the file, never migrates, and fails closed on an unreadable or
schema-less database. `Filekin.Mcp` asks it before starting anything: the coordination server prints
why and exits 2, and the Claude status-line mode discards the observation the same way, so neither can
attach as a writer on the way to discovering it has no project. Proved against the live WAL database
while it was open: the orphan's dead id is refused with exit 2, the real project starts normally.
`state.db` is `user_version` 7 and `integrity_check` = ok.

**Usage is an account fact, not a project fact (owner decision, 2026-08-31, and built).** The old
store kept one usage reading per project, so a new folder started blind about an account measured
minutes earlier, and two projects could hold different numbers for the same account. Proved by probing
the installed tools: `account/rateLimits/read` and `account/usage/read` take no folder and no project,
and Claude's five-hour window is spent by every session on the machine. `state.db` is schema **8**:
`agent_usage_observations` and `agent_usage_observation_windows` are keyed by provider alone, with
`reported_by_project_id` kept only as provenance. The 7-to-8 migration keeps the newest reading per
provider and drops the duplicates, which described the same account anyway. It was proved on a copy of
the live database: `user_version` 7 to 8, integrity and `foreign_key_check` clean, and a real
status-line payload stored account-wide afterwards.

**A window past its own reset time counts as full again.** Both providers say when each window resets,
so an old reading is not automatically useless. `AgentUsageWindow.HasResetBy` /
`RemainingPercentAt` and `AgentUsageSnapshot.MinimumRemainingPercentAt` / `IsUsable` carry the rule,
and every allowance decision uses them: a reading is usable when it is fresh **or** when every one of
its windows has since reset. One stale window that has not reset still answers nothing, because work
Filekin never saw may have spent it. Together with the account-wide store this is what lets Filekin
answer "can anyone work?" before starting anything, instead of paying for a launch to find out.
`RefreshAllowanceAsync` was already the pre-start read and already used `RecordAllowanceBeforeStart`
for an agent that has not clocked in; it now has something to read on a project's first run.

**Codex probe results worth keeping (2026-08-31).** `account/usage/read` answers with no thread and no
turn: `summary` (`lifetimeTokens`, `peakDailyTokens`, `longestRunningTurnSec`, streak days) and
`dailyUsageBuckets`. Passing `threadId` scopes it to one thread, so per-run token counts are available;
an empty thread answers all nulls, and the populated shape is unconfirmed because confirming it costs a
turn. It reports **tokens, never money**. `account/rateLimits/read` also carries `credits`, `planType`,
`spendControlReached` and `rateLimitReachedType`, none of which Filekin reads yet. **Cost tracking was
considered and deliberately dropped** (owner, 2026-08-31): Claude's status line does carry
`cost.total_cost_usd`, but its own documentation calls it a client-side list-price estimate that may
differ from the bill, so on a subscription it is not money spent. It would only be meaningful for
someone on API billing.

**The two Properties-dialog tests are opt-in now.** They opened real system dialogs on the owner's
desktop during every ordinary run. They are gated behind `FILEKIN_RUN_SHELL_DIALOG_TESTS=1` and skip
otherwise. They were kept rather than deleted because they guard a real past bug: the `properties` verb
fails with ERROR_CANCELLED for the user profile folder (DECISIONS.md, 2026-08-27). Run them by hand
after touching `WindowsPropertiesDialog`.

**The ten-entry relay ran end to end on 2026-08-31 at 23:09 and passed.** Codex started, and the turn
alternated Codex - Claude ten times with nobody pressing anything between entries. `handoff_text.txt`
holds ten real appended entries afterwards, not ten claims that it does. Cleanup left no Claude session
and no `Filekin.Mcp.exe` helper. `LiveTenEntryRelayTests` is that run, kept as a gated probe
(`FILEKIN_RUN_LIVE_TEN_ENTRY_RELAY=1`); it asserts on the file's real contents and on the turn order,
and it fails fast when the relay stalls instead of waiting out its deadline.

**Four faults that first run's predecessors exposed, all fixed:**

1. **Start work ignored the objective box.** The box was a draft; only Save wrote it to `state.db`, so
   pressing Start launched an agent against an empty objective, which then clocked in, spent a turn and
   could only ask a person what the job was. Start now saves what is typed before it starts, the Start
   button stays disabled until there is an objective, and `AgentRunService.StartAsync` refuses a blank
   objective before anything is spent.
2. **A written handoff was thrown away.** An agent writes its handoff last, and its turn can end
   underneath it: the provider reports the turn complete on one channel while the agent's tool call is
   still travelling on another. `SubmitHandoff` refused, the agent could not tell why, retried, failed
   again and reported itself blocked with the work already done - which is exactly how a relay stalled
   after entry 05. A handoff from an agent that no longer holds the lease is now **kept as history**
   and the agent is told it succeeded; the turn does not move a second time. A repeated submission in
   one turn is likewise not an error.
3. **Every tool refusal reached the agent as a bare invocation failure with no reason in it.** All
   eight tools now pass Filekin's own sentence through as an `McpException`, so a refusal is something
   an agent can act on rather than retry blindly. Without this nobody - agent, owner or Filekin - could
   diagnose the stall above.
4. **Closing Filekin counted the wrong thing and left sessions behind.** The close question asked this
   window's own session list, but a Claude background session stays open and idle after its turn, so it
   leaves that list long before it stops existing: closing reported nothing running while two idle
   sessions and three helper processes were still there. Closing now asks **the providers**, across
   every project, and **End agent sessions and close** reaches those same sessions. A provider that
   cannot be asked is reported as unknown, never as nothing.

**Speed, measured on that run.** Ten entries took 6m31s, about 39 seconds each. Roughly 15 to 25 of
those seconds fell between "the entry is on disk" and "the turn moved". Claude has no event to push, so
its lifecycle is polled and an inferred stop must hold across two polls; at a five-second interval that
alone was up to ten seconds per Claude turn. The interval is now two seconds, which costs one
`claude agents --json` process every two seconds while a session is open and saves roughly six seconds
per Claude turn. The rest is inherent: each turn starts a fresh provider session on purpose, because
Filekin will not keep the partner running and burning allowance while it waits. Effort is already a
per-agent choice, and the relay instructions themselves ask each agent to re-read two files every turn.

**Codex writes to disk correctly under Filekin's sandbox** - proved directly with one real turn using
Filekin's exact `turn/start` parameters (`workspaceWrite`, `approvalPolicy: never`): the file appeared
on disk with the right contents. An earlier run where entries went missing was the agents overwriting
the file rather than appending, not Filekin and not the sandbox.

**Exact next task: the owner's interaction pass on `/agents`, then the gated Codex probes.** The
coordination path is proved end to end, so what is left is the surface. Open `/agents` in
`D:\GitHub\agent-test` and confirm the objective box, Start work, the per-agent model and effort
control, the session view rows, and the close question all read well. Then run `LiveCodexRelayTests`
and `LiveAgentRunTests`. Check the machine with
`claude agents --json --cwd "D:\GitHub\agent-test"` rather than by looking for `Filekin.Mcp.exe`: the
session list is the truth and the companion is only its shadow. Do not start Section 4.

**Closing now asks what to do with a live agent session (owner decision, 2026-08-31, and built).**
This was the last live cause of orphaned sessions: closing Filekin used to walk away from every session
it was watching, and a leaked Claude session keeps respawning its own `Filekin.Mcp.exe` writer. The
owner chose the three-answer close. What it does now:

- Nothing running closes with no question at all. Terminals only keeps its old yes-or-no question,
  because a terminal always ends with the window.
- A live agent session is asked about plainly: **K - Keep agents running**, **E - End agent sessions
  and close**, **Esc - Cancel**. The first line says what is running and that it keeps working after
  Filekin closes. Focus starts on Keep, the answer that changes nothing.
- **End** is the provider's own cooperative stop for every session this window has open, never a kill.
  It reuses the same path as the per-agent **End session** button, so it also clears sessions in that
  project folder which this window never started.
- **A failed end does not close.** The window stays open and re-asks with the reason on it, because
  closing anyway would leave exactly the processes the question exists to prevent. Keep is still
  offered, so nobody is trapped. A provider that never answers runs out of a 30-second budget and is
  reported as a failure, not as a clean exit.

Seams: `AgentRunService.LiveSessions()` and `StopAllSessionsAsync()` (one refusing agent never spares
the rest; the first reason is returned and kept in `StopFault`), `ShellViewModel.LiveAgentSessionCount`
and `EndAllAgentSessionsAsync`, and `MainWindow.ShowConfirmation`, which now takes an optional second
answer so a three-answer question is not squeezed into yes-or-no. `AgentRunService.DisposeAsync` still
only lets go; deciding the sessions' fate belongs to the window, before it closes.

**Known limit, deliberate:** this counts sessions **this window** has open. A session leaked by an
earlier Filekin process is not in that count, because Filekin never reattaches to one. Clear those with
**End session** in `/agents`, which asks the provider for everything it still has open in the folder.
The companion guard above already removes their worst consequence.

**Roles are the owner's next feature, not yet built.** Two agents with different jobs ("Claude writes,
Codex reviews") only works today if the objective says so in prose. The design agreed with the owner:
one optional role line per agent, stored beside its model choice, sent with that agent's own opening
text. The objective stays what finished looks like, the handoff stays what is left, and an agent taking
over is now told plainly that the handoff is newer than the objective.

**Task after those regressions pass: Section 4 — Answering and approvals**, through each provider's
supported session path only. Treat it as a new owner checkpoint. Never synthesize keystrokes and never
answer yes automatically.

**Section 5 — Bootstrap preview.** An existing project writes nothing by default and is offered one
pointer line; an empty folder is offered `.filekin/PROJECT.md`, `AGENTS.md`, and `CLAUDE.md`, none
carrying invented rules, and never a competing `HANDOFF.md`. It may move earlier if a real run shows
the agents need the files sooner.

**The opening prompt is now minimal (owner decision, 2026-08-31).** It was about 263 tokens of prose
per session start, and most of it repeated what the MCP tool list already tells the model. It is now
about 71 tokens — a project line, "call filekin_clock_in, then filekin_read_state, and check the state
again as you work", whether this is a fresh start or a handoff, and the user's objective. Everything
else moved into the tool descriptions, which each provider sends to its own model anyway: clock in
first or you get no turn; the state says whether a hand-over or stop was asked for; a message does not
start the other agent; submit a handoff when your part is done and then end your turn. Keep new
coordination rules in the tool descriptions rather than growing this prompt back.

**Open design question raised after the first live Section 3 run, not an owner decision:** consider
whether durable, human-readable project context belongs in an explicitly previewed
`.filekin/PROJECT.md` instead of repeating a long coordination template in every opening turn. Do not
simply copy the current prompt into a file. Evaluate which parts are stable project context, which are
run-specific objective text, and which must be enforced out of band. Existing `AGENTS.md` /
`CLAUDE.md` files must still never be overwritten. Exact native session ids, leases, allowance
observations, credentials, and other live coordination state do not belong in a project file. Ask the
owner for a product decision before changing bootstrap ordering or writing this file.

Still open: what management grammar, if any, belongs beneath `/agents`; and which conservative handoff
percentage ships. The app uses the same safe implementation defaults as the tests (floor 10, request at
30); they are not a settled decision, so do not present a number as final without live validation.

Never use `bypassPermissions`, `-p`, the Agent SDK, API billing, terminal injection, or screen scraping.

### Standing implementation contracts

- Token cost is a design constraint, not an afterthought. Text Filekin sends to a model is spent from
  the user's own allowance, so say it once and say it where the model already looks: coordination
  rules belong in MCP tool descriptions, not in a growing opening prompt, and nothing Filekin sends
  should repeat what a tool description already carries.
- Filekin sends no model choice. Each tool runs whatever model its own configuration selects; a
  per-project model picker would be a new setting and needs an owner decision first.
- `Filekin.Core` contains no WPF, provider SDK, process, JSON-RPC, or MCP implementation types.
- Provider responses become provider-neutral immutable snapshots at the infrastructure boundary.
- Keep separate usage windows separate; do not invent a universal quota or predict next-turn cost.
- A provider stop event without a structured handoff becomes `NeedsAttention`; never activate the
  partner with guessed context.
- An MCP handoff/completion report does not prove the provider stopped and therefore does not release
  the writer lease. Only the app-owned provider lifecycle transition can release or transfer it.
- The agent holding the turn may submit a handoff without being asked, because the partner is not
  running and no message can wake it: a relay is impossible otherwise. Why the turn moves stays
  Filekin's fact. When Filekin asked, Filekin's reason is recorded; when the agent asked, the reason is
  `WorkCompleted`, because allowance is Filekin's own reading and a user request is the user's. A stop
  the user asked for is never turned into a hand-over — the written handoff is kept as history and the
  partner is not started. The lease still moves only on a proven provider stop.
- A structured usage-limit hook may establish an unavailable provider session before model-driven
  clock-in. It never releases a writer lease, stores raw provider error/transcript text, or accepts a
  stale session id over the current native identity.
- The working-tree lease is cooperative state, not an OS lock. Parallel writing is excluded from the
  first slice; a future parallel mode requires separate Git worktrees.
- `state.db` agent schema version 2 is normalized rather than one serialized state blob. Preserve
  `PRAGMA user_version` migration checks and the writer-reservation-before-read rule. One
  `user_version` describes the whole shared file, so `StateDatabase.SchemaVersion` is the single number
  the agent store and the operation journal both use; raise it there when either schema grows. The
  current migration works only because every revision so far is additive `CREATE TABLE IF NOT EXISTS`.
- Claude status-line observations are quota facts only. The helper process writes
  `agent_usage_observations`, never participant, lease, session, or turn state, and refuses a payload
  whose workspace is not this project's folder. The app, not the helper, applies an observation to a
  participant through `AgentProjectCoordinator.UpdateUsage`.
- Claude Code runs a status-line command through Git Bash when it is installed and PowerShell
  otherwise, and Git Bash eats unquoted backslashes. `ClaudeStatusLineCommand` therefore emits one form
  both shells accept: a bare `powershell -NoProfile -Command` prefix, forward slashes, and single-quoted
  paths. Both shells were verified against paths containing spaces. Do not "simplify" it to a quoted
  executable path, which PowerShell would treat as a string instead of a command.
- The periodic in-turn refresh is one-shot and rearms only after a tick finishes, so a slow provider
  read can never overlap the next tick. Do not convert it to a periodic `ITimer` period, and keep the
  interval shorter than `MaximumUsageAge` or every tick would evaluate stale usage. Disposal must cancel
  and drain the running tick before taking the operation gate; taking the gate first deadlocks.
- MCP processes receive one project GUID and provider identity at launch. They must not accept either
  identity from tool calls, expose native session identifiers, or run restart reconciliation on
  startup. Reconciliation belongs to the app before it starts new coordination activity.
- The native session identity is app-owned too. Filekin records the session it opened; `filekin_clock_in`
  reports presence and carries no identifier, so a model cannot name, invent, or substitute the session
  it speaks for. A provider lifecycle callback may establish an identity only when Filekin has none.
  Never re-introduce a session identifier into an opening prompt or a coordination tool argument.
- `AgentCoordinationRuntime.StartAsync` must complete persisted restart reconciliation before project
  preparation, MCP launch configuration, or lease changes. Provider refresh precedes selection; a
  failed refresh records `Unavailable` but never releases an active writer. MCP configurations are
  inert values and do not start providers. Ordinary Filekin startup performs reconciliation only and
  must never dispatch an agent or request shared-checkout consent.
- Agent edits are external filesystem activity and do not enter Filekin `/history` merely because
  Filekin coordinated the agent.
- Keep normal interactive terminal tabs unchanged. Agent coordination must not intercept ordinary
  terminal keys or depend on VT-screen scraping.
- `/agents` is an adaptive rich setup/control-room surface. Coordinated provider work is shown in
  dedicated Agent Session task surfaces using supported structured session events. They are not
  `TerminalControl`, ConPTY, terminal emulation, or a duplicate interactive CLI. Offer native CLI
  attachment only when it attaches to the exact coordinated session.
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
  setting inline and never writes `.claude/settings*.json` merely to enable coordination. The setup
  entry point is the confirmed `/agents` surface.
- Treat each recorded implementation task as a separate owner checkpoint. Complete one task, update
  this handoff with the exact next task, report, and stop. If the owner says to stop mid-task, update
  this handoff with the precise completed state and resume point before ending the turn.

### Current non-blocking product questions

- `/agents` is the confirmed adaptive setup/control-room surface. What later management grammar, if
  any, belongs beneath it?
- How does a user attach coordination to an existing project as-is, and which optional bootstrap files
  are proposed without modifying or replacing its current agent instructions?
- Can the user provide the opening work prompt directly, and if so how is it combined with Filekin's
  coordination contract and delivered to whichever agent is selected first?
- What conservative handoff threshold should ship after live validation?
- Is readable handoff export always written or optional?
- Which plugin/connector management comes after the first relay?

These do not block the Core coordinator or provider spikes. Do not invent their UI while building the
foundation.

### Current live-test state

Claude allowance was available on 2026-08-30 and 2026-08-31. The complete subscription-backed relay
passed cleanly, the structured exhausted-limit path remains verified, and the status-line quota probe
passed on 2026-08-31. Future live tests remain explicit and gated. This is not permission to use API
billing, credits, `-p`, or another authentication path.

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

### Phase zero is met

Every phase-zero condition is satisfied: vendor-free coordinator tests, honest adapter states with no
implicit paid billing, no stale writer lease surviving restart, an MCP vocabulary and persistence model
fixed by tests, Claude quota arriving through the documented status-line interface without transcript,
screen, or credential access, and one real subscription-backed Codex → Claude → Codex round trip with no
concurrent writes, forced termination, or automatic approvals.

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

## Paused task — `/history` and `/undo`

The durable journal and safe Core Undo foundation are implemented and intentionally paused while the
live agent relay is active. Current state and traps:

- SQLite retains the newest 50 top-level Filekin operations, demotes prior-session Undo promises on
  restart, and remains an additive table in the same `state.db` as coordination.
- Move/Rename, Toss aliases, Copy, Tidy, Zip, and Unzip are journaled according to the settled scope.
  Copy/Tidy are informational; Move/Rename, exact-identity Toss, Zip, and Unzip are session-undoable.
- Relocation, Toss, and archive evaluators/executors recheck immediately before changes, reverse bulk
  work in safe order, and retain exact pending work after partial/failure outcomes.
- Archive evidence includes SHA-256/time/length and exact recycled-original identities. Edited output
  requires explicit Keep Edited or Recycle Edited consent bound to the reviewed fingerprint.
- `OperationUndoCoordinator` is the authoritative exact-entry boundary. It kind-safely parses the
  requested row, converts legacy app archive payloads, reevaluates off the caller thread, executes once,
  and atomically stores lifecycle plus pending payload. Memory/SQLite stores reject stale loaded rows.
- Move/Toss path collisions currently return typed `NeedsDecision` without changing disk/history.
  No app path uses the coordinator yet; result-line archive Undo still uses its legacy executor.

**Remaining checkpoints when resumed:**

1. Implement Move/Toss Replace, Keep Both, Skip, Cancel, and bulk Apply-to-All execution with safe
   collision handling and exact retry payloads.
2. Compose the coordinator in `ShellViewModel`, route archive result-line Undo through it, and add
   `/undo` newest-safe selection. Keep the history UI out of this checkpoint.
3. Build the `/history` rich view with persistent rows, present-state explanations, and per-row
   Undo/Restore through the same exact-entry coordinator.
4. Finish conflict/edited-output prompts and keyboard behavior, then rebuild normal Release and let
   the owner perform the final interaction pass. Do not automate LiveView foreground input.

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

- `Filekin.Core/Operations/JournalEntry.cs`, `OperationUndoState.cs`, `IOperationJournal.cs`, and
  `InMemoryOperationJournal.cs` are the platform-neutral seam. Its explicit lifecycle and asynchronous
  contract are load-bearing for the SQLite implementation and later rich-view status text.
- `ShellViewModel.Archive.cs` owns today's archive-specific result-line Undo over the shared durable
  journal. Its exact-entry lookup is load-bearing once other operation kinds are recorded; later route
  it and `/undo` through one authoritative coordinator without reverting to newest-candidate behavior.
- Multi-archive unzip aggregation and reverse-order batch Undo are implemented. Keep that one-invocation/
  one-entry boundary when the rich history view is added.
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
  platform-neutral logic belongs in Core where it can be tested. The close question's own logic lives
  in `AgentRunService`, where it is tested; only its overlay needs the owner's eyes.
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

CI excludes `TestCategory=RequiresInteractiveShell`; the desktop suite does not. The two tests that
open a real Properties dialog are additionally gated behind `FILEKIN_RUN_SHELL_DIALOG_TESTS=1`, so an
ordinary run never pops a window. A running Filekin
instance locks the normal Release output, so ask the owner to close it rather than killing an unknown
instance. For the current phase, rebuild the normal Release executable and let the owner perform the
final UI interaction pass.

SQLite-backed test fixtures are deliberately `DoNotParallelize` because their temporary-database
cleanup calls the process-wide `SqliteConnection.ClearAllPools()`. Keep unrelated tests parallel; do
not remove that isolation without replacing the global cleanup boundary.

## Other open product questions

These do not block `/history` and `/undo`:

- Should the Files command-bar runspace load the user's PowerShell profile or remain clean/predictable?
- What terminal text should be exposed to assistive technology in v1?
- How should a selected file path be copied before the context-menu/clipboard work exists?
- How should terminal-tab overflow work?
- Should hosted terminal profile loading become a user setting?
