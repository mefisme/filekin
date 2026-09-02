# HANDOFF.md — Filekin

## Purpose

This is the short live state shared by coding agents: current phase, exact next task, genuine blockers,
load-bearing contracts, and known problems. Git and `HANDOFF-ARCHIVE.md` hold finished history. The
master specifications own settled product behavior. Keep this file under 500 lines; do not turn it
into a changelog, test ledger, implementation diary, or duplicate specification.

Read `AGENTS.md` and `ENGINEERING-GUARDRAILS.md` first, then the specifications relevant to the task.

## Current phase

Production implementation is focused on cooperative Agent Projects navigation, explicit work modes,
and the remaining Control Room lifecycle QA. `/history` and `/undo` remain paused. The working tree
contains Claude's uncommitted control-room, terminal-session, provider-lifecycle, project-navigation,
test, and documentation work; preserve it.

Implemented before this checkpoint:

- File hierarchy, persistent Files PowerShell command bar, and ConPTY terminal tabs.
- Confirmed v1 slash surfaces and filesystem commands listed in `FEATURES.md`.
- Provider-neutral agent coordinator, SQLite state, project-scoped MCP, Codex App Server and Claude
  background adapters, subscription usage ingestion, cooperative lease/turn handoff, and `/agents`.
- Live Codex → Claude → Codex relay, provider launches, and Claude usage reporting have passed against
  the owner's subscriptions. Live probes remain explicit and gated.

## Exact next task — Remaining Control Room lifecycle QA

Finish the current control-room checkpoint; do not start roles, bootstrap, `/history`, or `/undo`.

The headless relay is proved and is no longer the open question; the app surfaces around it are.
`LiveTenEntryRelayTests` passed on 2026-09-02 in 13m45s: twenty entries, Claude opening, Codex
reporting both objectives done, `StartNewObjective` between them, nothing pressed by a person. It
also passed Codex-first. Prefer that test over a manual relay for anything the engine can answer:
`FILEKIN_RUN_LIVE_TEN_ENTRY_RELAY=1`, with `FILEKIN_LIVE_RELAY_STARTER`, `_JOBS`, `_CODEX_MODEL`,
`_CLAUDE_MODEL`, `_EFFORT`, `_STALL_MINUTES`, `_JOB_MINUTES`. On modest models a run costs about one
credit, so it is cheaper than the manual pass it replaces. Run it detached: a run that dies with the
shell that launched it leaves a Claude session working and spending.

1. The manual `/append-test` rerun is now only for what the harness cannot drive: it goes through the
   app's own control room, its buttons, and its CLI tabs. The engine behaviour it used to prove —
   reserved lease before the prompt runs, recipient waiting for handoff assignment, a Claude `done`
   turn staying live until its pid is cooperatively stopped, ten alternating entries — is covered by
   the live test above.
2. Let the owner exercise `/projects`: its sidebar entry appears only after a project exists, the rich
   view lists **FOLDER · CONNECTION · WORK · AGENTS · USAGE LEFT**, and click/Enter opens the selected
   folder's control room without starting a provider. Saved rows appear first; live connection facts
   may refresh asynchronously through provider inspection. Invoking `/projects` with no saved projects
   shows the empty list and must not create `state.db`.
3. Exercise setup with **Use app settings**, **Plan / read-only**, and **Trust (auto)**. Confirm the
   recorded answer and explanation remain visible, **Change** offers the same ordered choices while
   nothing runs, and Change is visibly disabled while any agent session is live.
4. Then continue the terminal lifecycle pass below. Never start a second client on a Codex thread still
   owned by Filekin's App Server.

5. With a saved stopped **Codex** conversation whose session has run in this window, exercise
   **Resume CLI** and prove that the CLI opened in the special terminal tab still has this project's
   Filekin MCP identity and that clock-in, messages, turn ownership, handoff, stop, and completion
   still behave correctly. This is the main
   unproved path. Never start a second client on a Codex thread still owned by Filekin's App Server.
6. Exercise both providers through: start, idle, CLI open, terminal-tab close, End, Filekin close,
   Filekin reopen, and project-tab close/reopen. Verify the UI and persisted lease/session state tell
   the truth after each transition.
7. Verify the Filekin close overlay with terminals only, agent sessions only, both, provider-count
   failure/unknown, **Keep agents running**, **End agent sessions and close**, and Cancel. A terminal
   ends with the window; a provider background session may not.
8. Recheck the control-room wording and enablement in every state. Labels and status sentences must
   agree; disabled controls must visibly dim and lose the pointing-hand cursor. **End** is enabled only
   when a live session exists. Every row states CONNECTION and WORK separately, and the CLI control
   says **Open CLI**, **Resume CLI**, or **Go to CLI tab** for what it will actually do now.
9. Exercise the one-scroll control-room layout at short and tall window heights. **Activity log** is
   collapsed when a project tab first opens, expands inline without a modal or second scrollbar, does
   not force itself open for new events, and remembers its state per project tab within the window.
10. Let the owner perform the final interaction pass. Record only conclusions or remaining faults here.

No API billing, credits, `-p`, Agent SDK, `bypassPermissions`, terminal injection, screen scraping, or
automated foreground input is authorized by this test.

### Review findings still open

A full review of `Filekin.Core/Agents`, `Filekin.Infrastructure.Windows/Agents` and `Filekin.Mcp` on
2026-09-02 fixed four faults that could each stall a relay on their own (a waiting `read_state`, a
stale connected flag on Start, a late handoff erasing an accepted one, and an unwatched stop released
on the asking). These were found in the same pass and left alone deliberately, none of them able to
stall a relay:

- `NativeAgentSessionLauncher.ListClaudeBackgroundAgentsAsync` turns every failure into an empty list,
  so a Claude CLI that cannot answer reads as "nothing running". That defeats the duplicate-session
  guard in `EndSessionFilekinLostTrackOfAsync` and makes `AgentSessionLiveness.Unknown` unreachable.
- `NativeAgentSessionLauncher` persists `ConversationSessionId ?? NativeId`, so a listing taken before
  Claude populates `sessionId` stores the short attach handle as the conversation id. Attach is then
  refused, `--resume` fails, and the fallback quietly starts a new conversation, losing that agent's
  memory without anybody choosing it.
- `AgentRunService.CountLiveSessionsAsync` disposes its `SemaphoreSlim` while other checks may still
  hold it, so a faulted check throws `ObjectDisposedException` on an unobserved task during close.
- `AgentCoordinationRuntime.RefreshAsync` records "unavailable" with a token that may already be
  cancelled, turning a recoverable provider failure into a cancellation escaping the refresh.
- `AgentProjectCoordinator.ChooseModel`, `RecordNativeSessionAsync` and `ClearNativeSessionAsync` omit
  the `Enum.IsDefined(provider)` guard their neighbours have.
- `RecordUsageLimit` takes `participant.NativeSessionId ?? nativeSessionId`, and
  `filekin_report_usage_limit` is model-callable, so where Filekin has recorded no identity the model
  supplies one. Native session identity is meant to be app-owned.

## Current Agent Control Room state

- `/agents` opens/selects one persistent `Agents · <folder>` task tab per canonical folder. Files stays
  permanent; several project tabs/runtimes may coexist. Closing a project tab closes only the view—it
  never stops providers, releases a turn, or deletes the project.
- Setup is explicit and writes nothing into the project folder. It stores the folder, objective, and
  exact shared-checkout consent in transactional app state; no turn is granted until **Start work**.
- Once a project exists, `/projects` is a passive list of every saved agent folder. Its sidebar entry is
  conditional, and opening the list never starts providers; live connection facts may refresh through
  provider inspection after the persisted rows appear. A direct empty `/projects` read uses the
  non-creating existence check and leaves `state.db` absent.
- Work mode is one of **Use app settings** (no override), **Plan / read-only** (Claude `plan`, Codex
  `readOnly`), or **Trust (auto)** (Claude `auto`, Codex `workspaceWrite`). It is persisted, shown in the
  control room, and changeable only while no agent session is live.
- Only one agent owns the working-tree lease and active turn. A handoff submission alone never releases
  it; only app-owned provider-stop confirmation can transfer/release it. Stop keeps the project and is
  a resumable pause.
- A handoff recipient may be launched just before the stopped sender's lease is transferred so it can
  prove connection. Its `filekin_clock_in`, `filekin_read_state`, and `filekin_accept_handoff` calls
  wait for that transfer; otherwise a fast recipient can mistake the sender's lease for a block and
  abort a valid relay.
- The control room owns coordination facts: objective, connection, provider allowance windows, active
  agent, lease, messages, handoffs, and Start/Pass/Stop/End actions.
- **USAGE LEFT** is stated only by the column heading. A provider row contains the reading alone; when
  Codex returns account-wide quota families, Filekin presents only the `codex` family's five-hour and
  weekly windows. Other feature families are not Codex allowance and are never guessed to be credits.
- Objective text becomes a user-owned draft on the first edit. Passive project refreshes, completion
  refreshes, and an older asynchronous load/save result preserve that draft; each project tab remembers
  both its text and whether it is unsaved. **Save objective** is enabled only for changed, nonblank text.
  The coordinator also rejects an empty new objective, so presentation cannot bypass the rule.
- The control room is one vertically scrolling page. Historical events are behind a collapsed-by-default
  **Activity log** disclosure at its bottom; expansion lengthens the same page rather than opening a
  modal or nested scrollbar. Expansion is remembered per project tab for the life of the window and
  new events never force it open or move the page viewport.
- There is no custom Agent Session view. The provider-specific CLI action opens the exact native
  conversation in a specially marked ordinary Filekin terminal tab. The provider CLI owns transcript,
  questions, approvals, and `/clear`; Filekin does not emulate or scrape it.
- Claude attaches to its live background session. Codex resume starts a new CLI process and must carry
  the project MCP overrides; it is refused while Filekin's private App Server still owns that thread.
- Claude permission settings authorize Filekin's coordination server with `mcp__filekin`. Claude does
  not support MCP permission globs; `mcp__filekin__.*` leaves a headless session blocked asking to
  approve `filekin_clock_in` while it spends tokens.
- A resumed Codex terminal registers the saved project/provider/session identity before launch. It is
  therefore the existing worker, blocks a second Continue launch, and reconciles presence/lease when
  its terminal closes without clearing conversation memory. **End** closes that exact Codex terminal
  because Codex exposes no cooperative session-stop command.
- When an attached CLI exits back to PowerShell, a private terminal command-completion signal removes
  the agent identity, reconciles the control room, and leaves the tab open as an ordinary shell. This
  is synchronization, not terminal output parsing or input injection.
- Closing an attached terminal ends its root shell after confirmation. For Claude, closing the attach
  frontend does not itself end the background session; **End** uses Claude's cooperative stop.
- Filekin discovers live Claude sessions it stopped watching. Codex orphan discovery does not exist
  because the current private App Server is Filekin's child and cannot outlive it.
- The close flow asks providers what remains live across all saved projects, with a bounded wait. It
  does so after reopen even if `/agents` was not opened in the new window, never treats unknown as
  zero, and never claims sessions ended after a timeout/failure. Offline saved projects take the
  zero-provider-call path and do not construct the coordination runtime. Current-window handles count
  directly; only persisted plausible Claude presence needs an external probe because Codex inspection
  processes cannot outlive Filekin. Runtime shutdown cancels outstanding provider reads, releases
  independent session/source handles concurrently, and shares one account-level Codex usage client.
- Claude `state: done` with a pid is a finished turn, not a stopped session. It remains connected and
  watched, triggers the close overlay, and keeps the lease until Filekin's cooperative stop is proven;
  only a terminal lifecycle without a pid may release it.
- Each agent row states two facts in two headed columns: **CONNECTION** (`Running`, `Not connected`,
  `No answer`) and **WORK** (`Not started`, `Stopped`, `Waiting`, `Working`, `Handing over`,
  `Finishing`, `Done`, `Needs you`, `Stopping`). Running is read from live sessions, never from stored
  connection state, which can outlive the window that wrote it. The status sentence uses the same
  words as the rows. Unwatched-session inspection returns explicit `Running`, `NotRunning`, or
  `Unknown` liveness. A failed Claude check replaces any prior Running answer with **No answer** and
  explains **Couldn't check whether Claude Code is still running**; it never preserves stale success
  or claims the session stopped. The `/projects` aggregate uses the same honest result.
- `Done` belongs to the agent that finished the turn, never to the whole row set: an agent that took
  no turn says `Not started`, and one that took a turn and stopped says `Stopped`. That difference is
  the persisted `AgentParticipant.HasWorkedOnObjective`, which `Activate` sets and a new objective
  clears; a saved conversation cannot answer it, because it is memory of any job in that folder.
- The start action says what pressing it does now: **Start work** when nothing is running (it still
  carries a saved conversation on) and **Continue** only while a session is running. On a finished job,
  the whole Start/Pass/Stop row is hidden until **Save objective** records valid next work; the status
  says **Finished. Enter the next objective to start again.**
- Start responds immediately with the actual stages in the status band and global status. It reuses
  persisted allowance still within `MaximumUsageAge`, reads stale/missing providers concurrently, and
  reserves the selected provider's single writer lease before its work-capable prompt is launched.
  Clock-in atomically changes that reservation to Working; launch failure may abandon it only before
  clock-in. No duplicate provider preparation runs afterward, and each provider launch still
  independently proves subscription authentication.
- The CLI control names its current action: **Open CLI** while running, **Resume CLI** only where
  resuming is possible, **Go to CLI tab** when this window already has it open. Codex resume needs an
  unfinished job whose session has run in this window; a freshly started Filekin offers **Continue**
  instead, which carries the same saved conversation on. The shared Filekin button template owns
  disabled, hover, focus, and cursor visuals.

## Load-bearing agent contracts and traps

### Provider and lifecycle

- Each unmodified native tool authenticates to the user's own subscription. Filekin stores no secrets,
  pays for/resells/intermediates no usage, never enables metered overage, and fails closed when Claude
  settings or environment could redirect billing/authentication.
- Filekin reads provider-reported non-secret allowance. Missing/stale data is unknown, never zero.
  Separate windows remain separate; a window past its reset time counts as full again.
- Unknown allowance does not block a cold start; fresh exhausted allowance does unless the owner opted
  into low-allowance work. Automatic handoff asks early only when the partner has safe headroom.
- Provider stop without a structured handoff becomes `NeedsAttention`; never activate the partner with
  guessed context. A user-requested stop never becomes a handoff even if the agent submits one.
- Doing the work is not finishing the turn. An agent that stops mid-objective without submitting a
  handoff leaves a project that is idle, unowned, and indistinguishable from a finished one, which is
  how a relay dies in silence. `AskForTheMissingHandoffAsync` restarts that same agent once, in its
  own conversation, and names only the step it skipped; it never writes the handoff or starts the
  partner on a guess. A second miss, or a reminder that cannot be launched, becomes
  `MarkStoppedWithoutHandoff` with the real reason. One reminder per agent per turn: a real handoff
  clears the budget, and a project that is `StopPending`, `CompletionPending`, or already waiting on
  a person is never restarted.
- The instruction to end a turn with `filekin_submit_handoff` must stay in a project's own `AGENTS.md`
  and `CLAUDE.md`. Tool descriptions say what each tool does; nothing else tells an agent that ending
  a turn *requires* one of them first. Deleting that rule as "redundant" silently broke the live relay
  on 2026-09-02: Codex appended its entry, stopped without handing over, and the project went quietly
  to `Ready`.
- Native session identity is app-owned. `filekin_clock_in` reports presence and accepts no session id.
  A lifecycle callback may establish identity only when none exists.
- The agent holding the turn may submit a handoff without being asked. The reason remains Filekin's
  fact: Filekin's pending reason wins; an unsolicited one is `WorkCompleted`.
- The working-tree lease is cooperative state, not an OS lock. Parallel writers are out of scope; any
  future parallel mode requires separate Git worktrees.

### Session mechanics

- A leftover `Filekin.Mcp.exe` is a symptom, not the process to kill. A live Claude background session
  respawns it. Find and stop the parent session. `claude stop` can briefly report stopped and then be
  respawned, so Filekin requires two stable polls before releasing the lease.
- Rebuild `src/Filekin.Mcp/bin/Release/net10.0-windows/Filekin.Mcp.exe` after Core changes before live
  testing. A stale companion silently serves old rules.
- Claude has two ids: stored conversation `sessionId`, and the short `id` used by `attach`, `logs`,
  `stop`, and `rm`. Resolve them with `claude agents --json`; never infer one from the other.
- Claude liveness is `pid`, not `state`; `state: done` means the turn ended while the background session
  can remain alive and idle.
- Claude's `state` and `status` disagree at the end of a turn. A finished background turn is observed
  as `state: working` with `status: idle` and a live pid — not as `state: done`. Mapping lifecycle from
  `state` alone classifies that as Working forever, so the idle-stop path never runs, the writer lease
  is never released, and a submitted handoff sits in `HandoffPending` while the partner is never
  started. `MapLifecycle` therefore treats `status: idle` with no `waitingFor` as Idle whatever `state`
  says. `ClaudeSessionHandle` needs two consecutive idle reads before asking Claude to stop, because a
  session that has its prompt but has not begun answering also reads idle for one poll.
- `ClaudeSessionHandle` intentionally asks an idle session to stop so a finished turn releases its
  lease. An unwatched session is therefore discovered, cooperatively ended, then resumed by conversation
  id rather than adopted through a handle that would immediately stop it.
- `AgentSessionAttachCommand` accepts only the providers' hex-and-dash id shape. Claude uses
  `claude attach <id>`. Codex uses `codex resume` plus the exact project MCP config; never resume a live
  App Server thread as a second client.
- Normal terminal behavior is unchanged. Agent terminal tabs are real ConPTY terminals and Filekin
  claims only the existing terminal shortcuts; it never intercepts their ordinary keys.
- Root-shell startup scripts use PowerShell `-EncodedCommand`; raw `-Command "..."` loses embedded
  quotes through Windows argument parsing and corrupts Codex's TOML array overrides.

### Architecture and persistence

- `Filekin.Core` contains no WPF, provider SDK, process, JSON-RPC, or MCP implementation types.
  Provider results become provider-neutral immutable values at the infrastructure boundary.
- `AgentRunService` alone starts/stops provider work. `AgentCoordinationRuntime` owns transactional
  coordination and never dispatches a native turn.
- `AgentCoordinationRuntime.StartAsync` completes restart reconciliation before preparation, MCP config,
  or lease changes. Ordinary Filekin startup reconciles only; it never dispatches work or asks consent.
- MCP processes receive project/provider identity at launch. Tools cannot change those identities,
  expose native ids, or perform restart reconciliation.
- `StateDatabase.SchemaVersion` is the one `PRAGMA user_version` for coordination and operation history,
  now 9 for `agent_participants.has_worked_on_objective`. Preserve migrations and the
  writer-reservation-before-read rule; do not assume `CREATE TABLE IF NOT EXISTS` adds columns.
  `AddMissingColumnAsync` answers whether it added one, so a backfill runs once: that column is filled
  from a saved conversation rather than guessing that finished work never started.
- Claude status-line mode writes quota observations only and verifies the project folder. It never writes
  participant, lease, session, or turn state.
- The in-turn refresh is one-shot and rearms after a tick. Keep it shorter than `MaximumUsageAge`;
  overlapping periodic ticks are forbidden. Dispose by cancelling/draining the tick before taking the
  operation gate or it deadlocks.
- The opening prompt is deliberately minimal. Coordination rules live in MCP tool descriptions so the
  user does not repeatedly pay for duplicated instructions.

## Unbuilt/open agent decisions

- **Questionable/proposed, not decided:** replace the conditional `/projects` sidebar entry with one
  always-available `/agents` entry. The sidebar entry and bare `/agents` would open the Agent Projects
  rich overview; `/agents <single-folder-target>` would open that folder's existing control room or its
  explicit setup. Candidate targets are a path, `@thisfolder`, a saved `@Location`, or `@selection`
  only when the full selection is exactly one directory—never reinterpret files or multiple selections.
  The overview could offer a clearly named **New project** action for the current Files folder rather
  than an unexplained icon-only plus. This redesign still needs an inactive-row retirement action:
  decide archive/hide versus deletion of coordination state, what happens to consent/history/native
  conversation references, whether `/projects` remains an alias, and whether returning later creates
  a fresh project. Any destructive action must be unavailable while a provider session or working-tree
  lease may still exist. Current confirmed specs retain context-sensitive bare `/agents` plus `/projects`
  until the owner decides this replacement.
- Whether work mode remains one project-wide setting or becomes per-agent, allowing Codex and Claude
  to run under different modes. The current persisted `SharedCheckoutConsent.WorkMode` applies the same
  selected mode to both providers; do not add per-agent controls until the owner decides.
- Consider making live agent CLI tabs hideable rather than closeable: use an explicit **Hide CLI**
  control instead of a misleading X, let **Show CLI** reveal the same terminal, and reserve **End** for
  terminating the provider. This would protect an active Codex task from accidental tab closure but
  would not change provider-owned Ctrl+C or `/exit`, or the Filekin shutdown decision.
- Optional per-agent role lines.
- Previewed project bootstrap: existing projects default to no writes; empty folders may be offered
  `.filekin/PROJECT.md`, `AGENTS.md`, and `CLAUDE.md`, never a competing `HANDOFF.md`.
- Whether Codex should move from Filekin's private App Server to `codex app-server proxy` and the shared
  daemon, allowing Codex sessions to outlive Filekin and appear to other Codex clients.
- Durable project-context location, readable handoff export policy, later `/agents` management grammar,
  and the production allowance thresholds (current defaults are floor 10%, request 30%).
These do not block the lifecycle checkpoint. Do not invent their UI.

## Paused task — `/history` and `/undo`

The durable journal and platform-neutral undo coordinator are implemented and intentionally paused.
When resumed:

1. Implement Move/Toss Replace, Keep Both, Skip, Cancel, and bulk Apply-to-All with exact retry state.
2. Compose `OperationUndoCoordinator` in `ShellViewModel`, route archive result-line Undo through it,
   and add `/undo` newest-safe selection without building the history UI yet.
3. Build `/history` with persistent rows, present-state explanations, and exact-entry Undo/Restore.
4. Finish prompts/keyboard behavior, rebuild Release, and use a focused manual WPF pass.

Standing resume facts:

- SQLite retains 50 top-level app operations and demotes prior-session undo promises on restart.
- One invocation is one entry. Record only app mutations that occurred; shell/terminal and agent edits
  are excluded. Copy/Tidy are informational; Move/Rename, exact Toss, Zip, and Unzip are session-undoable.
- `/undo` selects the newest currently safe app operation; `/history` may act on any currently safe
  current-session row. Both reevaluate present safety immediately before execution.
- Undo never silently overwrites. Collisions offer Replace, Keep Both, Skip, or Cancel; Replace is not
  default. Edited archive output offers Keep Edited (safe default), Recycle Edited, or Cancel, bound to
  the reviewed fingerprint. Partial outcomes stay partial with exact pending work.
- `OperationUndoCoordinator` is the authoritative exact-entry boundary; no app path uses it yet, and
  result-line archive Undo still uses the legacy executor.

The detailed settled behavior remains in the master specs; implementation history is archived.

## Cross-product standing contracts

- From a focused terminal, Filekin claims only Ctrl+Tab/Ctrl+Shift+Tab, Ctrl+Shift+T, and Ctrl+Shift+W.
  Ordinary Tab, arrows, Escape, Ctrl+C without a selection, and Y/N belong to the shell.
- Space returns non-text Files/rich-view focus to the command bar. Sidebar Up/Down highlights without
  navigation; Enter navigates. Esc returns to Files.
- Terminal scrollback metrics flow one-way from `TerminalControl` to its scrollbar. Only an explicit
  scrollbar `Scroll` event changes the viewport; TwoWay binding causes a tab-realization feedback loop.
- `/run` is the only launch command. Known `@` references beat PowerShell splatting; unknown `@` passes
  through. Terminal input receives no slash/reference preprocessing.
- Sidebar is not an Explorer tree. Do not add Quick Access, This PC, automatic special folders,
  expandable Drives, visible Back/Forward buttons, or speculative navigation chrome.
- Files Back/Forward keyboard/mouse navigation is deferred and distinct from `/history` operation state.
- `WindowsUserEnvironmentWriter` must preserve expandable registry strings and use bounded broadcast;
  do not replace it with `Environment.SetEnvironmentVariable`.
- `/where` alias learning remains bounded: Query-strength paths only; executable shortcut targets only;
  short words require complete names; inside-name aliases require joined names of at least six chars;
  exclude publisher/architecture/folder-role words; scan plugin/cache roots only below matched programs.
- Terminal layering remains raw bytes in `ITerminalSession`, VT state in `TerminalEmulator`, drawing/input
  in `TerminalControl`, tab state in `TerminalTabViewModel`, and collection/selection in `ShellViewModel`.
  Drain output through teardown and close ConPTY only after graceful shutdown attempts.

## Current known problems

- **An open Codex CLI stops the relay, and every word Filekin says about it is wrong.** Proved by hand
  on 2026-09-02 in `D:\GitHub\agent-test`. Press **Resume CLI** on Codex while Claude holds the turn,
  touch nothing, and let the handoff arrive:
  1. The resumed terminal registers as Codex's session, so the row reads **Running · Waiting**, but the
     agent never called `filekin_clock_in`, so the coordinator still has it `Offline`. A running process
     and a clocked-in participant are different things and the control room shows only the first.
  2. The handoff cannot be delivered, and the project pauses saying *the handoff recipient does not have
     fresh, known usage above the safety threshold*. Allowance has nothing to do with it. Any recipient
     that cannot be reached is reported as a usage problem (`AgentProjectCoordinator` unsafe-recipient
     branch), which sends a person to a quota screen over a CLI they opened.
  3. The start control offers **Continue**, because a session is running. Pressing it fails with *at
     least one agent must clock in before Filekin selects the first turn*: `GiveInitialTurnAsync` asks
     the coordinator to select a turn, and selection refuses while nobody is clocked in.
  4. Closing the tab recovers completely — Codex starts, clocks in, and the written handoff is still
     delivered — so nothing is lost, but the way out is never stated.

  The fact underneath: a resumed CLI is a separate `codex resume` process, human-driven, and Filekin
  cannot dispatch a turn into it. While that tab is open the relay genuinely cannot continue by itself.
  That is defensible; saying nothing true about it is not. Any fix states the real cause where the
  person is looking, and `Continue` must either mean something here or not be offered.
- Accessibility is the largest general gap: Files/sidebar automation names expose view-model type names,
  and terminal text is not usefully exposed.
- There is no `Filekin.App` test project. Keep platform-neutral lifecycle logic in Core/Infrastructure;
  use a small manual pass for the close overlay, focus, styling, and other WPF-only behavior.
- Terminal selection is drag-only; tab overflow and header Left/Right navigation are unresolved.
- Files selection is not preserved across re-sort. Esc stops a running command only while the command
  bar has focus.
- `/drives` omits MTP devices; network mappings may need window reactivation before refresh.
- Command classification is whitespace-tokenized, not quote-aware, though raw input executes unchanged.
- Recycle Bin Restore/Delete verb matching is English-only. Interactive Recycle Bin tests stay excluded
  from hosted CI and must not be weakened.

## Validation

```text
dotnet build Filekin.sln -c Release -m:1 --no-restore
dotnet test Filekin.sln -c Release --no-build --no-restore -m:1
dotnet format Filekin.sln --verify-no-changes --no-restore
git diff --check
```

CI excludes `TestCategory=RequiresInteractiveShell`; desktop runs do not. Properties-dialog tests require
`FILEKIN_RUN_SHELL_DIALOG_TESTS=1`. SQLite fixtures are `DoNotParallelize` because cleanup calls the
process-wide `SqliteConnection.ClearAllPools()`.
