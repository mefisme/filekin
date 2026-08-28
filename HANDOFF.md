# HANDOFF.md — Filekin

## How to use this file

This is the live cross-agent state for Codex, Claude Code, and any other implementation agent: where
the project is, what to build next, what is blocked, and the traps that will bite you. It is meant to
be read in full at the start of every session, so it is kept short on purpose.

**What belongs here:** the current phase, the immediate next task, blocked work and the reason it is
blocked, standing contracts that must not be changed without an owner decision, known problems, and
how to validate.

**What does not belong here:** a per-session changelog, lists of changed files, or the test counts
from finished work. Git records all three, and none of it helps the next agent decide anything. When
a feature is done, replace its entry with the short conclusion a future agent actually needs and move
any long record to `HANDOFF-ARCHIVE.md`.

`HANDOFF-ARCHIVE.md` is frozen history: the full per-session implementation records up to
2026-08-28, the PowerShell runspace + ConPTY spike findings, the 2026-08-26 unimplemented-scope
audit, and the reference-source index. Read it only to find the reasoning behind something this file
states as a conclusion.

**Keep this file under about 500 lines.** If it grows past that, something in it belongs in the
archive, in `DECISIONS.md`, or in a specification.

## Where the project is

**Production implementation, one confirmed v1 surface at a time.** The spike is finished and the
solution is live at `https://github.com/mefisme/filekin`.

Shipped and real — no sidebar surface is a design sample any more:

- **Files** — listing, path bar, sorting, navigation, selection, free space, and the command bar over
  a persistent asynchronous PowerShell runspace.
- **Terminal tabs** — ConPTY-hosted PowerShell with VT rendering, selection and copy, scrollback,
  mouse reporting, theme-aware colour, and tab shortcuts.
- **Rich views** — `/recycle`, `/places`, `/drives`, `/settings`, `/info`, `/unzip`, `/zip`, `/tidy`,
  `/where`.
- **Commands** — `/copy`, `/move`, `/rename`, `/toss` (`/trash`, `/delete`), `/go`, `/run`, `/ext`,
  `/location`, plus command-bar completion and `@` references.
- **Settings** — a real surface backed by `%AppData%\Filekin\settings.json`, with saved Locations,
  archive behaviour, tidy behaviour, theme/accent, interactive-tool rules, and the Windows user-PATH
  editor.

Substantial confirmed v1 scope remains. See **Remaining v1 scope** below.

### Repository and CI

- Public repository `https://github.com/mefisme/filekin`, default branch `main`, GPL-3.0.
- `main` is protected by ruleset `main` (id `21453006`): pull request with one approval, required
  status check `Build, test, and format (Windows)`, no deletion, no non-fast-forward. **The repository
  admin role is a bypass actor**, so the solo owner can push straight to `main`; GitHub prints
  `Bypassed rule violations` when that happens, which is expected and not an error.
- `CODEOWNERS` is review routing only. `require_code_owner_review` is deliberately false.
- `.github/` holds SHA-pinned secretless Windows CI, a PR template, and weekly Dependabot for Actions
  and NuGet.

## Immediate Next Task

**Files Back/Forward navigation history** — FEATURES.md, *Per-Tab Files Navigation History*. It is the
smallest remaining confirmed v1 item, it needs no owner decision, and it is additive: no shipped
behaviour changes.

### Why this one and not the others

- **Durable `/history` + `/undo`** is blocked on two owner decisions that change the data model. Both
  are written up with options and a recommendation under **Blocked: durable `/history` + `/undo`**. Do not start it.
- **The compact context menu** (FEATURES.md, *Compact Context Menu*) lists Copy, Cut and Copy Path,
  and **file clipboard operations do not exist anywhere in the tree yet** — no `Clipboard` use outside
  `TerminalControl` and `/info`. That item is really "clipboard file operations + F2 rename + Delete
  key + the menu", and the menu itself is a surface the owner will want to see on screen. Bigger, and
  worth its own session.
- **`/find`** is a search subsystem, deliberately distinct from `/where` (ARCHITECTURE 5Q).

### What the specification actually confirms

FEATURES.md *Per-Tab Files Navigation History*; ARCHITECTURE.md lines 2120, 2603, 2864;
UX-DESIGN.md line 1154.

- Rich views are **never** history entries. Back/Esc dismisses a rich view, Forward never restores it.
- Up stays parent-directory navigation only. Up is not Back.
- The specification says *per Files tab*. **Files tabs do not exist yet** — `ShellViewModel` owns
  `TerminalTabs` and a single Files workspace. Build the history as one object owned by the Files
  workspace, shaped so it can later become one instance per tab. Do **not** add Files tabs as part of
  this task.

### Where the work goes

`ShellViewModel.NavigateToAsync` (`src/Filekin.App/ViewModels/ShellViewModel.cs:1473`) is the single
chokepoint every navigation already passes through: sidebar Locations, `/places`, `/drives`, `@name`,
`/go`, double-click, `cd` typed in the command bar, and startup. Record there and every route is
covered at once.

Put the stack itself in `Filekin.Core/Navigation/` as a small platform-neutral type, not inline in the
view model. **There is no App test project** — only `Filekin.Core.Tests` and
`Filekin.Infrastructure.Windows.Tests` — so logic that lives in the view model cannot be unit-tested,
and this logic has enough edge cases to deserve tests.

### Traps, all of them real in the current code

- **Back and Forward must not record themselves.** Use an internal overload or a private flag; do not
  widen the public signature.
- **A failed navigation must not push.** `NavigateToAsync` returns early on `IOException` /
  `UnauthorizedAccessException` (line 1484) leaving the location unchanged. Record only after the
  location actually changes.
- **Refresh must not push a duplicate.** `ShellViewModel.cs:1159` re-navigates to `_currentPath` after
  a Recycle Bin restore. The same path twice in a row is not a history entry.
- **Startup seeds, it does not push.** `ShellViewModel.cs:1441` navigates at launch. Back must be
  disabled on a fresh window, not enabled and pointing at nothing.
- **`cd` from the command bar is a real navigation** and belongs in the history, per the Filekin
  invariant that `Set-Location` moves the visible Files location.
- **Backspace is already Up** (`MainWindow.xaml.cs:400`). Leave it. Back/Forward are Alt+Left and
  Alt+Right, plus the mouse XButton1/XButton2 buttons.

### Ask the owner one thing before drawing anything

Whether visible Back/Forward buttons belong in the path bar. UX-DESIGN.md does not draw them, and the
owner reviews UI on screen and cuts controls that earn nothing. Keyboard and mouse buttons first; show
a screenshot before adding chrome.

### Done means

- Back and Forward work from every route listed above, and each trap above has a test.
- A rich view is never a history entry — prove it with at least `/places` and `/where`.
- Up and Backspace behave exactly as they do today.
- Release build clean, full desktop suite green, `dotnet format --verify-no-changes` and
  `git diff --check` pass, and the feature is driven live in the real window before it is called done.

## Remaining v1 scope

| Item | State | What it needs before it can start |
| --- | --- | --- |
| Files Back/Forward | not started | nothing — it is the **Immediate Next Task** above |
| File context menu (Open / Rename / Copy / Cut / Copy Path / Delete / Properties) | not started | file clipboard operations, which do not exist anywhere in the tree yet |
| `/history`, `/undo`, durable SQLite journal | **blocked** | the two owner decisions below |
| `/find` | unspecified | its own product discussion; it is deliberately distinct from `/where` (ARCHITECTURE 5Q) and was never given a confirmed section |
| Complex-operation preview, interactive collision handling (Replace / Keep Both / Retry), UAC elevation, locked-file handling | not started | confirmed under Safety and Recovery. Basic partial-success isolation is done; the interactive choices are not |
| Task tabs (intelligent task delegation) | not started | the Workspace Surface System names a third surface family; only rich views and terminal tabs exist |
| Terminal panes (split) | not started | tabs exist, panes do not |
| Virtual Files locations | not started | representing non-folder locations while keeping them distinct from real paths |
| Folder sizes | not started | the listing shows `—` for directories |
| Preferred external terminal | partial | `/ext` launches a terminal; there is no *preference* for which one |
| Contextual session names | partial | titles are `tool · folder`; the project-aware intent is only half met |
| Accessibility exposure | partial | the largest known quality gap — see **Known problems** |
| AI-assisted filesystem interpretation | not started | the interface is explicitly undecided. **Do not invent it.** |

Deliberately **not** v1 and correctly absent: `/recent`, `/disk`, `/interactive`.

**Two pieces of doc drift, both the owner's documents to change:** `FEATURES.md` still lists
`/delete` in Core File Operation Commands where `/toss` shipped, and its "`/interactive` — Not Version
One" paragraph still says v1 stores no user-defined interactive routing rules, which the owner
reversed on 2026-08-26 when Settings gained them. `DECISIONS.md` records both supersessions.

## Blocked: durable `/history` + `/undo` — two owner decisions

Both questions below are **blocking**: they change the data model, not just the presentation, so
answering them after the SQLite journal exists means a migration. Neither is hypothetical — each one
describes something the shipped archive code does today.

Note first what is **already settled**, because it removes most of the feared scope. ARCHITECTURE.md
Topic 4B: history persists across restarts, undoability does not. So durable storage only ever holds
*the record of what happened*. The promise that something can still be reversed stays in memory, and
`InMemoryOperationJournal` already implements that half correctly. `JournalEntry` also already stores
its payload as JSON precisely so the SQLite store is an additive swap rather than a rewrite.

### Question 1 — What should Undo do with a file the user edited afterwards?

**Today:** `ZipExtractionUndo.Undo` walks `outcome.CreatedFiles` and calls `File.Delete(file)` for
every one that still exists. It does not look at the file's modified time, size, or content. So:

```text
/unzip report.zip        →  creates report\notes.md
(user opens notes.md, works in it for an hour, saves)
/undo                    →  notes.md is deleted, and the hour is gone
```

Undo goes to the Recycle Bin for *replaced originals*, but a **created** file is deleted outright.
The user's edit is not recoverable.

**The question:** when Undo is about to remove a file that Filekin created, and that file has changed
since Filekin wrote it, what happens?

Options:

1. **Delete it anyway.** Simplest, and Undo always fully reverses. It can destroy real work.
2. **Recycle instead of delete.** One-line change; the edit becomes recoverable from the Recycle Bin.
   Undo stays complete, and the user is not asked anything. It quietly fills the bin.
3. **Skip changed files and report them.** Undo becomes partial: `"Removed 40 files · 1 kept
   (edited)"`. Nothing is lost, but the folder is left half-reversed, and ARCHITECTURE.md line 1242
   already requires a partial undo to be recorded accurately rather than shown as a full reversal.
4. **Ask.** Matches the existing conflict-view model, but puts a prompt in the middle of an operation
   the user expected to be instant.

**Data-model impact — this is why it blocks.** Options 3 and 4 need to know whether a file changed,
which means the journal must persist something to compare against — size plus last-write time at
minimum, per created file. Options 1 and 2 need nothing extra. Deciding this after the schema exists
means changing the schema.

**Recommendation: 2, with 3's reporting for the files it applies to.** Recycling a changed created
file costs one call, never loses work, keeps Undo complete, and needs no new stored state. Filekin
already treats the Recycle Bin as its recoverability net everywhere else; this is the same promise.

### Question 2 — Is one typed command one undo step?

**Today:** `/unzip a.zip b.zip c.zip` runs one loop and calls `RecordOperation("unzip", …)` **once
per archive** (`ShellViewModel.Archive.cs:518`). Three journal entries. `/undo` reverses "the most
recent" one. So one typed command needs three `/undo` presses, and the first press silently reverses
only the last third of what the user asked for.

`/history` would show it the same way:

```text
12:31  Extracted c.zip     [Undo]
12:31  Extracted b.zip     [Undo]
12:31  Extracted a.zip     [Undo]
```

That is three lines for one user action, and it makes `/undo` behave differently from what the user
typed.

**The question:** does the journal record the *invocation* (one entry per command line) or the *unit
of work* (one entry per archive)?

Options:

1. **One entry per invocation.** `/undo` matches what the user did — one press undoes the whole
   `/unzip`. `/history` reads as a list of user actions. Partial failures must be described inside
   the one entry ("2 of 3 archives"), and the undo handler must reverse a list of outcomes rather
   than one.
2. **Keep one entry per archive.** No code change. `/history` becomes a machine log, and `/undo`
   surprises people.
3. **Parent entry with children.** Both readings available. It is the only option that adds a real
   schema relationship, and nothing in the confirmed v1 scope asks for it.

**Recommendation: 1.** ARCHITECTURE.md Topic 4A frames `/undo` as reversing an *operation* the user
performed, and the `/history` mock-up in the same topic lists user actions
(`Moved 8 files → @projects`), not per-item rows. Option 1 also matches how `/move @selection`
already reports: one result line for the batch.

**Consequence to accept:** `AppCommandResult` and the archive path both record per-item today, so
choosing 1 means the recording site moves out of the per-archive loop and the payload becomes a list.
That is a contained change now and a migration later.

### What is not blocked

The rest can be built once these two answers exist: the SQLite `state.db` store behind the existing
`IOperationJournal` interface, the `Files · History` rich view, and wiring `/copy`, `/move`,
`/rename`, and `/toss` into the journal. `/tidy` is recorded but not undoable (Topic 5W), so it needs
no answer to Question 1.

## Standing contracts — do not change these without an owner decision

**Keyboard, from a focused terminal.** Filekin claims exactly four combinations; everything else
belongs to the hosted shell, including plain `Tab`, `Shift+Tab`, `Ctrl+C` with no selection, `Escape`,
and `Y`/`N`. Every key taken is a key some hosted tool loses.

```text
Ctrl+Tab / Ctrl+Shift+Tab   next / previous workspace
Ctrl+Shift+T                new terminal tab at the current Files folder
Ctrl+Shift+W                close the selected terminal tab (confirms while live)
```

`Ctrl+Shift+C` / `Ctrl+Shift+V` are terminal-local only because the shell cannot distinguish them.
`Ctrl+C` copies only when a selection exists. `Alt+F4` and `Alt+Space` belong to Windows.

**`/run` is the only launch command; there is no `/open`.** Relative targets resolve from the visible
Files folder first, then `PATH`/`PATHEXT`. Do not crawl the machine for applications. GUI
executables, shortcuts, and associated documents launch independently through Windows shell
execution; console executables and terminal-oriented scripts start in a hosted terminal. A folder
passed to `/run` is refused with a clear message — do not silently open Explorer. `/ext` stays
distinct. An unknown raw shell command still begins in the finite runspace; only a concrete Windows
console executable still running after a short grace period gets the Y/N terminal offer, as a fresh
relaunch and never a live promotion.

**`/info` is a field sheet, not a listing.** Type-specific metadata comes from the Windows Property
System, never per-format parsers. An executable's embedded name is **Company**, never "Publisher" —
Filekin does not verify signatures in v1. Encoding shows immediately; **Lines** stays behind a `Count`
action beside `SHA-256`. The recursive scan reports on a 250 ms timer, never follows reparse points,
and reports unreadable folders instead of hiding them. Shortcuts are revealed, not edited, and never
through `IShellLink::Resolve`.

**Do not put `Environment.SetEnvironmentVariable` back.** `WindowsUserEnvironmentWriter` writes
`HKCU\Environment` and sends `WM_SETTINGCHANGE` itself for two measured reasons. The framework method
rewrites the value as `REG_SZ` whatever it was, destroying a `REG_EXPAND_SZ` PATH — the text survives,
so string-comparing tests still pass, while every `%USERPROFILE%`-style entry silently stops
expanding. And it broadcasts without `SMTO_ABORTIFHUNG`, so each non-pumping top-level window costs a
full second: measured here with 13 such windows, the whole call took 17–20 s against 431 ms for
Filekin's own path. Both facts about the broadcast are true at once — it is sent, and a running
terminal still will not see the change, because `cmd.exe` and PowerShell keep the block they started
with. Explorer listens, so what it launches afterwards inherits the new value.

**The `/where` matcher's three bounding rules are load-bearing.** Codex's original alias learning
returned 2862 locations in 20.7 s for `/where "Visual Studio Code"` because it learned the word `user`
from the registration name *Microsoft Visual Studio Code (User)*, which then matched *NVIDIA User
Container*, which taught `nvidia` and `framework`, which swept Program Files. Fixture tests did not
catch it. The rules: only a `Query`-strength match may teach; names are learned from **paths**, never
display names, and a shortcut target teaches only when it is itself an executable; a short learned
word must be an entire name, and only a joined name of six or more characters may match inside
another. Publisher, architecture, and folder-role words are never learned, and the alias set is
capped.

**Command-bar `@` beats PowerShell splatting.** A token matching a known reference (`@thisfolder`,
`@selection`, a user Location) always resolves as that reference. Only unknown tokens pass through.
A user who needs splatting uses an independent terminal tab, which gets no `/` or `@` preprocessing.

**Location management is settled**: the sidebar plus `/location add|set|rename|remove`. Do not reopen
that grammar.

**Terminal layering.** Raw bytes in `ITerminalSession`, deterministic VT state in the platform-neutral
`TerminalEmulator`, drawing and input in `TerminalControl`, session/dispatcher state in
`TerminalTabViewModel`, collection and selection in `ShellViewModel`, window focus and confirmation in
`MainWindow`. Every parser fix gets a focused Core test. **`Filekin.Core` must not reference WPF.**

**ConPTY constraints, from the spike.** The communication channels must be serviced independently or
a full buffer deadlocks. Output must be drained through teardown, because `ClosePseudoConsole` can
emit a final frame and terminates attached console clients — graceful shutdown first, pseudoconsole
closure as the last resort. A plain text control is not a terminal; VT/ANSI must be interpreted.

## Known problems

**Accessibility — the largest quality gap.** The Files list and sidebar expose raw view-model
`ToString()` output as automation names (`Filekin.App.ViewModels.FileRowViewModel`). The terminal cell
grid is not exposed as text at all; `TerminalControl` has only a basic `Document` peer. The first is
cheap and worth doing regardless of the second.

**There is no test project for `Filekin.App`.** So `SelectAdjacentWorkspace`, the Places/Drives/Settings
row view models, and the theme code are covered by live QA only, never by tests. Put new logic in
`Filekin.Core` where it can be tested. If an App test project ever appears, those are the first
candidates.

**Two real-Recycle-Bin tests do not run on CI, by design.** `WindowsRecycleBin` reads the bin through
`Shell.Application`, and on a hosted runner a recycled file never reaches the bin, so the round trip
cannot be verified there. They carry `[TestCategory("RequiresInteractiveShell")]` and CI filters them
out. **Real coverage comes only from desktop runs.** Do not weaken these assertions to make CI green,
and do not try to infer the capability at runtime — that was tried and was wrong.

**Archive Undo is session-scoped and not durable**, one journal entry per archive rather than per
typed invocation, and it does not detect edits made to an output after extraction. All three are
exactly what the blocked questions above must settle.

**Terminal.** Selection is drag-only — no double-click word select, triple-click line select, or
shift-click extend; `Ctrl+A` is left to the shell for PSReadLine. Focus reporting (`?1004`),
synchronized output (`?2026`), and the kitty keyboard protocol (`ESC[>1u`) are requested by Claude
Code and deliberately ignored; the fallbacks are correct. Leaving a full-screen TUI does not restore
the previous screen — that is conhost, reproduced from a raw capture, and nothing in Filekin can
restore content conhost never re-sends. OSC window-title and hyperlink commands are ignored because
Filekin tab titles describe launch context. The root command line appends startup `CommandText`
verbatim, so a command containing embedded double quotes is out of v1 scope.

**A hosted terminal inherits Filekin's environment**, which is correct but means `NO_COLOR`, `TERM`
and friends flow into the shell and its children. This has already produced one false "colours are
broken" reading.

**`/drives` sees volumes only** — anything with a drive letter. A phone over MTP is not a volume,
never appears in `DriveInfo.GetDrives()`, and cannot appear at all; that is a scope limit, not a
refresh bug. A network mapping that reconnects on its own may not broadcast an arrival, though window
re-activation still catches it.

**Files.** `Esc` stops a running command only while the command bar holds focus — that is where the
caret sits after Enter, so the normal flow works, but clicking into the list mid-command leaves `Esc`
inert. Widening it is a keyboard-contract change. Selection is not preserved across a re-sort.
`FileLauncher.Open` swallows launch failures silently. The tab strip clips the last tab at the
default window width with three tabs open, which needs a product decision on overflow behaviour.
About is still a label with nothing behind it.

**Command classification tokenizes on whitespace and is not quote-aware**, so an executable path
containing spaces is not one token for the interactive-vs-finite decision. The raw input is still what
executes. `InteractiveCommandRegistry` ships a deliberately minimal built-in set plus user rules from
Settings; broadening the built-ins stays deferred.

**ConPTY resize is environment-dependent.** On a hosted CI runner the child's `RawUI` never observes
`ResizePseudoConsole` even though the call succeeds; on a real desktop it does. The test asserts only
that the resize is accepted and the session survives. Do not re-add a `RawUI` width-polling
assertion.

**Restore/delete verb matching is English-only** (the shell "Restore" verb).

## How to validate

```text
dotnet build -c Release                     0 warnings, 0 errors
dotnet test  -c Release                     run the FULL suite on the desktop, unfiltered
dotnet format --verify-no-changes
git diff --check
```

CI runs `--filter "TestCategory!=RequiresInteractiveShell"`, so a green CI is not the whole suite.
**Run the unfiltered suite locally before calling anything done.** As of 2026-08-28 that is 452 tests
(273 Core, 179 Windows).

Then drive the real window. Every one of the last several sessions found a defect that way that no
test caught — the `/where` alias cascade, the drive-probe starvation, the tidy header count, and the
`REG_EXPAND_SZ` destruction. Read the QA notes below first.

## Live QA notes for the WPF app

**Driving the app.** Start the Release build, foreground the window, send input with
`System.Windows.Forms.SendKeys` plus `mouse_event`, capture with `PrintWindow` (flag 2). UI Automation
(`System.Windows.Automation`) finds and invokes named controls — which is why accurate
`AutomationProperties.Name` values are worth keeping. Call `SetProcessDPIAware()` in the driving
process first or `GetWindowRect` returns virtualised coordinates and the capture is cropped.

**Never send input without confirming the foreground window.** `SetForegroundWindow` can be refused,
and the keystrokes then land wherever the foreground actually is — during this project that sent
`Ctrl+Shift+T` and a pasted command into a second Filekin instance the owner had open. Check
`GetForegroundWindow() == targetHwnd` **after** trying to focus, and check for more than one running
instance before starting.

**When input cannot reach the app at all, render offscreen rather than skip verification.** A
throwaway WPF console project in the scratchpad with a `ProjectReference` to `src/Filekin.App`:
`new Filekin.App.App()` + `InitializeComponent()` loads the merged dictionaries, `new MainWindow()` +
`Show()` gives a real window with real styles, `(ShellViewModel)window.DataContext` drives the real
view model, `Dispatcher.PushFrame` with a `DispatcherTimer` pumps instead of `Application.Run`, and
`RenderTargetBitmap` captures without the foreground. Pump after `Show()` or `ActualWidth` is still
zero. Delete it afterwards; it is not product code.

**Back up `%AppData%\Filekin\settings.json` before QA that changes preferences**, and restore it
after. The harness writes the user's real settings file.

**Back up the user PATH before any PATH QA**, restore it byte for byte, and verify no stray entry is
left. If you open Windows' own Environment Variables dialog to check, **close it with Cancel** — OK
rewrites the whole value and flattens its value kind, which is the exact defect
`WindowsUserEnvironmentWriter` exists to prevent.

**A running app locks the build output.** `Filekin.exe` holds `Filekin.Core.dll`, so a build fails
with MSB3027 while it is open. Close it first, and confirm which instance is yours before killing
anything.

**Probing what the shell receives.** `[Console]::ReadKey` is fine for keyboard checks but silently
drops mouse input, so it cannot test mouse reporting. Use a raw-stdin reader — a small node script
with `process.stdin.setRawMode(true)` appending to a file — because reading a file back is
unambiguous and reading a screenshot is not.

**ConPTY forwards a mouse-mode request only after the client enables raw/VT input.** A probe that
wrote `ESC[?1000h` before `setRawMode(true)` had it swallowed by conhost and looked exactly like a
Filekin bug. **Capture the raw ConPTY stream before changing product code** — a throwaway MSTest that
starts a session, subscribes to `OutputReceived`, and dumps bytes with `ESC` made visible settled
three separate "is this us or conhost" questions in this project. Delete it before committing.

**A mapped codepoint is not a correct glyph.** `CharacterToGlyphMap.ContainsKey(0xE8B7)` returns true,
so a coverage check confirms nothing — that codepoint is a page, not a folder. Render candidates with
`FormattedText` in Segoe MDL2 Assets and look at them. `ED25` folder, `E8B7` page, `E753` cloud,
`EDA2` drive.

**Colour looks broken when the environment says so.** `NO_COLOR` in Filekin's environment is inherited
by the hosted shell: PowerShell flips `$PSStyle.OutputRendering` to `PlainText` and node tools disable
colour. Launch from a clean environment before concluding anything about colour.

The harnesses used so far were throwaway scratchpad PowerShell and are **not** in the repository. A
maintained one would be reasonable, but it is developer tooling the owner has not asked for.

## Other open product questions

Record genuinely unspecified user-visible decisions here instead of silently choosing them. Resolved
ones move to `DECISIONS.md`; the resolved list up to 2026-08-28 is in `HANDOFF-ARCHIVE.md`.

- **Does the command-bar runspace load the user's PowerShell profile?** Terminal tabs do, by decision.
  The command bar does not — it uses `InitialSessionState.CreateDefault2()`, which never runs
  `$PROFILE`. Decide whether it should reflect the user's aliases and functions or stay a clean,
  predictable session. Not loading it also reduces the chance of a profile-defined command colliding
  with `/` and `@` handling.
- **Assistive-text exposure for the terminal in v1?** Unimplemented and unspecified.
- **Copying a file path from the Files list.** The owner noted that text selection is nowhere in the
  app. The Files list is deliberately a *filesystem* selection, so copying a path to the clipboard is
  a distinct command or shortcut that no specification defines. It overlaps the context-menu work.
- **Terminal tab overflow.** Three tabs at the default width clip the last one. Shrink, scroll, or
  overflow menu is a product decision.
- **Agent relay / MCP server — Proposed, not v1.** Recorded in `FEATURES.md` as Agent Relay Mailbox,
  Agent Turn Indicator, Agent Budget Watch, and Filekin MCP Server. Nothing is implemented and
  nothing is committed to v1.
- **Hosted terminal PowerShell profile becomes a user setting** (load vs skip, load remaining the
  default) when it is worth adding. `TerminalSessionRequest.LoadProfile` already exists; tests pin it
  to `false` for determinism.
