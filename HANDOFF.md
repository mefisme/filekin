# HANDOFF.md — Filekin

## Purpose

This is the short live state shared by coding agents: current phase, exact next task, genuine blockers,
load-bearing contracts, and known problems. Git and `HANDOFF-ARCHIVE.md` hold finished history. The
master specifications own settled product behavior. Keep this file under 500 lines; do not turn it
into a changelog, test ledger, implementation diary, or duplicate specification.

Read `AGENTS.md` and `ENGINEERING-GUARDRAILS.md` first, then the specifications relevant to the task.

## Current phase

Production implementation is focused on cooperative Agent Projects navigation, explicit work modes,
and the remaining Control Room lifecycle QA. `/history` and `/undo` remain paused. The tree is clean as
of 2026-09-03 apart from this checkpoint's own change; the full validation block below passes.

Implemented before this checkpoint:

- File hierarchy, persistent Files PowerShell command bar, and ConPTY terminal tabs.
- Confirmed v1 slash surfaces and filesystem commands listed in `FEATURES.md`.
- Provider-neutral agent coordinator, SQLite state, project-scoped MCP, Codex App Server and Claude
  background adapters, subscription usage ingestion, cooperative lease/turn handoff, and `/agents`.
- Live Codex → Claude → Codex relay, provider launches, and Claude usage reporting have passed against
  the owner's subscriptions. Live probes remain explicit and gated.

## Start here — 2026-09-03 checkpoint

Nothing in the tree is unverified. Build green, 995 tests pass, `dotnet format` clean. The engine is not
the open question; the app surfaces are, and most of what remains needs a person pressing things.

```text
FILEKIN_RUN_LIVE_TEN_ENTRY_RELAY=1 FILEKIN_LIVE_RELAY_JOBS=1 dotnet test tests/Filekin.Infrastructure.Windows.Tests -c Release --no-build --filter TheRelayReachesTenEntriesWithoutAnybodyPressingAnything
```

Filekin must be closed to build, or it holds its own DLLs. Run any live test **detached**
(`Start-Process`, redirected output): a run that dies with the shell that launched it leaves a Claude
session working and spending, observed twice.

### What is proved

- `LiveTenEntryRelayTests` passed three times on 2026-09-02: Codex-first, Claude-first with two jobs
  (twenty entries, 13m45s), and once more at 5m5s against the clock-in change. No human input, and no
  session left behind afterwards.
- **Resume CLI on Codex is proved**, by hand, in the app. It was the main unproved path. A resumed CLI
  keeps its conversation memory *and* this project's MCP identity: the tab called
  `filekin_coordination_<projectId>.filekin_read_state` and got live state back. The identity travels on
  the `codex resume` command line, and it survives.

### Watch the allowance before planning live QA

Codex's weekly window was fully spent on 2026-09-02 (`codex:secondary` 100% used, resets 2026-09-07).
Claude had room. A relay that stalls on Codex before then is a real allowance stop, not a fault. The
project has **Work even when little usage is left** ticked, so Filekin will still start it.

### The trap that cost the most time today

An agent will do the *whole job in one turn* if the objective's finish condition does not require
alternation. Given "append one entry per turn, then hand over … finished when the file holds 10
entries", Claude wrote all ten itself and reported completion. Filekin cannot prevent this: the lease
decides **whose** turn it is and nothing decides **how much** a turn contains. Rewriting the finish
condition as "finished when the file holds 10 entries **and the names alternate every line**" fixed it
immediately — the agent obeys the condition it is measured by, not the prose beside it. This is the
strongest evidence yet for the hook decision in `DECISIONS.md`.

## Exact next task — Run the Control Room lifecycle QA below

The presentation work is done. `tests/Filekin.App.Tests` now covers `AgentParticipantViewModel`,
`AgentProjectRowViewModel`, `ReattachAgentCliTabsAsync`, and the Start/Pass/Stop labels, enablement
and status sentence. What is left on this surface genuinely needs a person pressing things, so work
the QA steps below. Everything that needs only Claude, or no agent at all, can run now; the Codex half
waits for the allowance to reset on 2026-09-07.

**How to show a control room in a test, since the last agent had to work it out.** There is no seam
and none is needed: build an `AgentProjectTabViewModel`, set its internal `Project`, add it to
`AgentProjectTabs`, and call the public `SelectAgentProjectTab`. That is the path `ViewAgentWork`
takes, so it proves the real one. `ShowAgentProject` then runs with no store and no runtime behind
it — `RefreshUnwatchedSessionsAsync` returns at once without `_agentRun`, and the watch timer never
ticks without a dispatcher loop. `AgentProjects` in the test project builds every state through the
coordinator's own public transitions, and `FakeTerminalSession` gives `AddTerminal` something with no
ConPTY behind it.

**What cannot be tested this way, and why.** `/projects` (`ShellViewModel.Projects.cs`) needs the
store, and `AgentRuntimeAsync` hard-codes `new SqliteAgentProjectStore()` on its default `%APPDATA%`
path. A test of that surface would write to the owner's real `state.db`. Give the store path an
injection point before writing those tests; do not point a test at the live database.

**One thing worth knowing before trusting a row.** A clock-in carries no allowance, so an agent that
has reported in but has not been read yet is `UsagePending`, and `IsCliTabOpenButNotReportedIn` counts
that as not reported in. It errs towards the disabled control, which is the safe way round, and
`AnAllowanceReadingIsWhatTellsThoseTwoStatesApart` pins it. Do not "fix" it into a Ready check without
deciding what a start should do for a session Filekin does not hold.

### Still outstanding — Control Room lifecycle QA

The QA pass below is partly done and partly blocked. Codex's weekly allowance reached zero on
2026-09-02 and resets 2026-09-07, so step 5 and the Codex half of steps 4 and 6 cannot run until then.
Everything that needs only Claude, or no agent at all, can. Resume it when the allowance returns.

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

   **Filekin cannot remove a saved project**, so the empty case cannot be reached through the app.
   Found on 2026-09-02 while running this step. Nothing deletes a project in code, and no
   specification asks for one — `FEATURES.md` and this file only say what does *not* remove a project.
   Test the empty case by closing Filekin and moving `%APPDATA%\Filekin\state.db`, `-shm` and `-wal`
   aside together, then moving all three back; the `-wal` file carries unwritten changes, so moving
   one without the others corrupts the set. Whether removing a project should exist at all, and what
   it does to a folder that agents have worked in, is an owner decision that has not been made.
3. Exercise setup with **Use app settings**, **Plan / read-only**, and **Trust (auto)**. Confirm the
   recorded answer and explanation remain visible, **Change** offers the same ordered choices while
   nothing runs, and Change is visibly disabled while any agent session is live.
4. Then continue the terminal lifecycle pass below. Never start a second client on a Codex thread still
   owned by Filekin's App Server.

5. **Resume CLI on Codex is proved** for identity and tools — see *What is proved*. What is still
   unexercised through a resumed tab is the rest of the cycle: messages, turn ownership across a full
   handoff, stop, and completion. Never start a second client on a Codex thread still owned by
   Filekin's App Server.

   "Messages" here means the ones agents send each other through their own coordination tools, driven
   from inside the resumed tab; the control room shows them and has no box for sending one. A person
   cannot message an agent at all: `AgentRunService.SendPromptAsync` exists and is tested, but nothing
   in `Filekin.App` calls it, and no specification asks for that control. Whether a person should be
   able to say something to a working agent is an owner decision that has not been made — read this as
   a missing surface, not as dead code to delete.
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

**What the QA now covers that it did not before.** *A CLI a person is reading survives a handoff* was
merged on 2026-09-02, so a session outliving its turn is normal rather than a fault. Codex keeps its
CLI open across a handoff and takes its next turn in place; Claude is stopped and resumed with its
memory kept, and the tab it was showing is put back on the resumed session. Check that, and check that
a tab nobody opened is never created and a tab somebody closed stays closed.

## Next topic to discuss — two surfaces that do not exist

Both were found on 2026-09-02 while writing the QA steps for them, and neither is a bug: nothing in
the code does it and no specification asks for it. Each needs an owner decision before any work, and
neither should be built on a guess.

1. **Removing a saved agent project.** There is no way to remove one, so the empty `/projects` case
   cannot be reached through the app at all. The open questions are whether removing should exist,
   what it does to a folder agents have already worked in, and whether it may run while a session is
   live. `FEATURES.md` and this file currently say only what does *not* remove a project.
2. **Saying something to a working agent.** `AgentRunService.SendPromptAsync` exists and is tested, and
   nothing in `Filekin.App` calls it. The control room shows the messages agents send each other and
   offers no way for a person to send one. The open questions are whether a person should be able to
   interrupt a turn at all, what it means for the lease, and whether it is a message or a prompt.
   Treat the unused method as a missing surface, not as dead code to delete.

### Codex's shared daemon is not available on Windows

Tested 2026-09-02 with `codex-cli 0.152.1`, without spending a model turn. `codex app-server daemon
start` refuses with *lifecycle is only supported on Unix platforms*, and `codex app-server proxy`
fails trying to open a Unix domain socket under `~/.codex/app-server-control/`. The daemon is the
recorded root fix for an open Codex CLI stalling a relay, so that stall has no fix available here
today: closing the tab and pressing **Continue** remains the whole answer, and the control room
already says so. The full evidence and what the protocol would allow are in `DECISIONS.md`. Do not
re-run this gate on Windows until Codex ships the daemon for it; check `codex app-server daemon
version` first, and if it answers instead of refusing, the gate is live again.

### Review findings — all closed

The 2026-09-02 review of `Filekin.Core/Agents`, `Filekin.Infrastructure.Windows/Agents` and
`Filekin.Mcp` is fully worked off. The four faults that could stall a relay were fixed that day, as
were the two in this checkpoint's own QA path. The last four are now fixed too; none of them could
stall a relay. Build green, 995 tests pass, `dotnet format` clean.

One of the four changed confirmed behavior and needed the owner's decision, so it is recorded here
rather than left as a diff to rediscover: **`filekin_report_usage_limit` no longer establishes a
native session identity.** `AgentProjectCoordinator.ReportUsageLimit` took
`participant.NativeSessionId ?? nativeSessionId`, and that tool is one a model can call, so where
Filekin had recorded no identity the caller named it — and a recorded identity is what later decides
which conversation a resume reopens. The identifier is still required and length-checked at the tool
boundary, because Claude's `rate_limit` hook sends one; it is simply never stored. The limit report
itself is unchanged: Unavailable, Blocked or Waiting, status, and attention reason all still apply.
`FEATURES.md` was corrected in the same change, because it had specified the old behavior.

Two names in the closed findings were wrong and cost a search: the semaphore fault was in
`AgentRunService.CountLiveProviderSessionsAsync`, not `CountLiveSessionsAsync`, and the coordinator
method is `ReportUsageLimit`, not `RecordUsageLimit`.

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
  prove connection. `filekin_clock_in` and `filekin_accept_handoff` wait for that transfer, because a
  fast recipient would otherwise mistake the sender's lease for a block and abort a valid relay.
  `filekin_read_state` deliberately does **not** wait: it is the call every agent is told to repeat
  while it works, and waiting there blocked the agent doing as it was told — one that had submitted a
  handoff and was still working — then answered a routine question with a timeout.
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
  carries a saved conversation on) and **Continue** only while a session Filekin can give a turn to is
  running. A CLI tab a person opened is not one: while the agent a start would use is running only as
  that tab and has not clocked in, the control is disabled and the surface says to close the tab. On a finished job,
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
- `Filekin.App` grants `InternalsVisibleTo("Filekin.App.Tests")`, which is how presentation logic
  is tested at all. Test through the view models and the public paths the app itself takes. Anything
  a test needs that is private today needs a deliberate internal seam, never a widened field.
- Claude status-line mode writes quota observations only and verifies the project folder. It never writes
  participant, lease, session, or turn state.
- The in-turn refresh is one-shot and rearms after a tick. Keep it shorter than `MaximumUsageAge`;
  overlapping periodic ticks are forbidden. Dispose by cancelling/draining the tick before taking the
  operation gate or it deadlocks.
- The opening prompt is deliberately minimal. Coordination rules live in MCP tool descriptions so the
  user does not repeatedly pay for duplicated instructions.

## Unbuilt/open agent decisions

- **Decided 2026-09-02, not built:** a handoff no longer disconnects the agent that gave up the turn.
  Filekin releases the working-tree lease on a proven end of turn rather than a proven end of session, so
  a CLI a person opened survives the handoff. Only a tab the person already opened is ever reattached;
  Filekin never opens one by itself, and closing the tab ends the reattaching. See *A CLI a Person Is
  Reading Survives a Handoff* in `DECISIONS.md` for the provider evidence and the accepted trade — an
  idle agent is trusted not to write instead of being unable to, and an attached CLI is an input surface.
  This waits behind the Control Room lifecycle QA; do not start it inside that checkpoint.
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
- Optional per-agent role lines. A **project template** (what a new project is scaffolded from, on disk),
  a **preset** (a reusable named settings bundle chosen at setup) and an **agent role** (what one
  participant is asked to be for a turn) are three separate things and must not be merged into one
  feature. A benchmark's per-function agent definition is a bench fixture, not automatically any of them.
- Previewed project bootstrap: existing projects default to no writes; empty folders may be offered
  `.filekin/PROJECT.md`, `AGENTS.md`, and `CLAUDE.md`, never a competing `HANDOFF.md`.
- **Deferred with the evidence recorded:** whether Codex moves from Filekin's private App Server to the
  shared daemon via `codex app-server proxy`. See *Codex's Shared Daemon Is the Root Fix for the Open
  CLI* in `DECISIONS.md`. It is the root fix for the open-Codex-CLI stall below, not a cleanup, and it is
  **not part of this checkpoint**. `codex-cli 0.152.1` provides `app-server daemon`, `app-server proxy`,
  `codex agents` (browse live sessions on the shared daemon) and `codex queue --thread --message` (a
  documented message into a live session, not injection); `codex resume` has no flag to join a live
  thread, which is why the current resume path cannot be repaired by wording. Costs: Codex sessions would
  outlive Filekin, so Codex orphan discovery would have to be built; another Codex client could reach the
  coordinated thread; `app-server` is `[experimental]`. First thing to test, and the gate that can refuse
  the whole move: whether per-project MCP overrides and the selected work mode survive per thread on a
  daemon already running under different config.
- **Open, evidence recorded 2026-09-02:** move enforcement from a reminder to a refusal using provider
  hooks. See *Both Providers Ship Hooks, So Enforcement Can Be Preventive* in `DECISIONS.md`. Codex
  0.152.1 has hook events including `preToolUse`, `postToolUse`, `stop`, `sessionStart/End` and
  `preCompact/postCompact`; handlers may be a `command`, an `mcpTool` call into Filekin's own server, a
  `prompt`, or an `agent`; a `sync` hook can return `blocked`/`stop`; scope is `thread` or `turn`. So
  `AskForTheMissingHandoffAsync` need not stay reactive, and the required-handoff rule need not live in a
  project file Filekin does not write. **Unproven gate:** whether Filekin can install hooks through
  launch flags (`HookSource: sessionFlags`) and so still write nothing into the project folder — read
  from the protocol schema, no hook has been run. Never pass `--dangerously-bypass-hook-trust`; it is the
  same class as `bypassPermissions`.
- **Open, shape recorded 2026-09-02:** benchmarks per coordination function in two tiers. See
  *Benchmarks Are Per Function, in Two Tiers, and Measure Rediscovery* in `DECISIONS.md`. Tier 1 is a
  scripted fake agent with no live model for every state machine (turn selection, lease, handoff routing,
  missing-handoff recovery, allowance thresholds, restart reconciliation); tier 2 is a live scored run
  for handoff quality, context packing, and memory only. The realistic bench is a fixed repo plus an
  acceptance script the agents cannot read, scored objectively. Build **rediscovery cost** first — turns
  the receiving agent spent finding what its handoff already told it.
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

- **Reported 2026-09-03, not reproduced: the CLI-tab block appears with no CLI tab open.** The owner
  saw the control room say a session is held by a CLI tab while no such tab was in the strip, and
  found that opening the CLI and closing it again clears it. Code reading does not explain it:
  `IsCliTabOpenHere` has exactly one writer, `ShowAgentProject`, which recomputes it from the live
  `TerminalTabs` on every refresh, and the three-second watch calls that whenever the project tab is
  selected. The likeliest explanation is that the tab is still there and no longer looks like one: a
  tab keeps its agent identity until the private command-completion signal reports the CLI returning
  to PowerShell, so a signal that never fires leaves an ordinary-looking prompt still counted as the
  CLI holding that session. That matches the reported cure exactly — opening the CLI again and
  closing it properly is what clears the identity. It is reachable after **End** on Claude, which
  stops the session without closing its terminal. To confirm: reach the state, then check whether an
  agent-marked tab is still in the strip showing a plain prompt. If it is, the fault is the
  completion signal, not the flag. Do not reword around this; the words are right when a tab is
  genuinely open.

- **An open Codex CLI stops the relay.** Proved by hand twice on 2026-09-02 in `D:\GitHub\agent-test`.
  Press **Resume CLI** on Codex while Claude holds the turn, touch nothing, and let the handoff arrive.
  The pause itself is honest now; what remains is a control room that does not say the way out:
  1. The resumed terminal registers as Codex's session, so the row reads **Running · Waiting**, but the
     agent never called `filekin_clock_in`, so the coordinator still has it `Offline`. A running process
     and a clocked-in participant are different things and the control room shows only the first.
  2. ~~The pause blames usage.~~ **Fixed 2026-09-02, reproduced first.** `IsSafeToActivate` answered two
     unrelated questions in one predicate — `ConnectionState == Ready && (WorkOnLowAllowance || usage)` —
     and every caller reported only the allowance half, so an agent that was simply absent was explained
     as a quota problem. `WhyNotSafeToActivate` now returns `NotReportedIn` or `LowAllowance` and each
     caller states the one that happened. Presence is checked first, so an absent agent is never
     described as short of allowance.
  3. ~~The start control offers **Continue**.~~ **Fixed 2026-09-02.** It offered Continue because a
     session was running, and pressing it failed with *at least one agent must clock in before Filekin
     selects the first turn*. `AgentParticipantViewModel.IsCliTabOpenButNotReportedIn` now names the
     state where a running process and a clocked-in participant disagree; the start control is
     disabled while the agent it would use is in it, never says Continue for it, and the status line
     and hint both say to close that tab.
  4. Closing the tab recovers completely — Codex starts, clocks in, and the written handoff is still
     delivered — and the control room now says so.

  The fact underneath: a resumed CLI is a separate `codex resume` process, human-driven, and Filekin
  cannot dispatch a turn into it. While that tab is open the relay genuinely cannot continue by itself.
  That is defensible; saying nothing true about it is not. Any fix states the real cause where the
  person is looking, and `Continue` must either mean something here or not be offered.

  The root fix is the deferred shared-daemon move above: on the shared daemon Filekin's turns and the
  person's view are the same thread, so there is nothing to refuse.

  **Left to check by hand:** the presentation fix is written and builds green, but there is no
  `Filekin.App` test project, so it has been reproduced by nobody since. During the lifecycle pass,
  resume Codex's CLI while Claude holds the turn and confirm the status line names the tab, the start
  control is visibly dim, and closing the tab restores **Continue** and delivers the handoff.
  `AgentTheStartWouldUse` guesses the provider a start would pick — chosen, else the handoff
  recipient, else the only one running — which is a presentation-side approximation of
  `AgentRunService.StartCoreAsync`; if that choice ever changes, this guess must follow it.
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
`FILEKIN_RUN_SHELL_DIALOG_TESTS=1`.

Every SQLite fixture must be `[DoNotParallelize]`, because the assembly parallelizes at method level and
cleanup calls the process-wide `SqliteConnection.ClearAllPools()`. A class missing it does not fail
honestly: `AgentRunServiceTests` was missing it, and what that looked like was a Codex terminal close
leaving the working-tree lease behind. The store call inside the stop watcher had faulted, the fault was
recorded rather than thrown, and the lease stayed. It passed alone, failed under load, and moved when
unrelated tests were added. Suspect this before believing a lease bug that will not reproduce.
