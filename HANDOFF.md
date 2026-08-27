# HANDOFF.md — Filekin

## Purpose

Shared handoff state for Codex, Claude Code, and other implementation agents.

Keep this document current enough that another agent can continue the project without relying on chat history.

## Current Phase

**Hosted terminal tabs, user-defined Locations, the `/places` and `/drives` rich views, the Settings surface, command-bar completion, `/go`, `/run` with its unknown-console-command terminal fallback, `/info`, `/unzip`, and `/zip` are complete.** `/go <folder>` moves Files through the normal navigation pipeline and consumes its complete line remainder as one target, so Windows paths containing spaces need no quotes. Ordered Locations load from `%AppData%\Filekin\settings.json`, navigate Files, resolve as command-bar `@name` references, and can be added, path-updated, renamed, or removed through both the sidebar and `/location`. `/places` lists the six common folders followed by Windows-registered cloud sync roots; `/drives` lists assigned drives with capacity and a usage bar. Settings (`/settings`, or the sidebar footer entry) is a real rich view with five categories — Appearance, Startup, Terminal, Archives, and Advanced. `/unzip` and `/zip` share a preflight preview, ZIP-only planning and safe path handling, per-operation collision controls, progress/cancellation, and session-scoped archive Undo backed by `IOperationJournal`; the Archives settings control the preview and default collision policy. Running archive work is owned by Files rather than its temporary rich view: Back/Esc or another rich view detaches presentation while a command-bar task row keeps progress, View, and Stop available. Archive completion, cancellation, and result-line Undo now explicitly refresh the visible Files hierarchy. Hosted terminals and `/run` also merge the current Windows Machine/User PATH into Filekin's inherited process PATH, so CLIs installed after Filekin started (including Codex CLI) resolve without restarting Filekin. The archive feature is complete, documented, tested, and live-verified in the current local follow-up commit. Substantial confirmed v1 scope is still unimplemented — `/where`, `/tidy`, `/history`, `/undo`, durable operation history, Files Back/Forward, and the file context menu.

The public repository is live at `https://github.com/mefisme/filekin`, with `main` protected by an active repository ruleset. The production solution contains platform-neutral shell/location/terminal contracts, an asynchronous persistent PowerShell runspace adapter, a ConPTY-backed terminal-host service, the command classifier/router, the real Files/Recycle Bin workspace, the hosted terminal surface, settings-backed sidebar Locations, the Places/Drives navigation surfaces, and the Settings surface. No sidebar surface is a design sample any more.

## Current Product Identity

- Name: **Filekin**
- Category: keyboard-first Windows file manager + terminal
- License direction: GNU GPLv3
- Distribution: traditional installer + portable ZIP
- Runtime deployment: self-contained .NET

## GitHub Repository

- Public repository: `https://github.com/mefisme/filekin`
- Default branch: `main`
- Initial commit: `caba0d8` (`chore: establish Filekin production foundation`)
- GitHub recognizes the license as GPL-3.0.
- `.github/` contains SHA-pinned secretless Windows CI, `CODEOWNERS`, a PR template, and weekly Dependabot configuration for GitHub Actions and NuGet.
- Initial CI run `32869871853`: passed restore, Release build, all tests, and formatting verification.
- **Active branch protection**: repository ruleset `main` (id `21453006`), enforcement `active`, targeting the default branch. Rules: pull-request required with `required_approving_review_count = 1`, `require_code_owner_review = false`, `require_extra_approval_for_unattributed_changes = false`; required status check `Build, test, and format (Windows)` bound to the GitHub Actions app (`integration_id 15368`); block deletion and non-fast-forward. Bypass actor: repository admin role (`actor_id 5`, `bypass_mode always`) as the owner emergency bypass so the solo owner is not locked out.
- **CODEOWNERS is review routing only**, not a mandatory gate: `require_code_owner_review` is deliberately false so code-owner paths route review requests without adding a second required approval beyond the one requested review.

## Immediate Next Task

**Pick the next command with the owner.** The saved-Location rebase pass and the `/toss` / `/trash` /
`/delete` aliases are both complete and committed.

`/where` and `/tidy` are the remaining independent confirmed commands and need no new product
decision to begin. Durable `/history` + `/undo` is the larger slice, but **do not start it before the
owner settles its safety contract**. Both open questions are written up in full, with evidence from
the shipped code, options, and a recommendation, under **Open Product Questions** immediately below.
Files Back/Forward and the file context menu also remain unimplemented.

The next large slice remains durable `/history` + `/undo`, but its safety contract must be settled
with the owner before code: especially how to handle outputs edited after an operation and how a
multi-archive invocation appears as one user action. The remaining independent confirmed commands
are `/where` and `/tidy`. Files Back/Forward and the file context menu also remain unimplemented.

## Open Product Questions — durable `/history` + `/undo`

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
/unzip report.zip        →  creates report
otes.md
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

### Completed: `/toss`, `/trash`, and `/delete` are one command — 2026-08-27

The owner confirmed that all three names must perform the same recoverable Recycle Bin operation.
`/toss` remains primary; `/trash` and `/delete` are registered aliases of the same handler.

Mechanism: `IAppCommand` gained an `Aliases` list defaulting to empty (a default interface member, so
commands without aliases needed no change; `FileOperationCommand` re-declares it as `virtual` for its
subclasses). `AppCommandDispatcher` registers each command under its name and every alias and throws
on any collision between them, so an alias can never silently shadow another command. Handlers that
echo their own name now use `context.Command.Name` — the name the user typed — so `/delete` with no
argument answers `Usage: /delete <target> …`, not `/toss`.

All three appear in command completion; the two alias rows carry `(same as /toss)` so `/toss` stays
the name the docs teach. `/recycle` still opens the Recycle Bin view, so no alias is ambiguous.

This supersedes the part of the 2026-08-26 `/toss` decision that excluded `/trash` and `/delete`;
that entry is now marked partly superseded in DECISIONS, and PRODUCT, FEATURES, UX-DESIGN, and
ARCHITECTURE carry the alias rule.

**Verified state:** Release build 0 warnings / 0 errors. Full unfiltered Release desktop suite passes
**354/354** (202 Core, 152 Windows). `dotnet format --verify-no-changes --no-restore` and
`git diff --check` pass.

**Live WPF QA** (driven through UI Automation against the Release build, in a throwaway
`%TEMP%\filekin-alias-qa` folder — no user file or saved Location was touched): `/go` into the
sandbox showed `3 items`; `/toss by-toss.txt`, `/trash by-trash.txt`, and `/delete by-delete.txt`
each answered `Moved <name> to the Recycle Bin` and the listing shrank 3 → 2 → 1 → 0. Bare `/delete`
answered `Usage: /delete <target> [<target> …]`. Completion: `/t` + Tab opened the list showing
`/toss` and `/trash` with their descriptions; `/de` + Tab uniquely completed to `/delete`; `/tr` + Tab
uniquely completed to `/trash`. Afterwards the three recycled items were restored from the Recycle
Bin and the sandbox folder was removed, leaving no QA residue.

Note for future UI QA: the completion popup is opened by **Tab**, not by typing, and a WPF `Popup` is
its own top-level window — a UIA descendant search from the main window will not find
`CommandSuggestionList`. Search every top-level window of the process instead.

Files in this change: `src/Filekin.Core/Commands/App/IAppCommand.cs`, `AppCommandDispatcher.cs`,
`FileOperations/{FileOperationCommand,TossCommand}.cs`; App
`ViewModels/ShellViewModel.Completion.cs`; tests `AppCommandDispatcherTests.cs` and
`FileOperationCommandsTests.cs`; plus PRODUCT, FEATURES, UX-DESIGN, ARCHITECTURE, DECISIONS, and this
handoff.

### Refreshed: stale startup documents — 2026-08-27

`CLAUDE.md` and `AGENTS.md` still told an incoming agent that the throwaway spike was the first
engineering task and that production work must not begin — nine committed features out of date.
`README.md` still said the application "does not yet expose a production UI". All three now state the
production-implementation phase and point at `HANDOFF.md` as authoritative for current scope.
`PROJECT-SETUP.md` gained a **Historical** status header; its content is unchanged, because it is the
record of why the architecture was validated the way it was.

Both agent entry points now name `ENGINEERING-GUARDRAILS.md` explicitly instead of folding it into
"the master specifications". `CLAUDE.md`'s startup list reads it second. `AGENTS.md` — the file Codex
reads too — now gives one read order for **any** work, not only implementation changes: this file,
then `ENGINEERING-GUARDRAILS.md`, then `HANDOFF.md`, then the relevant specifications; it no longer
sends an agent to `PROJECT-SETUP.md` first. Its Source of Truth section also states that the
guardrails differ in kind from the other five: they govern *how* code is written and apply to every
change, so they are not a document to consult only when a product question comes up.

### Completed: Saved Locations follow `/move` and `/rename` — 2026-08-27

The owner confirmed that saved Locations must automatically rebase so Filekin's own move/rename
operations never knowingly break them. Shipped behavior:

- successful `/move` and `/rename` results carry structured source/destination `PathRelocation`s;
- `SettingsBackedLocationCatalog` rebases exact and nested saved paths, using the longest matching
  source, and persists all affected Locations in one settings write;
- names, order, nested relative suffixes, unknown fields, and Location-based startup behavior remain
  intact; `/copy` emits no relocation and never retargets a Location;
- `LocationRebaseCoordinator` compensates for a failed settings write by moving filesystem items back
  in reverse order, so a move nested inside an earlier destination is undone first;
- a compensation that itself fails reports how far it got (`"N of M items were returned, the rest
  remain moved"`) instead of a bare "rollback failed", because a partial rollback leaves the two
  halves of the batch in different states and the user must be told which;
- generic app-command dispatch now runs off WPF's UI thread, covering the previously synchronous
  `/copy`, `/move`, `/rename`, and `/toss` filesystem work;
- the result line appends `· Updated N saved Location(s).` only when a Location actually moved.

PRODUCT, FEATURES, UX-DESIGN, ARCHITECTURE, and DECISIONS carry the rebase behavior.

**Verified state:** Release build 0 warnings / 0 errors. Full unfiltered Release desktop suite passes
**350/350** (198 Core, 152 Windows), including ConPTY and both real Recycle Bin tests. Before the
review additions the same suite passed 345/345, exactly the total this handoff predicted.
`dotnet format Filekin.sln --verify-no-changes --no-restore` and `git diff --check` pass. All edited
Markdown is CRLF.

**Live-WPF `/move` and `/rename` were deliberately not exercised**, because the only realistic live
path mutates the owner's real `%AppData%\Filekin\settings.json` Locations and their real folders.
The durable substitute is `tests/Filekin.Infrastructure.Windows.Tests/Settings/LocationRebaseIntegrationTests.cs`,
which dispatches real `/move`, `/rename`, and `/copy` through `WindowsFileSystemOperations` over a
temporary tree wired to a real `SettingsBackedLocationCatalog` and a real settings file, then reloads
the catalog from disk to prove persistence. What remains unverified by that test is only the WPF
presentation layer above `CommandExecutor` — the appended result-line text and the listing refresh.

Files in this change: `src/Filekin.Core/Operations/PathRelocation.cs` (new),
`src/Filekin.Core/Commands/References/IUserLocationPathRebaser.cs` (new),
`src/Filekin.Core/Commands/App/LocationRebaseCoordinator.cs` (new), `AppCommandResult.cs`,
`FileOperations/{TransferCommand,MoveCommand,RenameCommand}.cs`; App
`ViewModels/{CommandExecutor,ShellViewModel}.cs`; Windows settings
`SettingsBackedLocationCatalog.cs`; tests `FileOperationCommandsTests.cs`,
`LocationRebaseCoordinatorTests.cs` (new), `SettingsBackedLocationCatalogTests.cs`, and
`LocationRebaseIntegrationTests.cs` (new); plus PRODUCT, FEATURES, UX-DESIGN, ARCHITECTURE,
DECISIONS, and this handoff.

### Completed: `/go` — 2026-08-27

`/go <folder>` is an app-owned Files navigation command. Its parser deliberately treats everything
after `/go` as one target, so `/go C:\Program Files` and `/go Common Files` need no quotes. Matching
outer quotes remain accepted. Relative paths resolve from visible Files; intrinsic, known-folder, and
saved-Location `@` references work when they resolve to exactly one folder. File, missing, empty, and
multi-item targets fail inline without moving Files. Directory validation runs off the UI thread and
successful navigation uses the existing `NavigateToAsync` pipeline, keeping breadcrumbs, listing,
free-space state, active Location, and later runspace synchronization together.

The command is in command-bar completion and is reconciled across PRODUCT, FEATURES, UX-DESIGN,
ARCHITECTURE, and DECISIONS. Ten focused parser cases pass. Live WPF QA navigated with the exact
unquoted absolute command `/go C:\Program Files`, then the relative `/go Common Files`; Files showed
`C:\Program Files\Common Files`, its breadcrumb, eight items, and no stale result line. No fixture or
user filesystem mutation was needed.

### Completed: `/unzip` and `/zip` — 2026-08-27

`/info` shipped as **`57dd1fc`** (`feat(app): add /info inspection`), CI run `33097697704` green.

`/unzip` and `/zip` are complete across Core, Windows infrastructure, App UI, Settings, tests, and
the six master specifications. The result line exposes archive Undo, and Settings has an Archives
category for preview and collision defaults.

**Follow-up lifecycle correction, 2026-08-27:** a running extraction/compression no longer belongs to
the archive rich view. Back/Esc and opening Settings/Info/another Files rich view detach it without
cancellation. A persistent command-bar task row shows the operation title and current entry with
accessible **View** and **Stop** actions; View reopens the same live surface. Skip-preview extraction
also returns command-bar control immediately. A second archive request is refused while one runs,
and Undo is associated with the archive result only rather than leaking beside later command output.

**Follow-up refresh and PATH correction, 2026-08-27:** archive execution and archive Undo bypass the
ordinary `ExecuteCommandAsync` outcome branch that consumes `RefreshListing`, so successful work was
presented without refreshing the Files hierarchy. Both archive paths now refresh the current folder
in `finally`; if Undo removes the folder currently being viewed, Files navigates to the nearest
surviving ancestor. Separately, command classification was correctly routing `codex` to a hosted
terminal, but Filekin and its ConPTY children retained the process-time PATH snapshot. New hosted
terminal sessions and `/run` resolution now preserve process-specific PATH entries while merging the
current Windows Machine and User PATH, allowing newly installed commands to resolve without an app
restart.

**Verified state:** Debug and Release builds pass with 0 warnings / 0 errors. The final CI-filtered
Release suite passes **326/326** (183 Core, 143 Windows); the full desktop Release suite passes
**331/331** (183 Core, 148 Windows, including the five interactive-shell tests). `dotnet format
--verify-no-changes --no-restore` and `git diff --check` pass. Live WPF QA exercised `/zip` preview,
root-folder replanning, creation, result-line Undo visibility/accessibility, and the Archives settings
panel. A final live pass observed the Files count and rows update immediately after `/zip` and
`/unzip` (1→2→3 items), and both raw `codex --version` and `/run codex --version` opened hosted tabs
and printed `codex-cli 0.150.0`. The generated QA trees were removed afterward.

#### Implementation record and decisions to preserve

Do the `CLAUDE.md` startup ritual — `AGENTS.md`, `PROJECT-SETUP.md`, this file — and read
`ENGINEERING-GUARDRAILS.md` before writing code. The guardrails are what keep this codebase from
drifting into generated-looking work, and the owner asks for them by name.

The original product rule began in `PRODUCT.md`; the completed behavior is now reconciled across
`PRODUCT.md`, `FEATURES.md`, `UX-DESIGN.md`, `ARCHITECTURE.md`, and `DECISIONS.md`.

| Document | What it holds for these commands |
| --- | --- |
| `PRODUCT.md:216`, `PRODUCT.md:257` | **The actual rule.** "Archive extraction can create unnecessary nested folders", and "avoid redundant directory nesting when an archive already contains a wrapper directory". |
| `FEATURES.md:29`, `FEATURES.md:554` | One-line confirmations: "without unnecessary outer-directory duplication", "redundant-root handling and safe extraction preview where needed". |
| `ARCHITECTURE.md:360` | `/unzip` may return an extraction preview — the preview surface is anticipated, not invented. |
| `ARCHITECTURE.md:445` | Archive path traversal is a named security concern. |
| `ARCHITECTURE.md:1322` | The old non-undoable decision is explicitly superseded by the 2026-08-27 session Undo behavior. |
| `ARCHITECTURE.md:1491` | The exact error wording for a missing archive, which `UnzipInvocationParser` uses verbatim. |
| `ARCHITECTURE.md:1521` | The result-line shape with `[Undo]`, now implemented for archive operations. |
| `ARCHITECTURE.md:3091` | Partial-success batch rule, naming `/unzip`. |
| `UX-DESIGN.md:319`, `:884` | Completion description; "References do not guess; commands validate", which is why bare `/unzip` does not hunt for an archive in the folder. |
| `DECISIONS.md` | Records the 2026-08-27 archive grammar, preview, folder, format, collision, and Undo decisions. |

`/zip` was new scope added by the owner during this work. It is now specified in `PRODUCT.md`,
`FEATURES.md`, `UX-DESIGN.md`, `ARCHITECTURE.md`, and `DECISIONS.md`.

**Validation commands used for the numbers above:**

```
dotnet build -c Debug --nologo
dotnet test -c Debug --no-build --filter "TestCategory!=RequiresInteractiveShell"
dotnet format --no-restore            # fixes LF endings first
dotnet format --verify-no-changes --no-restore
git diff --check
```

The Release build and unfiltered desktop suite were run before committing; see the verification
record above and **Tests / Validation** below.

#### What is DONE and tested

| Layer | Files | State |
| --- | --- | --- |
| Core model | `src/Filekin.Core/Archives/` — `ArchiveEntry`, `ArchiveFormats`, `IArchiveReader`, `UnzipLayout`, `CollisionPolicy`, `ArchivePlan`, `ArchivePlanner`, `ExtractionOutcome`, `IArchiveExtractor`, `ZipPlan`, `ZipPlanner`, `IArchiveWriter` | Complete, 19 planner tests |
| Core grammar | `src/Filekin.Core/Commands/App/Unzip/`, `.../Zip/` | Complete, 21 + 10 parser tests |
| Journal seam | `src/Filekin.Core/Operations/` — `JournalEntry`, `IOperationJournal`, `InMemoryOperationJournal` | Complete, 3 focused tests |
| Windows | `src/Filekin.Infrastructure.Windows/Archives/` — `ZipArchiveReader`, `ZipExtractor`, `ZipExtractionUndo`, `ZipCompressor`, `ZipCompressionUndo` | Complete, 11 + 10 tests including a zip-then-unzip round trip |
| Settings model | `FilekinSettings.ArchiveSettings`, `CollisionPreference`, store normalization | Complete, including defaults, round-trip, normalization, and unknown-field preservation tests |
| App wiring | `CommandExecutionOutcome.Unzip/.Zip`, `CommandExecutor` dispatch, `ShellViewModel.Archive.cs`, `ArchiveRowViewModel`, `Themes/Controls.xaml` (`ArchiveToggle`, `ArchiveRowItem`), `MainWindow.xaml(.cs)` archive surface, completion entries | Complete and live-verified |

#### Completion checklist

- Result-line `[Undo]` action: complete and accessible.
- Fifth Settings category, Archives: complete; preview and collision defaults save through the
  existing settings service.
- Journal and settings normalization coverage: complete.
- Master-specification reconciliation: complete, including explicit supersession of the old
  non-undoable `/unzip` statement and the formerly four-category Settings decision.
- Live WPF QA: preview rendering, root-folder replanning, creation, Undo visibility, and Archives
  settings passed. Long cancellation and disconnected-network behavior remain risk-based future QA,
  not blockers found in this pass.

#### Owner decisions made on 2026-08-27 — now also recorded in the master specifications

- **The redundant-nesting rule, stated positively:** extraction always produces **exactly one new
  folder** in the destination. An archive carrying its own wrapper folder reuses it; an archive of
  loose files gets one named after the archive. This turns `PRODUCT.md:257` ("avoid redundant
  directory nesting when an archive already contains a wrapper directory") into something a user can
  predict without thinking. Pinned by
  `ArchivePlannerTests.BothArchiveShapesProduceExactlyOneNewFolder`.
- **`/unzip` grammar:** `/unzip [-noroot] [-skip] [-overwrite] [-y] <archive...> [destination]`. The
  destination may be a path, `@thisfolder`, or an `@location`, **and need not exist yet**.
- **`/zip` grammar:** `/zip <item...> [name.zip]` — **no switches at all.** The owner cut them
  deliberately: `/unzip` earns switches because it decides where hundreds of files land; `/zip`
  decides one thing and that is already its second argument. The root and overwrite choices live in
  the preview instead. A switch is refused with a message naming it.
- **The preview is the default for both commands.** `-y` skips it for `/unzip`; the Settings toggle
  skips it for both. It is the default rather than opt-in because `/unzip` writes many files at once
  and, until durable history exists, the in-session `[Undo]` is the only way back.
- **Collisions:** the shipped default is **Skip**, the owner personally wants **Overwrite**, so the
  default lives in Settings and either switch overrides it for one command. Overwrite is survivable
  only because the replaced original goes to the **Recycle Bin first** — that is what makes the
  extraction reversible. Do not optimise that away.
- **`/unzip` IS undoable now.** This reverses `ARCHITECTURE.md:1322`. The owner's reasoning: "if it
  means deleting 400 files from the unzip action maybe undo would be good." Undo is cheap here
  because the plan already lists every path written: it deletes only what Filekin created and
  restores replaced originals from the bin. It does **not** need `/history` or the SQLite store. The
  seam is `IOperationJournal`; `InMemoryOperationJournal` is the session-scoped implementation, and
  the durable store later implements the same interface without changing callers.
- **Multiple archives are supported**, not refused. Each gets its own plan and folder, and failures
  are per-archive, matching the partial-success rule at `ARCHITECTURE.md:3091`.
- **Zip only in v1.** `System.IO.Compression` is the standard API; 7z and rar would each mean a
  third-party dependency, which is a product decision rather than an implementation detail. A
  recognised-but-unsupported archive earns a better error than "not an archive".

#### Traps worth knowing

- `ArchivePlanner` validates every entry name **twice** — syntactically, then by re-checking the
  resolved path for containment. Both gates are load-bearing; the end-to-end proof is
  `ZipExtractorTests.AnEntryClimbingOutOfTheFolderNeverLandsOutsideIt`.
- `ZipCompressor` writes to `<output>.filekin-part` and moves it into place only on success, and
  recycles the archive it replaces **after** the new one is built. A truncated zip is worse than no
  zip, because it opens and lies about its contents.
- `ZipExtractor` records a file path **before** writing it, so a cancelled or failed entry leaves
  something undo can clean up rather than an orphan Filekin no longer remembers making.
- Undo order is load-bearing: delete the created file first, **then** restore the original from the
  bin — they share a path. Folders come last, deepest first, and only when empty.
- `Progress<T>` publishes asynchronously. A test asserting on reports right after a short operation
  will race; both archive test files use a private `InlineProgress<T>` instead.
- `dotnet format` flags `ENDOFLINE` on any file written with LF endings. Run `dotnet format` without
  `--verify-no-changes` first, then verify.

#### Choices that look odd but are deliberate

Do not "fix" these without reading the reason first.

- **`ArchiveFormats` has two predicates.** `IsSupported` is zip only; `LooksLikeArchive` also matches
  `.7z`, `.rar`, `.tar`, `.gz`. The second exists so `/unzip bundle.7z` earns "not a format this
  version can open" instead of the misleading "not an archive", and so a trailing `.7z` argument is
  read as an archive rather than mistaken for a destination.
- **`ShellViewModel.Replan()` is `async void`.** It is driven by property setters, which cannot be
  awaited, so it behaves like an event handler. The live WPF pass exercised its root-folder toggle
  path; keep treating failure/cancellation behavior carefully.
- **The folder-name box only applies to a single archive.** With several archives selected each keeps
  its own name, because one name cannot describe them all. `BuildUnzipPlans` enforces that.
- **`ZipPlanner` strips the root only for exactly one source.** Stripping it from several would merge
  unrelated trees into one namespace and collide. With several sources the flag is ignored, not
  refused, because the preview shows the result either way.
- **The preview list is capped at 400 rows** (`MaxArchiveRows`) with an "and N more" row. Long enough
  to judge an archive, short enough that a 50,000-entry zip does not stall the UI.
- **`ArchiveToggle` and `ArchiveRowItem` in `Themes/Controls.xaml` are new.** `ArchiveToggle` is the
  app's first two-state control — nothing else in Filekin used a `CheckBox`, so there was no style to
  reuse. The folder-name box deliberately reuses the existing `LocationEditorTextBox` rather than
  adding another text style.
- **The archive surface reuses `_recycleBin`, `ApplyResult`, and the rich-view flags** already on
  `ShellViewModel`. It is a partial class file, not a separate view model, matching
  `ShellViewModel.Info.cs` and `ShellViewModel.Settings.cs`.

#### Known gaps in the agreed design

- **There is no way to force a preview when the Settings toggle is off.** `-y` skips the preview;
  nothing turns it back on for one command. Accepted for now rather than adding a fifth switch. If it
  bites, the natural fix is a `-preview` switch mirroring `-y`.
- **`/zip` cannot skip its preview from the command line.** By design — `/zip` has no switches — so
  its only control is the Settings toggle, shared with `/unzip`. If the owner wants them independent,
  that is two settings rather than one.
- **The journal is session-scoped.** Closing Filekin loses the ability to undo. That is intentional
  until the SQLite store exists; the Archives settings panel says so explicitly.

### 1. Choose the next app command with the owner

Four app commands then remain unimplemented — `/where`, `/tidy`, `/history`, `/undo`. Each needs the
owner's product discussion before any code, the way `/run`, `/info`, and `/unzip` did. `/history`
and `/undo` additionally need the durable SQLite store; the `IOperationJournal` seam added for
`/unzip` is where they plug in, and `/copy`, `/move`, `/rename`, and `/toss` should start recording
into it at the same time.

### 2. `/info` — approved behavior to preserve

The owner confirmed all of this on 2026-08-27; the reasoning is in `DECISIONS.md`.

- Bare `/info` describes the selection, then the visible folder.
- Type-specific metadata comes from the **Windows Property System**, never per-format parsers.
- An executable's embedded name is **Company**, never "Publisher". Filekin does not verify signatures
  in v1; that stays with the Windows Properties dialog. Do not relabel this row.
- Encoding is shown immediately (the text sniff already read those bytes); **Lines** stays behind a
  `Count` action, beside `SHA-256` and `Calculate`.
- The recursive scan reports on a 250 ms timer, never follows reparse points, reports unreadable
  folders instead of hiding them, and stops when the sheet closes.
- Shortcuts are **revealed, not edited**: Target, Arguments, Start in — no editor, and never
  `IShellLink::Resolve`.
- Info is a field sheet, not a listing, and is deliberately not a sidebar entry.

### 3. Approved behavior to preserve (`/run`)

- `/run <target> [arguments]` is the single app/file-launch command. There is no `/open`.
- Resolve relative targets from the visible Files folder first, then normal `PATH`/`PATHEXT` lookup.
  `@location\child`, absolute paths, shortcuts, associated documents, and ordinary PATH command
  names are supported. Do not crawl the machine for applications.
- GUI executables, shortcuts, and associated documents launch independently through Windows shell
  execution. Console executables and terminal-oriented scripts start in a hosted Filekin terminal,
  so a PATH-installed Python entry point such as `snapmap-midi` does not lock the command bar or
  require manual registration merely to work with `/run`.
- A folder passed to `/run` is currently rejected with a clear message because Files owns folder
  navigation; do not silently open Explorer.
- `/ext` stays distinct: bare `/ext` opens the preferred external terminal at the Files folder, and
  `/ext program args` explicitly launches an independent external process.
- Unknown raw shell commands still begin in the finite persistent runspace. Only a concrete Windows
  console executable that is still running after a short grace period receives the visible Y/N
  offer. This is a fresh relaunch, never live promotion; PowerShell cmdlets/functions are not
  guessed as terminal programs.
- Do not implement the remaining app commands without the owner's product discussion.

### 4. Keep the known quality gaps visible

The accessibility pass remains the largest known quality gap: the Files list/sidebar automation
names are poor, and the terminal cell grid has no assistive-text exposure.

**Tab-strip overflow, observed 2026-08-27.** With three terminal tabs open at the default window
width, the tab strip scrolls horizontally and the last tab is clipped beneath the window buttons.
This is pre-existing and unrelated to `/run`; it needs a product decision on tab overflow behavior
(shrink, scroll, or overflow menu) before it is worth implementing.

**`Esc` stops a running command only while the command bar has keyboard focus.** That is where the
caret sits after Enter and where the `Esc to stop` status is shown, so the normal flow works, but
clicking into the Files list mid-command leaves `Esc` inert. Widening it to the whole Files workspace
would be a keyboard-contract change, so it was not done unilaterally.

**The Info heading is easy to miss, observed 2026-08-27.** It shares the left column, size, and
colour family of the field labels, so it reads as an unlabelled row rather than a title. The owner
hit this, then chose to leave it; if it returns, try separation (space or a hairline), not weight.

Location management is already implemented through the sidebar plus
`/location add|set|rename|remove`; do not reopen that grammar without a new product decision.

The terminal surface covers VT rendering, selection/copy, scrollbars, tab shortcuts, Alt delivery,
mouse reporting, and theme-aware colours. Its one open v1 question is assistive-text exposure.

### Confirmed keyboard contract for a focused terminal

Filekin claims exactly four combinations from a focused terminal; **everything else belongs to the hosted shell**, including plain `Tab`, `Shift+Tab`, `Ctrl+C` with no selection, `Escape`, and `Y`/`N`. Do not add a fifth without an owner decision.

```text
Ctrl+Tab / Ctrl+Shift+Tab   next / previous workspace
Ctrl+Shift+T                new terminal tab at the current Files folder
Ctrl+Shift+W                close the selected terminal tab (confirms while live)
```

Terminal-local keys that never reach the shell only because the shell cannot distinguish them anyway: `Ctrl+Shift+C` (copy), `Ctrl+Shift+V` (paste). `Ctrl+C` copies only when a selection exists; `Ctrl+V` pastes. `Alt+F4` and `Alt+Space` are left to Windows.

## Spike Status

**Complete on the test machine.**

- Disposable project: `spikes/ShellTerminalSpike/`
- Automated result: **25 passed, 0 failed**
- Evidence: `spikes/ShellTerminalSpike/artifacts/latest-results.json`
- Final environment: Windows 10.0.26200 x64, .NET runtime 10.0.10, workspace-local SDK 10.0.400, Microsoft.PowerShell.SDK 7.6.5, external PowerShell 7.6.4, Python 3.13.15
- This is validation code only and is not a production Filekin scaffold.

## Findings

### What Worked

1. **Persistent PowerShell runspace**
   - One hosted runspace preserved `$x = "hello"` for a later `Write-Output $x` invocation.
   - Aliases, functions, and an imported module remained available across separate executions.
   - `InitialSessionState.CreateDefault2()`, `RunspaceFactory.CreateRunspace(...)`, and repeated `PowerShell.Invoke()` against the same runspace were sufficient for the proof.

2. **Files → PowerShell location synchronization**
   - The minimal test UI changed its visible `FILES LOCATION` and set the runspace with `Set-Location -LiteralPath`.
   - The runspace reported the matching FileSystem provider path.
   - `Environment.CurrentDirectory` did not change, proving that process-wide current directory is not required as the primary state model.

3. **PowerShell → Files location synchronization**
   - `cd ..` / `Set-Location` results were read from the runspace's `PathInfo` after command completion and updated the visible test location.
   - The manual UI pass showed `D:\GitHub\filekin\spikes\ShellTerminalSpike` changing to `D:\GitHub\filekin\spikes` after `ps cd ..`.

4. **Non-filesystem provider detection**
   - `Set-Location HKLM:\` reliably produced provider `Registry` and path `HKLM:\`.
   - The test restored the runspace to the prior Files filesystem path immediately, preserving the no-divergence rule.
   - The manual UI displayed `ROUTE TO TERMINAL: provider=Registry; path=HKLM:\` while Files remained at its prior filesystem location.

5. **Finite native commands**
   - `where.exe git` returned stdout and exit code 0.
   - `git status` (run from this non-Git directory) returned stderr and exit code 128; this was an intentional available-machine substitution that proved failure capture.
   - A purpose-built native probe independently proved stdout capture, stderr capture, and a nonzero exit code of 7.

6. **ConPTY terminal session**
   - The path `test terminal surface → ConPTY → PowerShell` worked with UTF-8 pipe input/output.
   - `ResizePseudoConsole(100, 30)` was observed inside PowerShell as `100x30` through `$Host.UI.RawUI.WindowSize`.
   - PowerShell was the root process created with `STARTUPINFOEX` and `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE`.

7. **Interactive child lifecycle**
   - Python 3.13.15 was used because it was installed and stable for automation.
   - `python -q` launched inside the ConPTY-backed root PowerShell, accepted input, and emitted output.
   - `exit()` returned to the same PowerShell, which accepted another command.
   - `exit` in root PowerShell ended the root process/session.

8. **Routing proof**
   - `where.exe git` classified to the finite runspace/result path.
   - bare `python` classified to the ConPTY PowerShell terminal path.
   - `python script.py` classified to the finite path, proving that simple argument-sensitive rules are feasible.

### Failures Encountered and Resolved

- The first ConPTY launch inherited the parent process's redirected stdout instead of using the pseudoconsole output pipe. This reproduced a Windows standard-handle duplication edge case documented by Microsoft Terminal maintainers.
- Setting `STARTF_USESTDHANDLES` while leaving `hStdInput`, `hStdOutput`, and `hStdError` null forced the child to establish standard I/O through ConPTY. After this change, every ConPTY check passed.
- ConPTY pipe handles created by `CreatePipe` are synchronous. Constructing `FileStream` with `isAsync: true` failed. The working implementation uses synchronous handles while servicing input and output on separate tasks/threads as Microsoft recommends.

### Lifecycle / ConPTY Constraints

- ConPTY communication channels must be serviced independently to avoid full-buffer deadlocks.
- Output must continue to be drained through teardown; `ClosePseudoConsole` can emit a final frame.
- `ClosePseudoConsole` terminates attached console clients. Product shutdown should still follow the specified graceful-first policy before using pseudoconsole closure as final teardown.
- A terminal renderer must interpret VT/ANSI sequences; a plain text output control is not sufficient. The spike captures raw VT output and intentionally does not attempt to become a production renderer.

### Unexpected Interactivity Findings

Observed finite-path behavior depends on the host environment:

- In the headless/WPF-like automated path, the unknown native helper saw redirected stdin, received EOF immediately, and exited with a failure code. It had no usable terminal input channel.
- In a manual console-hosted path, the helper saw non-redirected stdin but the Files-style command surface could not reliably deliver input to it; it waited until its self-timeout. Input sent during that wait was later consumed by the parent test UI.

What can be detected reliably:

- executable/argument matches in a deterministic registry before process creation,
- explicit user choice before process creation,
- command completion, output/error streams, and native exit code afterward,
- final runspace provider/location after a PowerShell command completes.

What cannot be detected reliably:

- whether an arbitrary unknown executable will later request terminal input,
- whether a quiet/running process is waiting for input versus doing legitimate finite work,
- whether argument combinations become interactive without tool-specific knowledge.

Promotion finding:

- ConPTY association is supplied to `CreateProcessW` through `STARTUPINFOEX` and `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE` at process creation time.
- No documented supported API attaches an already-running finite-path native process to a newly created ConPTY session.
- A running process therefore cannot realistically be promoted in place. Routing must happen before process creation, or the command must be stopped/allowed to fail and then launched again as a fresh process in a terminal.

Recommended fallback for architecture review:

- route known interactive invocations before creation,
- give finite-path native commands no synthetic interactive stdin,
- when an unknown command fails/hangs in a way the user identifies as interactive, offer an explicit fresh `Run in terminal` relaunch,
- do not claim that the already-running process or its state was promoted.

### Architecture Review

The core proposed architecture is validated:

```text
Files command bar → persistent PowerShell runspace → finite result
terminal tab      → ConPTY → root PowerShell → interactive child
```

No fundamental replacement architecture is recommended. Production implementation should carry forward these evidence-based constraints:

1. Use `STARTF_USESTDHANDLES` with null standard handles when creating the ConPTY root process, especially from a GUI or redirected host.
2. Drain ConPTY input/output independently and through teardown.
3. Route interactive tools before process creation; fallback is fresh relaunch, not live promotion.
4. Detect runspace provider after every command and immediately restore the Files filesystem location if the result is non-filesystem.
5. Treat non-filesystem terminal delegation as creation of a new root PowerShell initialized to the detected provider location; do not imply that the in-process runspace itself moved into ConPTY.

## Files Changed This Session

Initial project-development documents created:
- `AGENTS.md`
- `CLAUDE.md`
- `HANDOFF.md`
- `PROJECT-SETUP.md`

Master specifications updated with the official Filekin product name.

Spike session additions:

- `spikes/ShellTerminalSpike/ShellTerminalSpike.csproj`
- `spikes/ShellTerminalSpike/Program.cs`
- `spikes/ShellTerminalSpike/PowerShellRunspaceBackend.cs`
- `spikes/ShellTerminalSpike/ConPtySession.cs`
- `spikes/ShellTerminalSpike/CommandRouting.cs`
- `spikes/ShellTerminalSpike/SpikeRunner.cs`
- `spikes/ShellTerminalSpike/TestUi.cs`
- `spikes/ShellTerminalSpike/README.md`
- `spikes/ShellTerminalSpike/artifacts/latest-results.json`
- `spikes/Directory.Build.props` (keeps the frozen disposable spike outside production analyzer policy)
- `.tools/dotnet/` (workspace-local .NET SDK 10.0.400 required because the machine had runtimes but no SDK)
- `.tools/dotnet-install.ps1` (official Microsoft installer used for the local SDK)
- 2026-08-25 — a machine-wide .NET SDK 10.0.400 (10.0.4xx GA band, matching `global.json` `latestPatch`) was also installed into `C:\Program Files\dotnet` via the official installer (elevated), so the plain `dotnet` on PATH now builds/tests the solution directly — the gitignored `.tools/dotnet/` bootstrap remains valid but is no longer required on this machine.
- `HANDOFF.md`

Production scaffold additions/updates:

- Initialized the Git repository on branch `main` (no commit created).
- `Filekin.sln`
- `global.json`
- `Directory.Build.props`
- `.editorconfig`
- `.gitignore`
- `LICENSE`
- `README.md`
- `CONTRIBUTING.md`
- `SECURITY.md`
- `src/Filekin.App/`
- `src/Filekin.Core/`
- `src/Filekin.Infrastructure.Windows/`
- `tests/Filekin.Core.Tests/`
- `ARCHITECTURE.md`
- `DECISIONS.md`
- `ENGINEERING-GUARDRAILS.md`
- `FEATURES.md`
- `UX-DESIGN.md`
- `HANDOFF.md`

First production shell-boundary additions/updates:

- `src/Filekin.Core/Shell/IShellBackend.cs`
- `src/Filekin.Core/Shell/ShellExecutionResult.cs`
- `src/Filekin.Core/Shell/ShellLocation.cs`
- `src/Filekin.Core/Shell/ShellTerminalLaunchRequest.cs`
- `src/Filekin.Infrastructure.Windows/Shell/PowerShellRunspaceBackend.cs`
- `src/Filekin.Infrastructure.Windows/Filekin.Infrastructure.Windows.csproj`
- `tests/Filekin.Infrastructure.Windows.Tests/`
- `Filekin.sln`
- `README.md`
- `HANDOFF.md`

Public GitHub repository setup:

- Created and pushed public `mefisme/filekin` with `main` as the default branch.
- `.github/CODEOWNERS`
- `.github/PULL_REQUEST_TEMPLATE.md`
- `.github/dependabot.yml`
- `.github/workflows/ci.yml`
- `README.md` CI badge
- `HANDOFF.md`

Branch governance + terminal-host boundary session:

- Created the active `main` repository ruleset via the GitHub REST API (no file in the repo; the ruleset lives on GitHub).
- Platform-neutral terminal contracts in `Filekin.Core`:
  - `src/Filekin.Core/Terminal/TerminalSize.cs`
  - `src/Filekin.Core/Terminal/TerminalOutputEventArgs.cs`
  - `src/Filekin.Core/Terminal/TerminalExitEventArgs.cs`
  - `src/Filekin.Core/Terminal/TerminalSessionRequest.cs`
  - `src/Filekin.Core/Terminal/ITerminalSession.cs`
  - `src/Filekin.Core/Terminal/ITerminalHost.cs`
- ConPTY terminal-host service in `Filekin.Infrastructure.Windows`:
  - `src/Filekin.Infrastructure.Windows/Terminal/Interop/ConPtyInterop.cs` (LibraryImport P/Invoke + blittable structs)
  - `src/Filekin.Infrastructure.Windows/Terminal/PowerShellExecutableLocator.cs`
  - `src/Filekin.Infrastructure.Windows/Terminal/ConPtyTerminalSession.cs`
  - `src/Filekin.Infrastructure.Windows/Terminal/ConPtyTerminalHost.cs`
  - `src/Filekin.Infrastructure.Windows/Filekin.Infrastructure.Windows.csproj` (`AllowUnsafeBlocks` for LibraryImport marshalling)
- Tests:
  - `tests/Filekin.Infrastructure.Windows.Tests/Terminal/ConPtyTerminalSessionTests.cs`
- `HANDOFF.md`

Command-router session:

- Command routing in `Filekin.Core/Commands/`:
  - `CommandRoute.cs`, `CommandClassification.cs`
  - `IInteractiveCommandRegistry.cs`, `InteractiveCommandRegistry.cs`
  - `ICommandClassifier.cs`, `CommandClassifier.cs`
  - `CommandRouterResult.cs`, `CommandRouter.cs`
- Tests:
  - `tests/Filekin.Core.Tests/Commands/CommandClassifierTests.cs`
  - `tests/Filekin.Core.Tests/Commands/CommandRouterTests.cs`
- `HANDOFF.md`

Maximized-window work-area fix:

- `src/Filekin.App/Views/MainWindow.xaml.cs`
- `src/Filekin.Infrastructure.Windows/Windowing/MaximizedWindowBounds.cs`
- `HANDOFF.md`

`/info` inspection (Claude Code — uncommitted):

- `src/Filekin.Core/Inspection/{InspectionResult,IFileInspector,IAggregateScanner}.cs` (new)
- `src/Filekin.Core/FileSystem/ByteSize.cs` (moved from `src/Filekin.App/ViewModels/ByteSize.cs`, now public)
- `src/Filekin.Core/Commands/App/Info/{InfoInvocation,InfoInvocationParser}.cs` (new)
- `src/Filekin.Infrastructure.Windows/Inspection/Interop/{ShellMetadataInterop,ShellLinkInterop}.cs` (new)
- `src/Filekin.Infrastructure.Windows/Inspection/{WindowsFileInspector,DirectoryAggregateScanner,TextFileReader,FileChecksum,WindowsPropertiesDialog}.cs` (new)
- `src/Filekin.App/ViewModels/{InfoRowViewModel,ShellViewModel.Info}.cs` (new)
- `src/Filekin.App/ViewModels/{CommandExecutionOutcome,CommandExecutor,ShellViewModel,ShellViewModel.Completion,ShellViewModel.Settings,DriveItemViewModel}.cs`
- `src/Filekin.App/Themes/Controls.xaml` (`InfoFieldRow`); `src/Filekin.App/Views/MainWindow.xaml(.cs)`
- `tests/Filekin.Core.Tests/Commands/App/Info/InfoInvocationParserTests.cs` (new)
- `tests/Filekin.Infrastructure.Windows.Tests/Inspection/{WindowsFileInspectorTests,DirectoryAggregateScannerTests,TextFileReaderTests,FileChecksumTests,WindowsPropertiesDialogTests}.cs` (new)
- `DECISIONS.md`, `FEATURES.md`, `UX-DESIGN.md`, `ARCHITECTURE.md`, `HANDOFF.md`

`/run` and the terminal fallback (Codex started, Claude Code finished — committed as `005a4a7`):

- `src/Filekin.Core/Commands/App/Run/{RunInvocation,RunInvocationParseResult,RunInvocationParser}.cs` (new)
- `src/Filekin.Core/Commands/References/{IReferenceResolver,ReferenceResolver}.cs` (`ResolveToken`)
- `src/Filekin.Infrastructure.Windows/Commands/{RunTargetKind,RunTargetResolution,WindowsRunTargetResolver}.cs` (new)
- `src/Filekin.App/ViewModels/TerminalLaunchOutcome.cs` (new)
- `src/Filekin.App/ViewModels/{CommandExecutionOutcome,CommandExecutor,ShellViewModel,ShellViewModel.Completion}.cs`
- `src/Filekin.App/Views/MainWindow.xaml.cs` (Esc stops a busy command)
- `tests/Filekin.Core.Tests/Commands/App/Run/RunInvocationParserTests.cs` (new)
- `tests/Filekin.Core.Tests/Commands/References/ReferenceResolverTests.cs`
- `tests/Filekin.Infrastructure.Windows.Tests/Commands/WindowsRunTargetResolverTests.cs` (new)
- `DECISIONS.md`, `FEATURES.md`, `UX-DESIGN.md`, `ARCHITECTURE.md`, `HANDOFF.md`

## Unresolved Engineering Questions

The spike resolved the feasibility questions above. The owner confirmed the two resulting decisions on 2026-08-25.

## Handoff Template

Agents should update the sections below before stopping meaningful work.

### Last Agent
Codex — 2026-08-27 (paused mid-pass after implementing uncommitted saved-Location rebasing and
backgrounding app-owned filesystem commands; filtered Release validation is green, but full desktop
validation, final formatting checks, live-risk decision, documentation finalization, and commit remain).

The archive work is complete in the current local feature commit. It has not been pushed; branch
protection still requires the normal pull-request/check workflow.

### Work Completed

**`/go` Files navigation (2026-08-27, Codex) — Release-clean, 341/341 full desktop tests,
live-verified against the real WPF window.** A dedicated Core parser owns the deliberate
whole-remainder grammar, optional outer quotes, one-item reference expansion, and relative-to-Files
normalization. `CommandExecutor` validates the directory off the UI thread and returns a navigation
outcome; the existing Shell pipeline performs the actual move. Completion and five master product
documents were updated. Live QA navigated first to `C:\Program Files`, then relatively to
`C:\Program Files\Common Files`, with no quotes and no filesystem mutation.

**Archive hierarchy refresh + current PATH correction (2026-08-27, Codex) — Release-clean,
331/331 full desktop tests, live-verified against the real WPF window.** `RunArchiveAsync` and
`UndoArchiveAsync` now refresh Files independently of rich-view lifetime, including nearest-ancestor
recovery when Undo removes the current folder. `WindowsEnvironmentPath` merges process, Machine, and
User PATH values; `/run` reevaluates that merged PATH per invocation, and each new ConPTY PowerShell
session refreshes it at startup. Tests pin PATH merging/deduplication and prove a ConPTY child can
resolve a Windows command even when its parent process PATH is empty. Live QA showed created ZIP and
extracted folder rows immediately, then verified both Codex launch routes against CLI 0.150.0.

**`/unzip` + `/zip` completion (2026-08-27, Claude Code + Codex) — Release-clean, 328/328 full
desktop tests, formatting and `git diff --check` green, live-verified against the real WPF window.**

Claude Code supplied the archive model/planners, parsers, ZIP reader/extractor/compressor/undo
infrastructure, operation-journal seam, settings model, command dispatch, completion entries, and
archive preview surface. Codex audited that work against the six specifications, exposed the
session Undo action on the result line, added the fifth Archives settings category and persistence
handlers, added journal/settings coverage, reconciled the product/feature/UX/architecture/decision
documents, and ran final Debug/Release, filtered/full, formatting, whitespace, and live UI checks.
The live pass created a ZIP from `README.md`, toggled the root-folder plan, verified the success row
and accessible Undo action, opened Archives settings, and removed the generated QA archive afterward.

**`/info` inspection (2026-08-27, Claude Code) — committed as `57dd1fc`; Release-clean with 0 warnings, 248/248 tests, formatting and `git diff --check` green, live-verified against the real window.**

The owner approved seven product decisions before any code was written; they are all in `DECISIONS.md`
under 2026-08-27 and summarized in the `/info` preservation notes above.

**A probe came before the design.** `AGENTS.md` requires evidence over memory for unfamiliar Windows
APIs, and the whole plan rested on the Windows Property System being able to answer everything. A
throwaway scratchpad probe proved it first: image dimensions, `.wav` duration, `cmd.exe`
company/version, and a shortcut's target all came back through one `IPropertyStore` — **on a
thread-pool (MTA) thread**, which is where inspection actually runs. That is the check that mattered;
shell COM cannot be assumed apartment-free. The probe also settled two things the design would
otherwise have guessed at: `System.ItemTypeText` returns nothing useful, so friendly type names come
from `SHGetFileInfo` with `SHGFI_TYPENAME` instead; and `System.Link.Arguments` cannot be relied on,
so all three shortcut fields come from `IShellLink`.

**Shape.** `Filekin.Core.Inspection` owns `IFileInspector`, `IAggregateScanner`, and the result
types. `Filekin.Infrastructure.Windows.Inspection` owns `WindowsFileInspector`,
`DirectoryAggregateScanner`, `TextFileReader`, `FileChecksum`, `WindowsPropertiesDialog`, and the two
interop files. `ShellViewModel.Info.cs` owns the sheet. `InfoInvocationParser` parses `/info` before
the reference rewrite, exactly as `/run` does, so a multi-item `@selection` survives.

**`ByteSize` moved to `Filekin.Core.FileSystem` and became public.** It was `internal` in the App
layer, and the inspector needed it. One formatter for Files, Recycle Bin, Drives, and Info means they
cannot drift apart on rounding.

**Two defects found by the tests I wrote, both real:**

- **The UTF-8 validator called broken files valid.** It tolerated a multi-byte sequence running past
  the end of the block, which is right when the 8 KB sniff boundary cut the sequence in half and
  wrong at a real end-of-file. `[0x61, 0xE9, 0x62]` was reported as UTF-8. It now takes whether the
  block is the whole file and only forgives a truncated tail when the file was actually truncated.
- **A recursive delete cannot walk through a junction.** The scanner's own junction test could create
  the link but not clean it up (`UnauthorizedAccessException`). The test now unlinks reparse points
  before deleting the tree — the same asymmetry the scanner itself exists to respect.

**Live WPF QA**, on the real `MainWindow` shown off-screen and driven through the same public
`ShellViewModel` API the UI calls:

| Case | Result |
| --- | --- |
| `/info notes.txt` | Type / Size / Path / Created / Modified / Encoding, plus SHA-256 and Lines |
| `Calculate` on SHA-256 | real digest, action becomes `Copy` |
| `Count` on Lines | `3`, action clears |
| `/info photo.jpg` | `Dimensions 1,920 × 1,200` |
| `/info sound.wav` | `Duration 0:01` |
| `/info app.lnk` | Target, `Arguments --project "D:\Work"`, `Start in D:\Work` |
| `/info C:\Windows\System32\cmd.exe` | `Architecture x64`, `Company Microsoft Corporation`, **no** Publisher row, no Lines row |
| bare `/info`, nothing selected | the folder, scan filled in 7 files / 2 folders / 778.2 KB |
| bare `/info`, 2 rows selected | `2 selected items`, Windows Properties hidden |
| `/info not-a-real-file.txt` | `This item no longer exists.`, no invented rows |
| `/inf` + Tab | completes to `/info` |
| `/info C:\Windows` then close | `9,512…` while open; **identical 2.5 s after closing** — the scan really stops |

The Windows Properties escape hatch was verified separately, since it opens a modal system dialog: a
probe called it, waited for a new top-level window, found `… Properties`, and closed it cleanly.

**A defect the owner found in live use, 2026-08-27 — and the reason that probe was not enough.**
`Windows Properties` on `C:\Users\<user>` produced the shell's own "Unspecified error" box. The probe
had only tried a file. A second probe measured four APIs across five target kinds and isolated it
exactly: `ShellExecuteEx` with the `properties` verb works for files, ordinary folders, `C:\Users`,
and `C:\`, and fails with `ERROR_CANCELLED` (1223) **only** for the user profile folder, whose
properties handler will not accept a plain file-system path. `SHObjectProperties` worked for all
five and is the API documented for the job. `WindowsPropertiesDialog` now uses it, and passes the
Filekin window handle as owner so the dialog cannot be lost behind the app.
`WindowsPropertiesDialogTests` pins it against the real shell in the CI-excluded
`RequiresInteractiveShell` category, with the profile folder as the first case — the lesson being
that one sample of one target kind is not coverage for a shell API.

The user's real `settings.json` was hash-compared before and after and was unchanged. Note that the
owner had a Release build of Filekin running during this session, so the Release verification build
was redirected to a scratch output directory rather than closing their app.

**Files added:** `src/Filekin.Core/Inspection/{InspectionResult,IFileInspector,IAggregateScanner}.cs`;
`src/Filekin.Core/FileSystem/ByteSize.cs` (moved from the App layer);
`src/Filekin.Core/Commands/App/Info/{InfoInvocation,InfoInvocationParser}.cs`;
`src/Filekin.Infrastructure.Windows/Inspection/{WindowsFileInspector,DirectoryAggregateScanner,TextFileReader,FileChecksum,WindowsPropertiesDialog}.cs`
and `Inspection/Interop/{ShellMetadataInterop,ShellLinkInterop}.cs`;
`src/Filekin.App/ViewModels/{InfoRowViewModel,ShellViewModel.Info}.cs`; five new test files.
**Changed:** `src/Filekin.App/ViewModels/{CommandExecutionOutcome,CommandExecutor,ShellViewModel,ShellViewModel.Completion,ShellViewModel.Settings,DriveItemViewModel}.cs`;
`src/Filekin.App/Themes/Controls.xaml`; `src/Filekin.App/Views/MainWindow.xaml{,.cs}`;
`DECISIONS.md` (seven new entries), `FEATURES.md`, `UX-DESIGN.md`, `ARCHITECTURE.md`, `HANDOFF.md`.
**Deleted:** `src/Filekin.App/ViewModels/ByteSize.cs`.

**Still open:** there is no `Filekin.App` test project, so the Info sheet's row/scan lifecycle is
covered by live QA rather than unit tests. Adding one is a structural change left for an owner
decision.

**`/run` + unknown-console fallback finished (2026-08-27, Claude Code) — UNCOMMITTED; Release-clean with 0 warnings, 214/214 tests (including both real Recycle Bin tests), formatting and `git diff --check` green, live-verified against the real window.**

Picked up Codex's working tree, fixed the three lifecycle issues it had left open, found and fixed
two more, and validated the whole thing end to end.

**The three issues Codex flagged.**

1. `ExecuteCommandAsync` used the mutable `_currentPath` field across every await. It is now captured
   once into `commandFolder` and used for the execution *and* the relaunch, so navigating Files
   mid-command cannot move where the command or its relaunch runs. The "did the command `cd`?"
   comparison now measures against that captured folder rather than the live one, so a command that
   never moved the shell no longer yanks Files back from wherever the user navigated to.
2. The relaunch inside `catch (OperationCanceledException) when (_terminalFallbackAccepted)` is now
   `RelaunchInTerminal`, which catches its own failures and reports them inline. That catch block runs
   inside the `async void` key handler, so a throw there had nowhere to go.
3. The fallback wait is now `OfferTerminalFallbackIfStillRunningAsync`, which re-checks both
   `execution.IsCompleted` and `commandCancellation.IsCancellationRequested` before offering. The old
   code offered whenever `Task.Delay` lost the race — including when it lost because the user had just
   pressed Esc, which put a prompt on screen for a command that was already stopping.

**Two more found while reading the diff.**

- **The UI thread was doing filesystem and process work.** `ShouldOfferTerminalFallback` walks `PATH`
  and reads PE headers, and `ExecuteRun` resolved targets and called `ShellExecute` — all synchronously
  on the dispatcher, against the Performance guardrail. The probe now runs in `Task.Run`, and the whole
  `/run` resolve-and-launch loop is `LaunchRunTargets` inside `Task.Run`. Starting a ConPTY session off
  the UI thread is safe because `ConPtyTerminalSession` buffers output until the first renderer
  subscribes.
- **The failure messages were unreadable.** A missing target produced `Nothing launched.
  definitely-not-installed-xyz: Could not start definitely-not-installed-xyz: An error occurred trying
  to start process 'definitely-not-installed-xyz' with working directory '…'. The system cannot find
  the file specified.` `RunTargetResolution` now carries `FoundOnDisk`, so a name that never resolved
  reports `definitely-not-installed-xyz: not found in this folder or on PATH.`, and the
  `Nothing launched.` / `Launched n; m failed.` prefix is used only for a genuine multi-target batch.

**Live WPF QA.** Synthetic OS input still cannot reach a foreground window in this environment, so —
as in the Settings session — a throwaway harness in the scratchpad showed the **real** `MainWindow`
off-screen, drove it through the same public `ShellViewModel` API and the same routed
`PreviewKeyDown` events the UI raises, and captured `RenderTargetBitmap` PNGs. Every case below was
observed on the real window:

| Case | Result |
| --- | --- |
| `/run Projects` | `✕ Projects: folders are navigated in Files, not run.` no tab |
| `/run hello.ps1` | tab `Hello · …`, script output rendered, command bar silent |
| `/run "…\spaced tool.cmd" alpha` | tab `Spaced tool · …`, quoted path and argument intact |
| `/run notes.txt` | `✓ Launched notes.txt.` Notepad started, no tab |
| `/run definitely-not-installed-xyz` | `✕ … not found in this folder or on PATH.` |
| `/ru` + Tab | completes to `/run` |
| `ping -n 8 127.0.0.1` | no offer at 1s; offer at 3s |
| … then `N` | `… ping is still running · Esc to stop`, still busy |
| … then `Esc` | `Command stopped.`, no tab created |
| `ping -n 30 …` then `Y` | new tab `Ping · …` with live ping output, confirm strip cleared |
| `Start-Sleep -Seconds 5` | **no** offer after 3s — a cmdlet resolves to no console image |
| navigate during a command | Files stays where the user navigated |

The user's real `settings.json` was hash-compared before and after and was unchanged. No orphan
`Filekin`, `notepad`, or `PING` process remained; the harness only closes editors it started itself.

An orphaned Release-build `Filekin.exe` from the previous session (PID 75920, ~1.7 h old) was locking
the Release output directory and was closed gracefully before the build.

**Files changed by this pass:** `src/Filekin.App/ViewModels/{ShellViewModel,CommandExecutor}.cs`;
`src/Filekin.Infrastructure.Windows/Commands/{RunTargetResolution,WindowsRunTargetResolver}.cs`;
`tests/Filekin.Core.Tests/Commands/{App/Run/RunInvocationParserTests,References/ReferenceResolverTests}.cs`;
`tests/Filekin.Infrastructure.Windows.Tests/Commands/WindowsRunTargetResolverTests.cs`;
`DECISIONS.md` (four new entries), `FEATURES.md`, `UX-DESIGN.md`, `ARCHITECTURE.md` (new
implementation section plus two reconciled Topic 5H passages), `HANDOFF.md`.

Six tests were added: an unknown `@` reference stays literal for `/run`; a quoted target with spaces
stays one target; `ResolveToken` ignores non-reference tokens; a GUI `.exe` stays external; a `.ps1`
routes to a terminal even though it is not on `PATHEXT`; an unresolvable name is attempted rather than
refused and reports `FoundOnDisk: false`.

**Still true:** there is no `Filekin.App` test project, so the fallback state machine is covered by
live QA rather than by unit tests. Adding one is a structural change and was left for an owner
decision.

**What Codex had built (unchanged by this pass):**

- Added raw-token `@` resolution through `IReferenceResolver.ResolveToken`, preserving literal paths
  rather than PowerShell-quoting them. `RunInvocationParser` parses `/run` before the ordinary shell
  rewrite, keeps target and argument boundaries, expands target/argument references, supports a
  multi-item `@selection`, and rejects extra arguments when selection expands to multiple targets.
- Added `WindowsRunTargetResolver`: visible Files folder wins before `PATH`/`PATHEXT`; registered
  interactive tools and `.bat`/`.cmd`/`.com`/`.ps1`/`.py` targets route to a hosted terminal; PE
  subsystem inspection distinguishes `WindowsCui` console `.exe` files from GUI `.exe` files;
  documents/shortcuts remain external shell launches; folders are classified separately.
- `CommandExecutor` special-cases `/run`, safely constructs the hosted PowerShell invocation with
  single-quoted arguments, supports multiple terminal launches, and uses the existing Windows shell
  launcher for GUI/doc/shortcut targets. It also exposes the predicate/relaunch operations used by
  the delayed raw-command fallback.
- `ShellViewModel` starts a concrete but unregistered console command in the finite runspace, waits
  two seconds, then displays `Run it again in a terminal tab?` if it is still active. Y cancels the
  finite invocation and relaunches fresh in a hosted tab. N/Esc on the prompt continues it and shows
  the explicit `Esc to stop` status. `MainWindow` routes command-bar Esc to cancellation while busy.
- Added `/run` to the command completion catalog with `Launch a file or application`.

Files currently modified/untracked: `src/Filekin.App/ViewModels/{CommandExecutionOutcome,
CommandExecutor,ShellViewModel.Completion,ShellViewModel,TerminalLaunchOutcome}.cs`;
`src/Filekin.App/Views/MainWindow.xaml.cs`; `src/Filekin.Core/Commands/References/{IReferenceResolver,
ReferenceResolver}.cs`; `src/Filekin.Core/Commands/App/Run/*`;
`src/Filekin.Infrastructure.Windows/Commands/{RunTargetKind,RunTargetResolution,
WindowsRunTargetResolver}.cs`; `tests/Filekin.Core.Tests/Commands/References/ReferenceResolverTests.cs`;
`tests/Filekin.Core.Tests/Commands/App/Run/*`; and
`tests/Filekin.Infrastructure.Windows.Tests/Commands/*`.

All of the above is now finished; the surviving open questions are recorded in **Immediate Next
Task**, section 3.

**Command-bar completion (2026-08-26, Codex) — committed as `feat(app): add command bar completion`; Release-clean, 194/194 tests, formatting and `git diff --check` green, live-verified.**

Completion is explicit rather than ambient: typing never opens UI. Tab owns only a leading matching
`/` command token or a matching known `@` reference token. A unique match completes immediately. An
ambiguous match extends the common prefix and opens a compact overlay above the command bar; the row
pairs a command with a concise description or a reference with its resolved path. Only implemented
commands are listed. Intrinsic references, ordered saved Locations, and available Windows known-folder
aliases share the same catalog and resolver precedence; duplicate user/known names appear once with
the user Location winning.

While open, Up/Down wraps through the list, Shift+Tab moves backward, Tab accepts without executing,
and Esc closes while preserving the draft. Enter closes the overlay and executes exactly the typed
text. With no overlay open, Up/Down keeps command-history recall. Unknown PowerShell `@` forms and
non-command slash text are not claimed. The popup uses a custom Filekin template and does not reflow
the Files workspace.

Core owns token detection, filtering, common-prefix calculation, and safe token replacement under
`Filekin.Core.Commands.Completion`; eight tests cover bare `/` discovery, inline references, unknown
PowerShell syntax, embedded `@` text, non-leading slash text, reference subpaths, case-insensitive
whole-token replacement, and ambiguous common prefixes. WPF owns explicit invocation, selection, dismissal,
pointer acceptance, and the transient overlay in `MainWindow`/`ShellViewModel.Completion`.

Live QA against the real Debug window verified: `/r` + Tab became `/re` and showed `/recycle` and
`/rename` with descriptions; Down highlighted `/rename`; Tab accepted it without execution; `@thi`
completed directly to `@thisfolder`; bare `@` showed known references with real resolved paths and
`@selection` with the selected path; Esc dismissed the list with `@` unchanged. Accessibility exposed
the popup as a separate `Command suggestions` list whose row names include both token and description.
The first live launch caught a WPF-only defect — `Popup.IsOpen` defaults to TwoWay and rejected the
read-only view-model property — fixed by making that binding explicitly OneWay.

Final validation: serial Release solution build with build servers disabled passed with 0 warnings
and 0 errors; the full desktop suite passed Core 112/112 and Windows infrastructure 82/82 (**194
total**, including both real Recycle Bin tests); `dotnet format --verify-no-changes --no-restore`
and `git diff --check` passed. The QA app was closed and no Filekin test window remained.

Files changed/added: `src/Filekin.Core/Commands/Completion/*`;
`tests/Filekin.Core.Tests/Commands/Completion/CommandCompletionTests.cs`;
`src/Filekin.App/ViewModels/ShellViewModel.Completion.cs`;
`src/Filekin.App/Themes/Controls.xaml`; `src/Filekin.App/Views/MainWindow.xaml{,.cs}`; completion
sections in `DECISIONS.md`, `FEATURES.md`, `UX-DESIGN.md`, and `ARCHITECTURE.md`; `HANDOFF.md`.

**Settings surface (2026-08-26, Claude Code) — Release-clean with 0 warnings, 186/186 tests, formatting and `git diff --check` green, verified against a real running window. COMMITTED in `e7a9f81`.**

Settings was the last unbuilt v1 seam: the sidebar footer had a `Settings` label with nothing behind it, and three separate preferences had nowhere to live. This pass built the surface and all three.

**The surface.** `/settings` and the sidebar footer entry open the same rich view over the preserved Files workspace — the same family as `/recycle`, `/places`, and `/drives`, so Esc/Back dismissal, focus restore, path-bar hiding, and command-bar availability all came free. A category rail on the left (Appearance, Startup, Terminal, Advanced) drives one panel on the right. Option rows are single-click, matching Places and Drives. Nothing has a Save button: every choice writes `settings.json` immediately and reports an inline failure if the write does not succeed.

**One settings owner.** `UserSettingsService` now holds the in-memory document; `SettingsBackedLocationCatalog` reads and mutates through it instead of owning its own `FilekinSettings`. This was a latent data-loss bug, not a refactor for tidiness: the catalog rebuilt the whole document from its own list on every Location edit, so the first `/location add` after this work would have erased the theme, accent, startup target, and interactive programs. `UserSettingsServiceTests.ALocationEditDoesNotDiscardAPreference` pins it.

**Appearance — theme.** Dark (default), Light, and Follow system. `Tokens.Light.xaml` is a key-for-key twin of `Tokens.Dark.xaml`; `ThemeManager` swaps the whole dictionary, located by a `ThemeName` sentinel key rather than by merge order. Follow system reads `HKCU\...\Themes\Personalize\AppsUseLightTheme` and re-resolves live on `WM_SETTINGCHANGE`/`ImmersiveColorSet`. The light grounds/lines/text come from the light half of the owner's original *Filekin Files* colour study (artifact `36afd639`), whose dark half is the palette already shipping — the two sets are one design, not two guesses.

**Appearance — accent.** Six accents (Blue default, Teal, Green, Orange, Pink, Purple), each with a dark and a light variant. `ThemeManager` writes `AccentBrush`, `AccentInkBrush`, `AccentDimBrush`, `AccentLineBrush`, and `DirBrush` directly into `Application.Resources`, above the palette dictionary, so a top-level entry shadows the merged one and a later theme swap keeps the accent. The dim/hairline alphas reproduce the shipped blue exactly (`0x26`/`0x4D` dark, `0x1F`/`0x52` light).

**Terminal colours.** A terminal renders raw cells and never reads the resource dictionary, so it was hard-coded to the dark palette. `TerminalPalette` now holds the ground, default text, caret, selection, and sixteen ANSI colours; `ThemeManager` points it at the right set and `TerminalControl` repaints on its `Changed` event (subscribed on `Loaded`, released on `Unloaded`, so a closed tab is not kept alive by a static event). ANSI colours are never accent-tinted; the light set darkens them because the standard bright colours vanish on a light ground.

**Startup.** `openFilesAtLaunch` is `{ target: home | location | folder, name?, path? }`. `StartupLocationResolver` turns it into one folder for one launch and never rewrites the preference: a removed Location or an unreachable path falls back to Home with a small non-blocking notice, and the setting stays for a later launch. A Location **rename** follows through into the startup target inside the same durable write; a **remove** deliberately does not, so the user finds the broken target in Settings and repairs it. Nothing here touches `$PROFILE` (owner instruction, 2026-08-26).

**Interactive programs.** `InteractiveCommandRegistry` gained `ReplaceUserPrograms`, a public `BuiltInPrograms` list, and `IsBuiltIn`. `CommandExecutor` now receives the registry instead of constructing one, so a Settings change reaches the live classifier with no restart. Built-in rules are listed but not removable; a user rule is a plain program name normalised the same way the classifier normalises an invocation (`C:\tools\vim.exe` and `vim` are one rule) and is not argument-sensitive.

**Advanced.** The settings path, Open settings.json, and Show in Files.

**Files changed:** `src/Filekin.Core/Commands/InteractiveCommandRegistry.cs`; `src/Filekin.Infrastructure.Windows/Settings/{FilekinSettings,FilekinSettingsStore,SettingsBackedLocationCatalog,UserSettingsService,StartupLocationResolver}.cs`; `src/Filekin.Infrastructure.Windows/Theming/WindowsAppTheme.cs`; `src/Filekin.Infrastructure.Windows/Windowing/SystemThemeNotifications.cs`; `src/Filekin.App/Theming/{ThemeManager,AccentPalette,TerminalPalette}.cs`; `src/Filekin.App/Themes/{Tokens.Dark,Tokens.Light,Controls}.xaml`; `src/Filekin.App/ViewModels/{SettingsViewModels,ShellViewModel.Settings,ShellViewModel,CommandExecutor,CommandExecutionOutcome}.cs`; `src/Filekin.App/Controls/TerminalControl.cs`; `src/Filekin.App/Views/MainWindow.xaml{,.cs}`; five new test files; `DECISIONS.md`, `FEATURES.md`, `UX-DESIGN.md`, `ARCHITECTURE.md`, `HANDOFF.md`.

**Two real defects found and fixed during verification:**

- **A relative `ResourceDictionary.Source` resolves against the entry assembly**, not the assembly that wrote it. `new Uri("Themes/Tokens.Light.xaml", UriKind.Relative)` worked when `Filekin.exe` was the process, and threw `Cannot locate resource` the moment anything else hosted the window. Fixed with an assembly-qualified pack URI whose name is read from the assembly at runtime — note the assembly is `Filekin`, **not** `Filekin.App`, so writing the name out by hand would have been wrong too.
- **An event handler in a `ResourceDictionary` template does not compile** — `Controls.xaml` has no `x:Class`. The Remove button in `ProgramRowItem` carries no `Click`; the owning `ListBox` handles the bubbled `Button.Click` and reads the row from the button's `DataContext`.

**Live verification.** Synthetic keyboard and mouse input could not reach the foreground window in this session (`SetForegroundWindow` refused, and `mouse_event` clicks never arrived — the running app was confirmed alive and responding, and a `PrintWindow` capture showed the UI unchanged after each click). Rather than skip verification, a throwaway offscreen WPF harness in the scratchpad showed the **real** `MainWindow`, drove it through the same public `ShellViewModel` API the UI calls, and captured `RenderTargetBitmap` PNGs. Verified that way: dark and light both render the whole window (chrome, sidebar, rich view, status bar) with no unthemed patch; all six accents draw swatches and re-tint the window; the Files listing's directory colour follows the accent; the Settings panels for all four categories lay out correctly; `vim` adds and appears as `added` while built-ins show as `built-in`; `ssh` is refused as already built in; `two words` is refused with the name rule; a real hosted PowerShell tab renders light-on-light with a pink caret under `theme: light, accent: pink`. All four startup cases were run against a real `ShellViewModel.InitializeAsync`: a saved Location opens `D:\GitHub`; a removed Location opens Home with `@gone is no longer a saved Location.`; an explicit folder opens it; an unreachable `E:\camera` opens Home with `not available right now` **and the preference was still in the file afterwards**. The user's real `settings.json` was backed up before the run and restored after; no orphan process remained.

**`/places` and `/drives` rich views (2026-08-26, Claude Code) — Release-clean, 144/144 tests, formatting and `git diff --check` green, live-verified with keyboard and mouse. COMMITTED in `e7a9f81`.**

Codex had built most of the `/places` back end and then ran out of context mid-task, leaving the solution **not compiling**. This pass fixed that, finished Places, and built `/drives` end to end.

**What Codex left broken, and why:**

- `CloudStorageInterop.SHLoadIndirectString` declared its output buffer as `StringBuilder`. Source-generated P/Invoke does not marshal `StringBuilder` (**SYSLIB1051**), so the project failed to build. It is now a pinned `Span<char>`; the caller trims at the first NUL. Verified against the real export table that shlwapi exports `SHLoadIndirectString` with **no `W` suffix**, so `EntryPoint` is spelled exactly that way — `LibraryImport` does not append the suffix the way `DllImport` + `CharSet` does.
- Three **CA1859** errors (interface-typed private parameters) in `WindowsPlacesProvider` and `WindowsRegisteredCloudRootSource`, and three **CA1861** errors (inline constant arrays in repeated assertions) in `WindowsPlacesProviderTests`.
- No Places UI existed at all. `MainWindow.xaml` had zero Places markup — the view model was wired to nothing.

**Places.** Six common folders in fixed order (Desktop, Documents, Downloads, Pictures, Music, Videos) when they resolve, then cloud sync roots registered for the current Windows user, sorted by display name. Home/user profile is deliberately absent. Rows carry a `COMMON`/`CLOUD` section caption on the first row of each group rather than a second collection or a stock `GroupStyle`. Live result on this machine: 9 destinations, with `Dropbox`, `iCloud Drive`, and `OneDrive - Personal` resolved from the Windows registration — no vendor names are hardcoded anywhere.

**Drives.** `DriveLocation`/`DriveKind`/`IDrivesProvider` in Core; `WindowsDrivesProvider` in the Windows project. Sorted by root. Each row shows root, volume label, type (Local/USB/Network/Optical/Other), `free of total`, and a restrained usage bar. Assigned but unreachable drives stay visible, dimmed, with `No media` (removable/optical) or `Unavailable` (everything else), and never navigate — verified live with both a click and Enter on a real empty optical drive.

- **Enumeration cannot block the view.** `DriveInfo.Name` and `DriveType` are local metadata, but `IsReady`, `VolumeLabel`, and the capacity properties can block for seconds waking a sleeping device or reaching a dead network mapping. Each drive is probed on its own task under a 2-second overall cap; anything that has not answered is reported unavailable rather than waited on. The whole enumeration also runs off the UI thread.
- Capacity is `null` when a drive is not ready, so an unavailable row cannot display invented numbers. A test asserts capacity is present **exactly when** the drive is available.

**Live drive refresh (owner-approved during this session).** `/drives` re-enumerates while it is on screen when a volume arrives or leaves. `VolumeChangeNotifications` in the Windows project recognizes `WM_DEVICECHANGE` with `DBT_DEVICEARRIVAL`/`DBT_DEVICEREMOVECOMPLETE` and `DBT_DEVTYP_VOLUME`, read out of the `DEV_BROADCAST_HDR` payload; `MainWindow.WindowProcedure` (already installed for the maximized work-area fix, now an instance method) restarts a 600 ms `DispatcherTimer` instead of enumerating inside the window procedure. The timer both coalesces the burst a single insertion produces and lets the volume settle, because it is not queryable the instant the first broadcast arrives. Refresh only runs while `IsDrivesOpen`.

- No `RegisterDeviceNotification` call is needed: Windows broadcasts **volume** events to every top-level window unregistered. Device *interface* notifications would need registration, but `/drives` only ever shows drive letters.
- Four tests cover the payload arithmetic: volume arrival and removal are reported; a non-volume device type (a serial port) is not; `DBT_DEVICEQUERYREMOVE` is not (it is a request, not a completed change); and a null `lParam` is not, because Windows sends event types such as `DBT_DEVNODES_CHANGED` with no header at all.
- Live-verified without touching the app: with `/drives` open and focused, `subst X: …` from a separate process made the status go `3 drives · 1 unavailable` → `4 drives · 1 unavailable` with a fully populated `X:\` row sorted into place, and `subst X: /d` took it straight back to `3 drives`.

**Shared behavior.** Sidebar entry and slash command open the same surface. The filesystem path bar hides while either view is visible (`IsFilesContentVisible` now excludes all three rich views). Files path, selection, and `@selection` stay preserved underneath. Esc and Back dismiss. The command bar stays available. Single click or Enter navigates and dismisses; navigation only dismisses if it actually succeeded.

**Two defects found and fixed during this pass:**

1. **`` is a page glyph, not a folder.** Every Places common row and the Places header rendered a document-with-folded-corner icon. Confirmed by rendering the candidate glyphs from `segmdl2.ttf` side by side: `E8B7` is a page, **`ED25`** is the folder. Note that a glyph-coverage check is useless here — `CharacterToGlyphMap.ContainsKey(0xE8B7)` returns `true`, because the codepoint *is* mapped, just to the wrong picture. Render the glyph and look at it.
2. **Window re-activation dropped the keyboard row.** Places and Drives rebind their collections wholesale on refresh. `RefreshPlacesAsync`/`RefreshDrivesAsync` now return whether the rows actually changed and only publish when they did, and `RefreshWorkspaceAfterReturnAsync` captures and restores the focused row and scroll offset for whichever rich view is open — the same treatment the Recycle Bin already had. `WorkspaceRefreshResult.VisibleRichViewChanged` now means "whichever rich view is visible got new content", not "the Recycle Bin changed".

Files changed/added this pass: `src/Filekin.Core/Navigation/{DriveLocation,IDrivesProvider}.cs` (new); `src/Filekin.Infrastructure.Windows/Navigation/{WindowsDrivesProvider}.cs` (new), `src/Filekin.Infrastructure.Windows/Windowing/{VolumeChangeNotifications}.cs` (new), and fixes to `Navigation/{WindowsPlacesProvider,WindowsRegisteredCloudRootSource,Interop/CloudStorageInterop}.cs`; `src/Filekin.App/ViewModels/{DriveItemViewModel}.cs` (new), `{PlaceItemViewModel,CommandExecutionOutcome,CommandExecutor,ShellViewModel}.cs`; `src/Filekin.App/Themes/Controls.xaml` (`PlaceRowItem`, `DriveRowItem`); `src/Filekin.App/Views/MainWindow.xaml(.cs)`; `tests/Filekin.Infrastructure.Windows.Tests/Navigation/{WindowsDrivesProviderTests}.cs` and `Windowing/{VolumeChangeNotificationsTests}.cs` (both new), plus `WindowsPlacesProviderTests.cs`; `HANDOFF.md`.

Validation: `dotnet build Filekin.sln -c Release` clean, 0 warnings. `dotnet test -c Release --filter "TestCategory!=RequiresInteractiveShell"` passed Core 96/96 and Windows 52/52 (**148 total**). `dotnet format --verify-no-changes` and `git diff --check` pass. Note that Codex's new files arrived with **LF** line endings and failed `dotnet format`'s `ENDOFLINE` rule; `dotnet format` without `--verify-no-changes` fixed them.

Live QA against the Release build (driver in the session scratchpad, built per the Live QA Notes below): `/places` and `/drives` open from the command bar; arrow keys move the row; Enter navigates from both a COMMON and a CLOUD row and from a drive row; a single click navigates from both surfaces; Enter and click on the unavailable optical drive do nothing; Esc dismisses both without navigating; the path bar hides while a view is open and returns after. The app closed with no orphan process.

**Settings-backed user Locations (2026-08-26, Codex) — COMMITTED in `e7a9f81`; final validation below.**

- Added the first readable settings schema at `%AppData%\Filekin\settings.json`: an ordered `locations` array of `{ "name", "path" }` objects. The schema and precedence rules are recorded in `DECISIONS.md`.
- Added `FilekinSettingsStore`, which accepts comments/trailing commas and unknown fields, validates each Location independently, leaves malformed input unchanged, preserves unknown fields across load/save, and replaces a file through a same-directory temporary file. First launch creates a readable empty settings file.
- Replaced the five fake sidebar Locations with the validated settings entries. Clicking one navigates the Files hierarchy; its active marker follows the exact current path. Missing/offline destinations remain saved and report `Location unavailable` when used.
- Added `UserNamedLocationResolver` and `CompositeNamedLocationResolver`. The exact ordered settings snapshot used by the sidebar now also supplies command-bar `@name` resolution; explicit user Locations take priority over Windows known-folder aliases.
- The owner confirmed managing Locations rather than generic references. Added `/location add <name> <path>`, `set <name> <path>`, `rename <name> <new-name>`, and `remove <name>`. `set` changes only the saved path; remove changes only settings and explicitly reports that the folder was not deleted.
- Turned the sidebar `+` into a real accessible Add Location action. The compact in-sidebar editor supports name/path entry, Enter/Escape, atomic name+path edits, and pointer-only removal. A restrained right-click context menu on existing entries exposes Edit and Remove.
- Added a settings-backed catalog behind `IUserLocationEditor`; commands, sidebar rows, navigation, and reference resolution all use that single catalog. Mutations save before publishing the new snapshot, so a write failure cannot leave runtime state ahead of disk.
- Tests added: three Core resolver tests, four `/location` command tests, seven settings-store tests, and seven settings-backed catalog mutation tests.

Files changed/added: `src/Filekin.Core/Commands/References/{NamedLocation,UserNamedLocationResolver,CompositeNamedLocationResolver,IUserLocationEditor}.cs`; `src/Filekin.Core/Commands/App/Locations/LocationCommand.cs`; `src/Filekin.Infrastructure.Windows/Settings/{FilekinSettings,FilekinSettingsStore,SettingsBackedLocationCatalog}.cs`; `src/Filekin.App/ViewModels/{CommandExecutor,ShellViewModel}.cs`; `src/Filekin.App/Themes/Controls.xaml`; `src/Filekin.App/Views/MainWindow.xaml(.cs)`; matching Core/Windows tests; the master specifications; `HANDOFF.md`. The owner's agent-relay/MCP proposal changes were preserved.

Validation: `dotnet build Filekin.sln -c Release --no-restore -m:1` passed with 0 warnings/errors. `dotnet test Filekin.sln -c Release --no-build --no-restore -m:1 --filter "TestCategory!=RequiresInteractiveShell"` passed Core 96/96 and Windows 40/40 (**136 total**). The two real-Recycle-Bin desktop tests were deliberately excluded because this change does not touch the bin. `dotnet format Filekin.sln --verify-no-changes --no-restore` and `git diff --check` pass. Live Release QA confirmed: the five fake entries are gone; the `+` is an accessible Add Location button; the editor opens compactly with the current Files path prefilled; focus moves to Name; Escape closes it and restores workspace focus; invalid names stay in the editor with clear inline feedback; and the app closes without an orphan process. No real user Location was created during that initial QA; later follow-up QA used an owner-created populated row without mutating it.

Follow-up UI fix: the owner found a white native icon/checkmark gutter covering the left side of the dark Location context menu. `LocationContextMenu` now owns its complete popup template (`Border` + `ItemsPresenter`) and disables the stock drop shadow instead of combining Filekin menu items with the Windows menu shell. Release build remains clean, and live QA against a populated Location verified that both the normal and keyboard-highlighted menu states render without the white strip.

**Alt shortcuts and terminal mouse reporting (2026-08-26, Claude Code) — Release-clean, 117/117 tests, formatting and `git diff --check` green, live-verified against the real Claude Code TUI.**

Two owner-reported defects, both real:

1. **No Alt shortcut reached the hosted program.** Windows reports an Alt combination as `Key.System` and puts the real key in `SystemKey`, and it never raises a text-input event for Alt, so `TerminalControl` saw nothing usable and dropped every one. Every Alt binding a TUI defines was dead. The control now resolves `SystemKey` and sends the traditional Escape-prefixed form: Escape plus the character for a printable key, Escape plus the ordinary byte for Enter/Backspace/Tab/Escape, and the existing modifier parameter for cursor and function keys (which must not be double-prefixed). The character is read from the user's **current keyboard layout** via `MapVirtualKeyW`, wrapped as `Filekin.Infrastructure.Windows.Input.KeyboardCharacters`, rather than assuming a US mapping. `Alt+F4` and `Alt+Space` are left to Windows, and a bare Alt press is swallowed so WPF does not enter menu mode over the terminal. Verified live: a `[Console]::ReadKey` probe in the tab reported `KEY=[M] CHAR=[109] MOD=[Alt]`.
   - The same pass removed the Escape prefix `OnTextInput` used to add, which would have corrupted **AltGr** (Control+Alt on many layouts) now that Alt is handled in the key handler.
2. **Scrolling was dead inside full-screen tools.** Claude Code enables mouse tracking (`?1000/1002/1003/1006`) and scrolls its own transcript from the wheel reports the terminal is supposed to send. Filekin sent none, and its own wheel only drives terminal scrollback, which an alternate screen does not have. `TerminalEmulator` now tracks the mouse modes (independently and cumulatively, so turning off the widest mode falls back to the next one still on) plus the SGR encoding flag, and a new platform-neutral `TerminalMouseReport.Encode` produces the wire form. `TerminalControl` forwards presses, releases, wheel and motion whenever a program has asked for the mouse; motion is throttled to one report per cell entered. Holding **Shift** overrides tracking so the terminal's own text selection stays reachable.

**Evidence captured during the fix (worth keeping):**

- **ConPTY forwards a mouse-mode request only after the client puts its input handle in virtual-terminal mode.** A probe that wrote `?1000h` *before* `setRawMode(true)` had the sequence silently swallowed by conhost — the emulator reported `tracking=None` — while the same probe with raw mode enabled first produced `tracking=ButtonEvent sgr=True`. This cost real debugging time; the first probe looked like a Filekin bug and was not one.
- Filekin's reports arrive at the program correctly encoded. A raw capture of the hosted program's stdin showed exactly `ESC[<64;74;16M ESC[<64;74;16M ESC[<65;74;16M ESC[<65;74;16M ESC[<0;74;16M ESC[<0;74;16m` for two wheel-ups, two wheel-downs, and a left press/release at column 74, row 16.
- End to end: with a 160-line transcript, wheeling inside Claude Code moved it from lines 148–159 to 136–152 and Claude showed its own "Jump to bottom (ctrl+End)" affordance — Claude, not Filekin, was doing the scrolling.
- `.NET`'s `Console.ReadKey` only surfaces key records and silently drops mouse input, so it cannot be used to probe mouse reporting. Use a raw-stdin reader (the node probe in the scratchpad) instead.

Files changed in this pass: `src/Filekin.Core/Terminal/Emulation/TerminalEmulator.cs`, `src/Filekin.Core/Terminal/Emulation/TerminalMouseReport.cs` (new), `src/Filekin.Infrastructure.Windows/Input/KeyboardCharacters.cs` (new), `src/Filekin.App/Controls/TerminalControl.cs`, `tests/Filekin.Core.Tests/Terminal/TerminalEmulatorTests.cs`, `DECISIONS.md`, `HANDOFF.md`.

**Terminal selection/copy, scrollbars, and tab shortcuts (2026-08-26, Claude Code) — Release-clean, 115/115 tests, formatting and `git diff --check` green, live-verified.**

Owner-requested follow-ups to the hosted terminal, all live-tested on the running Release build:

- **Terminal text selection and copy.** Dragging selects a range; the selection renders as a highlight and is part of the render run key, so a highlighted span breaks the draw run exactly at its edges. Selection is stored in **absolute line indices**, not viewport rows, so it stays over the same text while new output scrolls the screen and while the wheel moves through scrollback. It is dropped when the user types, when the buffer switches to or from the alternate screen, and when the tab changes.
- **Copy/paste keys.** `Ctrl+C` copies only when a selection exists and otherwise passes through as the interrupt byte; `Ctrl+Shift+C` always copies; `Ctrl+V`, `Ctrl+Shift+V`, and `Shift+Insert` paste. Verified live both ways: a five-line drag copied exactly those lines, and `Ctrl+C` with no selection interrupted a 120-second `Start-Sleep`. Recorded in `DECISIONS.md`.
- **`Ctrl+Tab` / `Ctrl+Shift+Tab`** cycle the workspaces in tab-strip order (Files first, then terminals, wrapping). **`Ctrl+Shift+T`** opens a terminal at the current Files folder; **`Ctrl+Shift+W`** closes the selected terminal with the same confirmation as its close button. These four are the only keys Filekin claims from a focused terminal — verified live that plain `Tab` still reaches PSReadLine and completes (`Get-Ch` → `Get-ChildItem`). `Ctrl+W` was rejected because PSReadLine binds it to `BackwardKillWord`; the reasoning is in `DECISIONS.md`.
- **Terminal scrollbar.** `TerminalControl` exposes `ScrollMaximum` / `ScrollValue` / `ViewportLines`, and the terminal template binds a slim `ScrollBar` beside it that collapses when there is no scrollback. Dragging the thumb and the mouse wheel drive the same offset.
- **Command output is selectable and scrolls.** The expandable output region was a `TextBlock`, so substantial command output could be read but never copied. It is now a read-only, borderless `TextBox` with its own `Auto` vertical scrollbar. Verified live: a drag-select plus `Ctrl+C` copied the exact span, and `Esc` still collapses the region and returns focus to the Files list.
- Core additions supporting the above: `TerminalSnapshot.FirstVisibleLine`, `TerminalEmulator.GetLines(startLine, startColumn, endLine, endColumn)` (end column exclusive, reversed drag coordinates normalized, trailing blanks trimmed), and monotonic absolute line indices — `TrimmedLines` advances when scrollback trims, on a full reset, and on `ESC[3J`, so a stale selection resolves to nothing instead of silently pointing at newer output. Two Core tests cover both.

Note for whoever picks this up: `src/Filekin.App/Controls/TerminalControl.cs` also carries a glyph-run rewrite that arrived in the working tree from another agent during this session. It replaces `DrawText` with explicit per-cell glyph advances so text stops drifting away from the cell grid (a shaped run advances by the font's own width, not the rounded cell width, and the error accumulates across a line). It was reviewed, kept, and built on rather than reverted; the selection work layers on top of it.

The Files list is deliberately **not** text-selectable — its rows are a filesystem selection, which is the app's model. Copying a file path to the clipboard would be a separate, unspecified feature; see the open question below.

Files changed in this pass: `src/Filekin.Core/Terminal/Emulation/TerminalEmulator.cs`, `src/Filekin.Core/Terminal/Emulation/TerminalSnapshot.cs`, `src/Filekin.App/Controls/TerminalControl.cs`, `src/Filekin.App/ViewModels/ShellViewModel.cs`, `src/Filekin.App/Views/MainWindow.xaml`, `src/Filekin.App/Views/MainWindow.xaml.cs`, `tests/Filekin.Core.Tests/Terminal/TerminalEmulatorTests.cs`, `DECISIONS.md`, `HANDOFF.md`.

**Hosted terminal review, live QA, and four defect fixes (2026-08-26, Claude Code) — Release-clean, 113/113 tests, formatting and `git diff --check` green, live-verified on the desktop with plain PowerShell and the real Claude Code TUI.**

Reviewed Codex's uncommitted hosted-terminal batch, drove the running Release app through Windows UI Automation plus `PrintWindow` capture, and fixed every defect found. The layering was kept exactly as Codex left it (raw bytes in `ITerminalSession`, VT state in the platform-neutral `TerminalEmulator`, drawing/input in `TerminalControl`, session/dispatcher state in `TerminalTabViewModel`, collection/selection in `ShellViewModel`, window focus/confirmation in `MainWindow`). No third-party terminal dependency was added and the cell renderer was not replaced.

Defects found and fixed:

1. **Private-parameter CSI sequences were executed as standard commands.** `TerminalEmulator.ExecuteCsi` stripped a leading `<`, `=`, `>` or `?` and then fell through to the shared final-byte handlers, so xterm's `CSI > 4 ; 2 m` (modifyOtherKeys, sent by Claude Code at startup) was applied as SGR 4 + SGR 2 — **every cell on screen rendered dim and underlined**, drawing a horizontal rule under all 30 rows of the TUI. A raw ConPTY capture proved the only genuine SGR in claude's stream was `ESC[93m` / `ESC[m` while the emulated screen reported `[Dim, Underline]` on every row. Prefixed sequences are now routed separately: `?`-prefixed `h`/`l` still reach DEC private-mode handling, and every other prefixed sequence (`>1u`, `<u`, `>0q`, `>4;2m`) is ignored. Two Core regression tests cover this.
2. **Concurrent unserialized writes to the ConPTY input pipe.** `TerminalControl` sends one keystroke per fire-and-forget `WriteAsync` without awaiting, and `ConPtyTerminalSession.WriteAsync` wrote straight to the input `FileStream`. Concurrent `FileStream` writes are undefined and can interleave or drop typed input. Writes are now serialized behind a `SemaphoreSlim` in the session — the layer that owns the pipe — with an integration test that fires one un-awaited write per character and asserts the line still arrives intact.
3. **Per-cell text layout made the renderer unusable under load.** `OnRender` built one `FormattedText` and several `SolidColorBrush` objects per cell, i.e. a full text layout for every character on screen every frame. Printing 2000 lines burned **4.31 s of CPU over a 5 s window**. `OnRender` now batches adjacent same-style cells into a single run (breaking runs at wide/continuation cells so double-width glyphs stay correct) and caches frozen brushes and pens. The identical measurement is now **0.69 s** — a 6.3× reduction.
4. **The `+` new-terminal button rendered as tofu.** It used `Content="+"` under the `IconActionButton` style, which sets `FontFamily="Segoe MDL2 Assets"`; that font has no `+` glyph, so the button drew an empty box. It now uses the MDL2 `Add` glyph `&#xE710;`, consistent with the other icon buttons.

Smaller robustness fixes in the same pass: `ShellViewModel` captures the UI `Dispatcher` at construction instead of calling `Dispatcher.CurrentDispatcher` when a tab is added; `MainWindow.FocusSelectedTerminal` posts at `DispatcherPriority.Loaded` so the first tab is focusable after the layout pass that realizes it; `TerminalControl` no longer maps Ctrl+letter when Alt is also down, so **AltGr** (Control+Alt on many layouts) produces its character instead of a control code; typing while scrolled back now repaints immediately; and the session's startup-replay buffer is capped at 1 MB and cleared on dispose so a session nobody renders cannot grow without bound.

**Evidence recorded from live capture (important for future terminal work):**

- **ConPTY does not forward alternate-screen mode for shell-emitted `ESC[?1049h`.** A raw capture of `Write-Host "$e[?1049h…"` showed conhost translating it into `ESC[2J` + `ESC[H` + a repaint on the *same* screen, and on exit it emitted only `ESC[4;1H` with **no repaint of the previous main-buffer content**. The pre-app screen is therefore not restored. This is conhost's behavior, not ours — our emulator's `?1049` handling is correct and unit-tested. A real TUI child (`claude`) *does* get `ESC[?1049h` forwarded, so both paths occur.
- **ConPTY passes 24-bit and 256-colour SGR through untouched** — captured `ESC[38;2;255;140;0m` and `ESC[38;5;208m` verbatim — and the terminal renders both correctly, along with RGB backgrounds.
- **`NO_COLOR` in the inherited environment silently disables colour end to end.** During QA the app was launched from a shell with `NO_COLOR=1`; the hosted PowerShell set `$PSStyle.OutputRendering = PlainText` and stripped ANSI from its own pipeline output, and the nested `claude` disabled colour entirely (grey mascot, no accents). Relaunched with a clean environment, the TUI renders its full palette. A hosted terminal inheriting the parent environment is correct behavior; this is only a trap when diagnosing "missing colours".
- Claude Code also requests the kitty keyboard protocol (`ESC[>1u`), focus reporting (`?1004`), synchronized output (`?2026`), and mouse tracking (`?1000/1002/1003/1006`). None are implemented; ignoring them is safe and the TUI falls back correctly.

Live QA performed on the running Release build (window captured with `PrintWindow`, driven with UI Automation and synthesized input):

- `+` starts PowerShell at the visible Files folder; the startup prompt is not lost.
- Typing, PSReadLine syntax colouring, `Up` history recall, `Ctrl+C`, `cls`, and `Ctrl+Shift+V` paste all work.
- Window resize propagates: `121x32` → `94x24` reported by the child, with correct reflow.
- The real Claude Code TUI renders correctly — orange mascot, orange/yellow accents, bold, box rules, and wide CJK glyphs.
- Child-tool exit (`/exit` in claude) returns to the same PowerShell prompt and leaves the tab open; root `exit` removes the tab and restores the Files workspace.
- Command-bar routing: `claude` typed in the Files command bar opens a new tab titled `Claude · mfloy`; `cd HKLM:\` opens a `PowerShell` tab at `HKLM:\` while Files stays at its filesystem location.
- Tab titles disambiguate (`PowerShell`, `PowerShell · mfloy`, `PowerShell · mfloy · 2`); closing a non-selected tab does not change selection.
- In-app confirmations only — no OS dialog — for closing a live tab and for closing the app; the app-close prompt is one consolidated message naming the session count, and Escape cancels it.
- Mouse-wheel scrollback works and hides the cursor while scrolled back.
- After the app closes, no orphaned `pwsh` or child processes remain.

Files changed in this pass: `src/Filekin.Core/Terminal/Emulation/TerminalEmulator.cs`, `src/Filekin.App/Controls/TerminalControl.cs`, `src/Filekin.App/ViewModels/ShellViewModel.cs`, `src/Filekin.App/Views/MainWindow.xaml`, `src/Filekin.App/Views/MainWindow.xaml.cs`, `src/Filekin.Infrastructure.Windows/Terminal/ConPtyTerminalSession.cs`, `tests/Filekin.Core.Tests/Terminal/TerminalEmulatorTests.cs`, `tests/Filekin.Infrastructure.Windows.Tests/Terminal/ConPtyTerminalSessionTests.cs`, `HANDOFF.md`.
**Hosted terminal renderer + real tab lifecycle (2026-08-26, Codex) — UNCOMMITTED, intentionally paused at the owner's request. Debug app build passes; focused Core tests pass; live QA/format/Release validation still required.**

- Added a platform-neutral streaming terminal emulator under `Filekin.Core.Terminal.Emulation`. It incrementally decodes split UTF-8 and split VT sequences into a cell grid; tracks cursor, delayed wrap, scrolling margins, primary/alternate buffers, normal-buffer scrollback, cursor visibility, application-cursor/application-keypad/bracketed-paste modes; handles common cursor/edit/erase/scroll/SGR (16/256/RGB) sequences; preserves wide/combining characters; and emits replies for cursor-position/device-attribute queries. `TerminalSnapshot` gives the renderer an immutable screen image.
- Added eight focused `TerminalEmulatorTests` covering control characters, split UTF-8/CSI, wide cells, SGR, cursor edit/erase, scrollback viewport, alternate-screen restore, terminal query replies/modes, and resize. All Core tests pass: **83/83**.
- Added `TerminalControl`, a custom WPF `FrameworkElement` that draws terminal cells/colors/styles and the focused cursor. It is not a plain transcript control. It maps text, Ctrl+A–Z, Ctrl+Space, Enter/Backspace/Tab/Escape, cursor/navigation keys with modifier/application-mode sequences, Insert/Delete/Page keys, and F1–F12; supports bracketed `Ctrl+Shift+V`/Shift+Insert paste; mouse-wheel scrollback; ConPTY resize; and a basic automation name/help/document peer. It does **not yet** implement terminal mouse-reporting, mouse text selection/copy, or a full accessibility text provider; decide/fill those based on v1 requirements after basic live QA.
- Added `TerminalTabViewModel`, which owns one `ITerminalSession` plus its emulator, marshals raw output to the WPF dispatcher, sends emulator query replies to ConPTY, forwards input/resize, and surfaces root-shell exit.
- Replaced the static Codex/Claude tab samples with the permanent Files tab, a bound collection of live terminal tabs, and a `+` action that starts PowerShell at the current Files folder. A terminal replaces the full Files workspace while selected. Duplicate titles are disambiguated with ` · 2`, etc. Known interactive commands and non-filesystem provider delegation now produce a `CommandExecutionOutcome` carrying a real ConPTY session; `ShellViewModel` owns selection/add/remove/disposal. Root PowerShell exit removes the tab; child-tool exit still returns to the same PowerShell prompt.
- Added in-app confirmation before ending a live terminal tab and one consolidated confirmation before closing Filekin with active terminals. While a terminal is selected, Files-only Y/N/Escape handling returns without intercepting terminal keys. Returning to Files calls the existing refresh-on-return boundary and does not modify the command draft.
- Hardened `ConPtyTerminalSession.OutputReceived` so output produced between process creation and the renderer's first subscription is queued and replayed instead of losing the startup prompt/frame. Added an integration test that deliberately subscribes after a one-second delay.
- Rechecked the current Microsoft platform contract before implementation: [CreatePseudoConsole](https://learn.microsoft.com/en-us/windows/console/createpseudoconsole) says the UTF-8 output stream interleaves text and VT sequences and makes the host responsible for presentation/input; [Console Virtual Terminal Sequences](https://learn.microsoft.com/en-us/windows/console/console-virtual-terminal-sequences) documents the supported output, input-mode, query-reply, and alternate-buffer sequences used here.

Files currently changed/added for this batch:

- Core (new): `src/Filekin.Core/Terminal/Emulation/TerminalCell.cs`, `TerminalSnapshot.cs`, `TerminalResponseEventArgs.cs`, `TerminalEmulator.cs`.
- App (new): `src/Filekin.App/Controls/TerminalControl.cs`, `src/Filekin.App/ViewModels/TerminalTabViewModel.cs`.
- App (changed): `CommandExecutionOutcome.cs`, `CommandExecutor.cs`, `ShellViewModel.cs`, `Views/MainWindow.xaml`, `Views/MainWindow.xaml.cs`.
- Windows infrastructure (changed): `Terminal/ConPtyTerminalSession.cs`.
- Tests: new `tests/Filekin.Core.Tests/Terminal/TerminalEmulatorTests.cs`; changed `tests/Filekin.Infrastructure.Windows.Tests/Terminal/ConPtyTerminalSessionTests.cs`.
- Documentation: `HANDOFF.md`.

Exact pause state / cautions:

- `dotnet build src/Filekin.App/Filekin.App.csproj --no-restore -m:1` passes with 0 warnings/errors. `-m:1` was needed because this desktop currently has many unrelated `dotnet` build-server processes and a parallel project-reference build twice ended with an unhelpful 0-error MSBuild failure; individual/serial builds work.
- `dotnet test tests/Filekin.Core.Tests/Filekin.Core.Tests.csproj --no-restore` passes **83/83**.
- `dotnet test Filekin.sln --no-restore -m:1` passed Core **83/83** and Windows infrastructure **25/27**. The new delayed-subscriber ConPTY test passed. The only failures were the two pre-existing real-Recycle-Bin tests (`RecycledFileAppearsInTheBinAndCanBeRestored`, `DeleteForeverRemovesOnlyTheTargetedItemFromTheBin`), both because the just-recycled fixture was not returned by shell enumeration. This run was not used to change or clean the user's bin; reproduce the established outside-sandbox/desktop condition before calling it a regression.
- `git diff --check` passes. Files written by this batch currently have LF in the worktree and Git warns they will become CRLF; run the repository's normal formatter/line-ending normalization before commit.
- No live WPF run has been done. Likely first resume work: launch the Debug/Release app, click `+`, type a distinctive PowerShell command, resize, switch Files↔terminal, type `exit`; then launch `claude` or `codex` from the Files command bar and exercise its alternate-screen/special-key behavior. Inspect close/app-close confirmations. Fix before expanding scope.
- Review the custom renderer carefully. It intentionally implements the documented/common VT subset, not every xterm extension. Mouse reporting, selection/copy, OSC hyperlinks/title changes, and full screen-reader text exposure are not implemented. OSC titles are deliberately ignored because confirmed Filekin titles describe launch context rather than tracking internal `cd`/shell title changes.

Intended continuation plan and programming boundaries (preserve this approach unless testing disproves it):

1. **Keep the existing layering.** Raw bytes remain owned by `ITerminalSession`/`ConPtyTerminalSession`; deterministic VT state remains in the platform-neutral `TerminalEmulator`; WPF drawing/input remains in `TerminalControl`; session/dispatcher/disposal state remains in `TerminalTabViewModel`; workspace collection/selection remains in `ShellViewModel`; window-only focus and confirmation behavior remains in `MainWindow`. Do not move VT parsing into code-behind or make Core depend on WPF.
2. **Review before broadening.** Read every current terminal diff and run the focused tests first. Correct bugs in the current cell-buffer/parser rather than replacing it with a plain `TextBox`, stripped-ANSI transcript, or WebView. Do not add a third-party terminal dependency without an explicit architectural/product decision.
3. **Prove plain-shell behavior first.** Start Filekin, use `+` to create PowerShell at the visible Files folder, confirm the initial prompt was not lost, type a marker command, use arrows/history/Ctrl+C/Escape, resize, wheel through scrollback, switch tabs, return to Files, and type `exit`. Expected: the terminal is independent after launch; root `exit` removes the tab; returning to Files invokes `RefreshWorkspaceAfterReturnAsync` while preserving selection, focus, scroll position, rich-view state, and an unentered command draft.
4. **Then prove routing.** Launch built-in interactive classifications from the Files command bar (`powershell` first, then an installed `claude` or `codex`) and verify a real terminal tab is selected with `Tool · Folder` naming. Run `cd HKLM:\` from Files and verify provider delegation opens a PowerShell terminal at that provider while Files stays at its filesystem location. Finite/verbose commands must continue using adaptive Files output; verbosity alone must not create a terminal tab.
5. **Exercise real VT/TUI behavior.** In Claude/Codex, specifically test alternate-buffer enter/leave, screen redraws, colors, wide Unicode, arrows/Home/End/Page keys, Enter/Backspace/Tab/Escape/Ctrl+C, paste (including bracketed paste), resize, child-tool exit returning to PowerShell, and subsequent commands in the same shell. Use failures as evidence to add only the missing VT/input sequences needed for correct behavior, with a focused Core test for every parser fix.
6. **Verify lifecycle and focus.** Test duplicate title suffixes, tab-to-tab switching, closing a non-selected and selected live tab, cancel/accept via buttons and Y/N/Escape, automatic root-exit removal, and one consolidated app-close confirmation for multiple sessions. Terminal input must bypass Files-only Escape/Y/N handling. Confirm no output events or session callbacks survive disposal and no tab closes merely because the interactive child exits.
7. **Accessibility/input follow-up.** Keyboard behavior is part of the current completion bar. The current basic automation peer is deliberately only a starting point; assess screen-reader output exposure plus mouse selection/copy and terminal mouse reporting after plain/TUI behavior works. Record a product/spec question if full behavior materially changes v1 scope instead of silently inventing it.
8. **Finish with repository hygiene.** Reproduce the two Recycle Bin integration failures under the established desktop/outside-sandbox condition; run Debug and Release build, all tests, `dotnet format Filekin.sln --verify-no-changes --no-restore`, and `git diff --check`; normalize changed files to CRLF; update this handoff with live evidence and remaining limitations; then commit the terminal batch only when relevant checks are green.

**Command/file focus consistency + Recycle Bin selectable rows (2026-08-26, Codex) — Release-clean, 101/101 tests passed outside the sandbox, live-verified through Windows UI automation, committed as `d1c9c0a`.**

- Fixed `Space` from the Files list by handling it during preview/tunneling, before WPF's `ListBoxItem` consumes Space for selection. `Ctrl+Space` remains available for selection semantics.
- Fixed Escape from the command bar by returning focus to the actual previously selected row container rather than the `ListBox` itself. The selected item, scroll position, and next Up/Down movement now remain deterministic. Escape from the command bar returns to whichever workspace surface is active; workspace-level Esc still dismisses the Recycle Bin rich view.
- Command recall is deterministic and preserves an unexecuted draft: Up recalls prior entries; Down past the newest entry restores the text the user was editing instead of always clearing it.
- Hid the complete filesystem path row while Recycle Bin is open: breadcrumb, hidden-folder item count, and external-terminal action no longer compete with the rich-view header or imply that they describe the bin. The click handler still guards hidden navigation defensively. The Recycle Bin header owns the total bin count, the status bar owns its selected count, and the command prompt quietly retains the preserved Files path/context.
- Completed the owner-requested Recycle Bin selection redesign: selectable rows with normal single/Shift/Ctrl multi-selection and highlight; one Restore / Delete forever action bar operates on the selection; Empty remains a separate whole-bin action; per-row buttons and their unused danger-icon style were removed. Bulk restore/delete refreshes the bin once after processing. Recycle action selection remains local and never changes filesystem `@selection`.
- Clarified Recycle Bin hover versus selection: keyboard navigation keys (arrows, Page Up/Down, Home/End) suppress the stationary-pointer hover until the mouse moves/clicks again, so paging cannot leave two selection-looking rows. The status bar now reports the visible rich-view count (`1 selected · Recycle Bin`, etc.) rather than the hidden Files selection count. Shift/Ctrl multi-selection still intentionally highlights every selected row.
- Confirmed one conventional extended-selection model across mouse and keyboard: unmodified navigation replaces selection, Shift navigation extends it, Ctrl navigation moves focus without changing the selected set, and Ctrl+Space toggles the focused item. Recycle rows now draw a thin focus outline independently of the filled selected-row highlight, making Ctrl-navigation and multi-selection unambiguous. The list exposes concise automation help for these modifiers.
- Kept the command bar enabled in Recycle Bin, consistent with the rich-view specification. PowerShell exposes the Windows-only `Clear-RecycleBin` cmdlet (but no built-in Get/Restore-RecycleBin); any completed command now refreshes the visible bin so `Clear-RecycleBin -Force` or other shell/COM manipulation cannot leave stale rows. Raw shell commands retain raw-shell safety semantics; Filekin's selection action bar remains the guided in-app restore/delete path.
- Added workspace refresh-on-return at the WPF window-activation boundary. Every return refreshes the preserved Files listing and the visible rich view (currently Recycle Bin); unchanged collections remain untouched, while changed collections restore every still-valid selection, the focused row, and scroll position. The command-bar draft is never assigned by refresh and remains intact. `RefreshWorkspaceAsync` is deliberately the same boundary future real Files-tab activation should call after the user returns from a terminal tab.
- Recorded the Recycle Bin local-action-selection exception in `DECISIONS.md` so it does not silently conflict with the general rich-view/filesystem-selection rule.
- Recorded the owner's future requirement for a durable user-configurable interactive-app registry. Exact authoring remains deferred until hosted terminal tabs are complete: hand-edited configuration, Settings UI, and an app command such as `/registerapptab` are candidates that may share one underlying config; no final surface or command name has been chosen.

Files changed: `DECISIONS.md`, `src/Filekin.App/Themes/Controls.xaml`, `src/Filekin.App/ViewModels/ShellViewModel.cs`, `src/Filekin.App/Views/MainWindow.xaml`, `src/Filekin.App/Views/MainWindow.xaml.cs`, and `HANDOFF.md`.

**Recycle Bin feature set + in-app confirmations (2026-08-26, Claude Code) — built, unit-tested (101/101), live-verified via UI Automation, and subsequently committed as part of `9d2b62e`.** A `/toss` deletes to the Recycle Bin (was `/delete`; renamed for app-uniqueness — PowerShell already has `rm`/`del`, but nothing that lands recoverably in the bin), and the bin is now a first-class, reachable surface:

- **`/recycle` opens a rich Recycle Bin view** over the Files area (name, original location, deleted date, size, per-row **Restore**). Also reachable from the **sidebar**: `/recycle` is a third `Surfaces` nav item alongside `/places` and `/drives`, same `/`-accent look (owner: "recycle bin is a type of place" — no trash icon, follow the existing surface style). Clicking it opens the view (`OnSurfaceSelected`).
- **Empty Recycle Bin** — a trash-glyph button in the view header, disabled when empty, via `SHEmptyRecycleBinW` (no confirmation/progress/sound flags; we do our own confirm).
- **Per-item permanent delete** — a compact trash icon per row (`DangerIconButton` style, red on hover) beside Restore. IMPORTANT: it does **not** use the shell "Delete" verb — that pops Windows' *own* OS confirm dialog. It deletes the bin's backing store directly (`entry.Path` = the `$R…` data file/folder, plus its `$I…` metadata sibling), so the delete is silent and stays in-app.
- **In-app "are you sure?" (owner requirement): never an OS dialog.** All `MessageBox` confirms were removed and replaced by an in-app strip below the command bar (`IsConfirming`/`ConfirmPrompt` + `RequestConfirmation`/`ConfirmYesAsync`/`CancelConfirmation`). Answer with **Y**/**N** keys (window-level `OnPreviewKeyDown`, works from any focus) or **Yes**/**No** buttons; Esc cancels. Applies to the two irreversible actions (Empty, per-item delete). The reversible `/toss` has **no** confirm (owner: not even for deleting outside the current folder — it's recoverable from the bin); the earlier outside-folder confirm and its `confirmOutsideTrash` plumbing were removed from `CommandExecutor`/`ShellViewModel`/`MainWindow`.
- **Window fit** — `MainWindow.FitToWorkArea()` clamps the startup size to `SystemParameters.WorkArea` so the bottom sidebar nav (`/places /drives /recycle`) and the Settings/About footer are never pushed off-screen on smaller displays (they only showed when maximized before). The bottom surfaces stay pinned; `@` Locations is the single scrollable region.
- **Test-flake fix** — `WindowsRecycleBinTests` is `[DoNotParallelize]`: the assembly runs method-level parallel, and two real-Recycle-Bin integration tests were racing on the one shared bin/COM.

New/changed files — Core: `FileSystem/{RecycledItem,IRecycleBin}.cs` (`IRecycleBin` = `List`/`Restore`/`DeleteForever`/`Empty`). Windows: `FileSystem/WindowsRecycleBin.cs` (shell-automation `List`/`Restore`, `$R`/`$I` `DeleteForever`, `SHEmptyRecycleBinW` `Empty`; `partial` for `LibraryImport`; STA thread for the shell COM). App: `ViewModels/{ByteSize,RecycledItemViewModel}.cs`, `ShellViewModel` (recycle-bin state + `OpenRecycleBinAsync`/`CloseRecycleBin`/`RestoreAsync`/`DeleteForeverAsync`/`EmptyRecycleBinAsync`/`HasRecycledItems`, confirm state + `Request*`/`ConfirmYesAsync`/`CancelConfirmation`), `CommandExecutor`/`CommandExecutionOutcome` (`/recycle` → `RecycleBin()` outcome; confirm plumbing removed); `Views/MainWindow.xaml`(.cs) (rich bin view, Empty/Restore/trash buttons, confirm strip, `OnSurfaceSelected`, `FitToWorkArea`, `OnEmptyRecycleBin`/`OnDeleteItem`/`OnConfirmYes`/`OnConfirmNo`, window-level Y/N/Esc); `Themes/Controls.xaml` (`DangerIconButton`). Tests: `tests/Filekin.Infrastructure.Windows.Tests/FileSystem/WindowsRecycleBinTests.cs` (Restore round-trip + `DeleteForever`; `[DoNotParallelize]`).

**Originally deferred for usage budget — the Recycle Bin selectable-rows redesign.** This was completed by Codex later on 2026-08-26; see the entry above.

Wired the Files command bar (HANDOFF "next seam" step 2) — **built, unit-tested, visually QA'd in the later Codex pass, and committed in `9d2b62e`**. The static command row is now a real terminal-style input: Enter runs the line, Up/Down recall history. Flow: `ReferenceResolver.ResolveLine` → `CommandClassifier` → app `/` command (`AppCommandDispatcher`) or finite PowerShell (`PowerShellRunspaceBackend`, created lazily and kept at the current Files folder). Output is adaptive (UX-DESIGN): small output shows inline, substantial output shows a compact `✓ Completed · N lines` / `✕ Failed` summary with a `View`/`Collapse` expandable region (Esc collapses); a `cd` re-navigates Files and a filesystem-changing command re-lists. Interactive tools and non-filesystem providers (`cd HKLM:\`) return an honest "coming with terminal support" notice rather than a faked/hidden session (that is step 3).

Added the **External Terminal Escape Hatch** (UX-DESIGN) as owner-decided "both command + button": a new Core `/ext` command (`Filekin.Core.Commands.App.External` — `IExternalLauncher`, `ExternalLauncherCommand`, `ExternalTerminalCommand`; Windows `WindowsExternalLauncher`). Bare `/ext` opens the user's external terminal at the current folder (prefers `wt -d`, falls back to pwsh/powershell); `/ext <program> [args]` launches that program externally at the folder (e.g. `/ext code`). A small command-prompt icon button in the path row does the bare-`/ext` action. Owner decisions this session: command named `/ext` (not `/terminal`, since the bar is already a terminal); `/ext` takes arguments; a `/reveal`/open-in-Explorer command was considered and **rejected** — Filekin replaces Explorer, so it must not send users back to it (use `/ext explorer` only if someone insists). Typing `powershell` stays the embedded-tab path (step 3), distinct from `/ext`.

New files — Core: `Commands/App/External/{IExternalLauncher,ExternalLauncherCommand,ExternalTerminalCommand}.cs`, plus `BuiltInAppCommands.CreateDispatcher(operations, launcher)` overload. Windows: `Commands/WindowsExternalLauncher.cs`. App: `ViewModels/{CommandExecutor,CommandExecutionOutcome}.cs`; `ShellViewModel` extended with command-bar state/history/execution and `OpenExternalTerminal`, now `IAsyncDisposable` (disposes the runspace on window Closed); `Themes/Controls.xaml` styles `CommandInputBox`/`IconActionButton`/`ResultGlyph`; `Views/MainWindow.xaml`(.cs) command-zone rework + `/ext` button + `OnCommandKeyDown`. Tests: `Commands/App/External/ExternalTerminalCommandTests.cs` (5).

Earlier the same day (2026-08-25) — fixed the Files listing showing the legacy user-profile junctions (Application Data, Cookies, Local Settings, My Documents, NetHood, PrintHood, Recent, SendTo, Start Menu, Templates) — which cannot be opened and are hidden from Explorer/terminal. `FileSystemDirectoryLister` now omits only protected OS items (`Hidden`+`System` "super-hidden") and keeps everything Explorer's hidden view shows, including plain-`Hidden` folders like `AppData`. No show-hidden toggle in v1. Recorded in DECISIONS.md; verified live (home listing dropped 64→47, `AppData` kept, all junctions gone) and against the real profile folder independently.

Earlier the same day — wired the Files hierarchy to the real filesystem (HANDOFF.md "next seam" step 1), preserving the validated `MainWindow` visual tokens/composition. New platform-neutral Core pieces under `Filekin.Core.FileSystem`: `DirectoryEntry`, `IDirectoryLister` + `FileSystemDirectoryLister` (one-level enumeration over ordinary .NET APIs, skips items it cannot stat), `FileTypeCode` (deterministic extension→terminal type-code map, not AI classification), and `FileListingSort` (directories always group first; the active column sorts within each group; re-sort reverses direction; case-insensitive ordinal name tie-break). New App view models: `ObservableObject` (hand-rolled `INotifyPropertyChanged`, no MVVM dependency added), `FileRowViewModel` (immutable display row), `PathSegmentViewModel` (clickable crumb), `FileLauncher` (GUI-open via Windows association), and a rebuilt `ShellViewModel` that owns the current location, listing, sort, and selection, enumerates off the UI thread, and exposes `BuildReferenceContext()` for the future command bar. `MainWindow` now has a live clickable path bar, keyboard-accessible sortable column headers (Buttons with `AutomationProperties.Name` + active-column caret), a virtualizing recycling Files list, double-click/Enter to open, Backspace to go up, selection→status count, and a real free-space status. Caption buttons also got accessible names. The command row, tabs, and sidebar Locations/Surfaces remain static preview.

Previous Codex entry:
Fixed the custom-chrome window's maximize-only taskbar overlap. `MainWindow` now handles `WM_GETMINMAXINFO` after its native source is initialized, and `MaximizedWindowBounds` sizes and positions the window to the nearest monitor's `MONITORINFO.rcWork` instead of the full monitor rectangle. This preserves Windows taskbar space on any edge and supports non-primary monitors whose virtual-screen coordinates may be negative. The existing maximized content inset remains in place for the invisible `WindowChrome` resize border.

Recovered and completed Claude Code's interrupted first WPF Files-shell design pass. Preserved Claude's uncommitted dark-theme tokens, custom control styles, static `ShellViewModel`, `MainWindow`, startup wiring, and the six new visual/interaction decisions in `DECISIONS.md`. Replaced fragile private-use glyph literals with ASCII C# `\uE922` / `\uE923` escapes (XAML glyphs use numeric XML references), repaired invalid XML comments, made sample status properties analyzer-compliant instance properties, separated Segoe MDL2 icon glyphs from normal Settings/About labels, and implemented the confirmed Esc-to-collapse output behavior with focus returning to `FilesList`. Normalized all changed files to the repository's CRLF policy.

Used Windows app-control visual QA on the running WPF build. Verified the collapsed and expanded command-output layouts, `View` → `Collapse`, Esc collapse plus Files-list focus restoration, Settings/About text rendering, and maximize/restore glyph swapping. The current shell is explicitly a static visual preview; no fake sample element is recorded as production behavior.

Three pieces this session. (1) Applied the owner-confirmed `main` branch governance as an active GitHub repository ruleset. (2) Implemented the production terminal-host boundary: platform-neutral terminal contracts in `Filekin.Core` (`ITerminalHost`, `ITerminalSession`, `TerminalSessionRequest`, size/output/exit types) and a ConPTY-backed implementation in `Filekin.Infrastructure.Windows` (`ConPtyTerminalHost`, `ConPtyTerminalSession`, `PowerShellExecutableLocator`, LibraryImport interop) — PowerShell is the root process; input, raw-VT output, resize, exit notification, and teardown sit behind the boundary; re-verified against the current Microsoft ConPTY documentation. (3) Implemented the `Filekin.Core.Commands` command router: a deterministic classifier + built-in interactive registry, and a router that dispatches app `/` commands, finite runspace commands, and known-interactive terminal launches, and consumes provider-delegation terminal launches. No terminal renderer or WPF surface was added.

Also surfaced a specification conflict about the terminal root process (shell-as-root vs. tool-as-root) — see the new entry under **Product Questions Requiring Owner Decision**.

Follow-up work later on 2026-08-25: reconciled the DECISIONS.md tool-as-root entries as superseded by shell-as-root; installed a machine-wide .NET SDK 10.0.400 so the plain `dotnet` command builds locally; and investigated a CI-only failure in the resize test (root cause and final resolution recorded under **Known Problems** — the resize test now asserts the boundary contract instead of the child's `RawUI`).

Later still on 2026-08-25: implemented the **`/` application-command dispatch** subsystem (`Filekin.Core.Commands.App` + the four core file-operation commands over a new `IFileSystemOperations` port, with `WindowsFileSystemOperations` providing System.IO copy/move and Recycle Bin delete via `SHFileOperationW`). Incorporated the owner's updated UX/decisions/guardrails specs and recorded the owner's **`@` disambiguation** decision (known command-bar references win over PowerShell splatting).

Finally on 2026-08-25: implemented the **`@` reference resolver** (`Filekin.Core.Commands.References` — `ReferenceResolver`/`IReferenceResolver`, `ReferenceContext`, `ReferenceResolution`, `INamedLocationResolver`) with light-touch line resolution that rewrites only recognized `@thisfolder`/`@selection`/named-location tokens (with optional `\subpath`) into PowerShell-quoted paths and passes native `@` syntax through untouched, plus `WindowsKnownFolderLocations` for the built-in known-folder references (`@desktop`, `@documents`, `@downloads` via `SHGetKnownFolderPath`, `@pictures`, `@music`, `@videos`, `@home`). Owner reconfirmed keeping `@` as the reference sigil.

On 2026-08-26 the owner reported the terminal caret sitting several columns past the last typed character inside a hosted Claude Code session, growing worse the further along the line the cursor was. Root cause: `TerminalControl` drew each styled run as one shaped `FormattedText`, so the font advanced the pen by its own advance width (8.203 px for Cascadia Mono at 14 px) while the grid, caret, and backgrounds used the ceiling-rounded cell width (9 px). The 0.797 px per character difference accumulated inside every run: measured **31.9 px of drift after 40 characters**, about four empty columns between the last glyph and the caret. `TerminalControl` now builds a `GlyphRun` per style run with **explicit per-cell advance widths**, so every grapheme is pinned to its own cell and drift is structurally impossible; combining marks get a zero advance on top of their base glyph, and a cluster the font cannot supply flushes the batch and falls back to a `FormattedText` drawn at the same cell origin. Cell width also changed from `Math.Ceiling` to nearest-integer rounding (9 px to 8 px here) so columns stay near the font's real metrics instead of being stretched, and the baseline now comes from the measured typeface instead of the run's own layout.

### Tests / Validation
- 2026-08-27 Codex `/go`: focused parser coverage passed **10/10**; Debug and Release solution builds
  passed with **0 warnings / 0 errors**. The CI-filtered Release suite passed **336/336** (193 Core,
  143 Windows); the full desktop Release suite passed **341/341** (193 Core, 148 Windows), including
  every ConPTY and real Recycle Bin integration test. Formatting verification and `git diff --check`
  passed. Live WPF QA used `/go C:\Program Files` and then `/go Common Files`; breadcrumbs, listing,
  item count, and command prompt all moved to `C:\Program Files\Common Files`. The app was closed;
  no QA fixture was created and no user file was changed.
- 2026-08-27 Codex archive-refresh/current-PATH correction: Debug and Release solution builds passed
  with **0 warnings / 0 errors**. Focused PATH/resolver tests passed **12/12**. The final CI-filtered
  Release suite passed **326/326** (183 Core, 143 Windows); the unfiltered desktop Release suite passed
  **331/331** (183 Core, 148 Windows), including all five interactive-shell tests and both real
  Recycle Bin tests. `dotnet format --verify-no-changes --no-restore` and `git diff --check` passed.
  Live WPF QA in a temporary `.qa-bugfix` folder showed `/zip` update Files from 1→2 visible items and
  `/unzip` update it from 2→3; raw `codex --version` and `/run codex --version` each opened a hosted
  terminal and printed `codex-cli 0.150.0`. Undo uses the same unconditional refresh helper in a
  `finally`; the destructive UI action was not invoked during this pass. Filekin was closed and the
  exact temporary QA tree was permanently removed afterward.
- 2026-08-27 Codex detachable-archive follow-up: Debug solution build passed with **0 warnings / 0
  errors**. Core tests passed **183/183** and focused ZIP infrastructure tests passed **21/21**. The
  full Release run passed Core **183/183** and Windows infrastructure **143/145**; the only failures
  were the two existing real-Recycle-Bin integration tests, which could not observe their newly
  recycled fixtures through the Windows shell in this environment and failed identically on a
  focused rerun. The final CI-filtered Release suite passed **323/323** (183 Core, 140 Windows), the
  Release build passed with 0 warnings / 0 errors, and formatting verification passed. Live WPF QA
  used a generated 12,000-file fixture: compression completed normally;
  extraction kept advancing after Esc; `/settings` opened while it ran; View reopened the same live
  operation; completion produced result-line Undo; `-y` started directly in the detachable task row;
  and a later harmless `Write-Output hello` result displayed without the stale archive Undo. The app
  was closed and the exact generated `.qa-archive-lifecycle` tree was deleted afterward.
- 2026-08-27 Codex archive completion: Debug and Release solution builds passed with **0 warnings /
  0 errors**. CI-filtered Debug tests passed **323/323** (183 Core, 140 Windows). The unfiltered
  desktop Release suite passed **328/328** (183 Core, 145 Windows), including the five
  interactive-shell tests. `dotnet format Filekin.sln --verify-no-changes --no-restore` and `git
  diff --check` passed. Live WPF QA opened `/zip README.md filekin-qa.zip`, verified the preview and
  root-folder toggle replanning, created the archive, verified the success row and accessible Undo
  action, and rendered the new Archives settings category with both defaults. The app was closed and
  only the generated QA archive was removed afterward.
- 2026-08-26 Codex `/run` + fallback WIP pause: Debug solution build passed with **0 warnings / 0 errors** after integration. Focused Core tests passed **120/120**; Windows infrastructure tests filtered with `TestCategory!=RequiresInteractiveShell` passed **86/86**. An earlier unfiltered Debug run reached the two known real-Recycle-Bin sandbox failures; they have not yet been rerun outside the sandbox for this change. `git diff --check` reported line-ending warnings only and no whitespace errors. **Not yet done:** Release build, final full desktop suite, formatting verification after final changes, docs reconciliation, or live WPF QA. `Get-Command snapmap-midi` confirmed the user's example resolves on PATH to `C:\Users\mfloy\AppData\Roaming\Python\Python313\Scripts\snapmap-midi.exe`; it was not launched during this pass.
- 2026-08-26 Claude Code caret-alignment fix: `Filekin.App` Release build passed with **0 warnings / 0 errors** (built to a scratch output path because the owner's running Filekin instance held the app's `bin` lock); full suite passed **113/113**; `dotnet format --verify-no-changes --no-restore` and `git diff --check` exited 0. Font metrics were measured rather than assumed with a throwaway WPF probe: Cascadia Mono at 14 px reports advance 8.2033, baseline 12.9867, height 16.27, and 40 drawn characters span 328.13 px against 360 px of ceiling-rounded cells. Live WPF QA of the new glyph path is still outstanding — it needs a Filekin restart, which the owner deferred because the running instance hosts the reporting session.
- 2026-08-26 Claude Code hosted-terminal review/fix pass: Release build passed with **0 warnings / 0 errors**; full suite passed **113/113** (85 `Filekin.Core.Tests` — the prior 83 plus 2 private-parameter CSI tests; 28 Windows infrastructure — the prior 27 plus the ordered-concurrent-write test). `dotnet format Filekin.sln --verify-no-changes --no-restore` and `git diff --check` both exited 0 after CRLF normalization. The two real-Recycle-Bin integration tests **passed** in this run outside the sandbox, so the earlier failures did not reproduce. Live QA is listed in full in the Work Completed entry above; measured render cost for 2000 scrolling lines dropped from **4.31 s to 0.69 s** of CPU over the same 5 s window.
- 2026-08-26 Codex hosted-terminal WIP pause: Debug App build passed with 0 warnings / 0 errors using serial MSBuild (`-m:1`). Focused Core suite passed **83/83** (including 8 new emulator tests). Serial full-suite run passed Core **83/83** and Windows infrastructure **25/27**; the new delayed-subscription ConPTY replay test passed, while the two existing real-Recycle-Bin round-trip tests could not find their just-recycled fixtures through shell enumeration. No live WPF QA, Release build, or formatting verification has been done for this uncommitted batch. `git diff --check` passes; CRLF normalization remains.
- 2026-08-26 Codex refresh-on-return pass: Release build passed with 0 warnings / 0 errors; full suite passed **101/101** (75 Core, 26 Windows infrastructure); formatting and `git diff --check` passed after CRLF normalization. Live WPF QA preserved a Files selection and an unexecuted command draft across minimize/reactivate. With one existing Recycle Bin row selected, a uniquely named workspace fixture was externally recycled while Filekin was inactive; refocus updated the header **3→4 items** and retained the existing selection/draft. After restoring and removing only that QA fixture, another refocus updated **4→3 items** and again retained selection/draft. No user Recycle Bin item was changed.
- 2026-08-26 Codex mixed-input/path-row pass: Release build passed outside the sandbox with 0 warnings / 0 errors; full suite passed **101/101** (75 Core, 26 Windows infrastructure); `dotnet format --verify-no-changes` and `git diff --check` passed. Live WPF QA against two existing bin rows confirmed the Files breadcrumb/item-count/external-terminal row is absent in Recycle Bin and returns on Esc; Ctrl+Down moved only the focus outline; Ctrl+Space produced two selected rows; plain Page Up collapsed to one; Shift+Page Down extended to two; Ctrl+Page Up preserved both while moving focus. No Restore, Delete forever, or Empty action was executed and no Recycle Bin contents changed.
- 2026-08-26 Codex Recycle Bin clarification pass: Release build passed with 0 warnings / 0 errors; full suite passed **101/101** after the final build; formatting passed. Live WPF QA used two temporary files created solely for the test: click selected the first row and reported `1 selected · Recycle Bin`; Down moved selection to the second row while the pointer remained over the first, and only the second retained selection styling; Shift+Up intentionally selected both and reported `2 selected · Recycle Bin`. Both QA files were restored and then removed, leaving the user's Recycle Bin as it was before the test.
- 2026-08-26 Codex focus/Recycle Bin redesign: Release build passed with 0 warnings / 0 errors. Full suite passed **101/101** (75 Core, 26 Windows infrastructure) when run outside the filesystem sandbox; the two real Recycle Bin tests cannot observe the same Windows shell namespace inside the restricted sandbox, where the other 99 tests passed. `dotnet format --verify-no-changes`, `git diff --check`, and CRLF normalization passed.
- 2026-08-26 Codex live WPF QA through Windows computer control: selected `.android`, pressed Space and observed command caret focus without changing selection; Esc returned to `.android`; the next Down selected exactly `.cache`. Executed harmless unknown slash command `/bogus`, typed an unexecuted `draft`, and verified Up restored `/bogus` while Down restored `draft`. Opened `/recycle`, verified breadcrumbs were disabled, selected one row and extended to two with Shift+Down, confirmed selection-level Restore/Delete actions enabled, and used Esc to restore `C:\Users\mfloy` with the underlying `.cache` selection unchanged. No Recycle Bin restore, permanent-delete, or empty action was executed during this QA.
- 2026-08-26 Recycle Bin + in-app confirms: Debug build 0 warnings / 0 errors; **101/101** tests passed (75 `Filekin.Core.Tests`; 26 Windows infrastructure — +1 `WindowsRecycleBin.DeleteForever`, alongside the existing Restore round-trip). Live UI-Automation verification: the sidebar `/recycle` opens the bin; the **Empty** button raises the *in-app* confirm strip ("Empty the Recycle Bin? N items deleted for good." + Y·Yes / N·No) with **no OS dialog**, and clicking **No** cancelled without touching the real bin. Per-item delete uses the silent `$R`/`$I` path (the shell "Delete" verb was rejected because it pops an OS confirm — observed live during a test run). NOTE: still not committed.
- 2026-08-26 command-bar wiring (step 2): Release build 0 warnings / 0 errors; **95/95** tests passed (71 `Filekin.Core.Tests`, +5 for the `/ext` external command; 24 Windows infrastructure); `dotnet format --verify-no-changes` and `git diff --check` clean. **NOT yet visually QA'd on the running app, and NOT committed** — see Recommended Next Step.
- 2026-08-25 Files-hierarchy wiring: Release build 0 warnings / 0 errors; **88/88** tests passed (64 `Filekin.Core.Tests` — the prior 50 plus 7 `FileTypeCode`, 5 `FileListingSort`, 2 `FileSystemDirectoryLister`; 24 Windows infrastructure); `dotnet format --verify-no-changes` and `git diff --check` clean.
- 2026-08-25 Files-hierarchy wiring: live Windows visual QA via `PrintWindow` capture of the running Release build. Confirmed the real home-folder listing (DIR codes, trailing `/`, real dates), colored clickable path segments, directories-first ordering, the active-column sort caret, item count, and real free-space status. Drove the MODIFIED header twice through UI Automation and confirmed the caret moved to MODIFIED, reversed to descending, and the rows reordered — proving the header-click → view-model → re-sort path end to end.
- 2026-08-25 maximize fix: full Release build passed with 0 warnings and 0 errors; all 74 tests passed (50 Core, 24 Windows infrastructure); formatting verification passed.
- 2026-08-25 maximize fix: live Windows visual QA confirmed the maximized window used the 1536×912 work area at origin 0,0, leaving the taskbar region outside the window. The status bar, file view, command result row, and expanded output remained fully visible in both collapsed and expanded-output states.
- 2026-08-25 WPF recovery: full Release `dotnet build Filekin.sln -c Release --no-restore --disable-build-servers` passed with 0 warnings and 0 errors.
- 2026-08-25 WPF recovery: full Release test suite passed 74/74 (50 Core, 24 Windows infrastructure).
- 2026-08-25 WPF recovery: `dotnet format Filekin.sln --verify-no-changes --no-restore` passed after CRLF normalization; `git diff --check` passed.
- 2026-08-25 WPF recovery: live Windows visual QA passed for normal/expanded output, View/Collapse, Esc collapse and focus restoration, Settings/About label rendering, and maximize/restore glyph switching.
- Production Release build (`Filekin.sln`): passed, 0 warnings, 0 errors.
- Production tests: passed, 74/74 (50 `Filekin.Core.Tests` — 2 product-identity, 10 classifier, 4 router, 6 app-command parser, 4 app-command dispatcher, 11 file-operation commands, 13 reference resolver; 24 `Filekin.Infrastructure.Windows.Tests` — 5 runspace integration, 5 ConPTY terminal-host integration, 5 Windows filesystem operations, 9 Windows known-folder locations).
- Production `dotnet format Filekin.sln --verify-no-changes --no-restore`: passed (exit 0).
- Terminal-host coverage: executable resolution, input→output round-trip through ConPTY, `Resize` accepted by the live pseudoconsole with the session still usable afterward (child `RawUI` reflection is environment-dependent — see the ConPTY resize note under **Known Problems**), one-shot startup command runs with the `-NoExit` prompt remaining, and `exit` ends the root process while raising `Exited`.
- App-command coverage (`Filekin.Core.Tests`, in-memory fakes): parser tokenization (single/double quotes, empty quotes, bare `/`, case-folding); dispatcher unknown-command / bare-`/` / duplicate-registration; and the four file-operation commands — relative-path resolution against the current location, copy-into-directory naming, no-overwrite refusal, missing-source/target errors, argument cardinality, rename rejecting a path as the new name, delete routing to Recycle, and refusal on a non-filesystem location. Windows filesystem-operations coverage (real temp dir): `GetKind`, file copy, recursive directory copy, move, and Recycle Bin delete removing the file from its path.
- Router coverage (in-memory fakes, no real PowerShell/ConPTY): `/` → app command (nothing executed), finite command → runspace execution, known-interactive command → terminal start with launch command/location/title and no runspace execution, and provider-delegation finite result → terminal started for the delegated launch. Classifier coverage: `/` app command, ordinary finite, empty input, always-interactive tools, argument-sensitive `python` vs `python script.py`, and path/extension normalization.
- Branch ruleset verified via the GitHub API: PR review count 1, code-owner review false, unattributed-changes extra approval false, required check `Build, test, and format (Windows)` bound to the GitHub Actions app, deletion/non-fast-forward blocked, owner admin bypass present.

## Specified but Not Implemented — full audit, 2026-08-26

Prompted by the owner asking what happened to autocomplete, which had been specified in four
documents and never built or tracked. This is the sweep that should have happened before any
"complete" claim. **Every entry below is Confirmed in `FEATURES.md` unless marked otherwise.**
Verified against code, not memory: the only app commands that exist are `/copy`, `/move`, `/rename`,
`/toss`, `/ext`, `/location`, plus the `/recycle`, `/places`, `/drives`, `/settings` rich views
(`BuiltInAppCommands.cs` and the rich-view branch in `CommandExecutor.ExecuteAsync`).

### Confirmed commands with no implementation at all

| Command | Confirmed in | Notes |
| --- | --- | --- |
| `/where` | FEATURES "Utilities", its own section | Locate an application/tool and related resources. |
| `/history` | FEATURES `/history`, "Rolling 50-Operation History" | Needs the durable operation history below. |
| `/undo` | FEATURES `/undo`, "Narrow Undo Scope", "Safe Undo Collision Handling" | Scope is move/rename plus reliable Windows delete/restore. Copy not guaranteed. |
| `/tidy` | FEATURES "Utilities", "`/tidy` Integration", "Fast Tidy Execution" | Native engine, no confirmation step, rich result afterwards. Legacy Desktop-icon behaviour explicitly excluded. |
| `/find` | referenced across the specs | Never given its own confirmed section; scope is not actually settled. Treat as unspecified rather than pending. |

`/delete` still appears in FEATURES "Core File Operation Commands", but `DECISIONS.md` (2026-08-26)
settled the recoverable-delete verb as **`/toss`**, which is what shipped. FEATURES is stale there.

Deliberately **not** version one, and correctly absent: `/recent`, `/disk`, `/interactive`.

### Confirmed subsystems with no implementation

- **Durable operation history.** `ARCHITECTURE.md` specifies a small embedded **SQLite** `state.db` beside `settings.json` for history and undo metadata, with automatic rolling 50-operation retention. There is no SQLite package reference in any project and no `state.db`. **The seam now exists**: `Filekin.Core.Operations.IOperationJournal`, with `InMemoryOperationJournal` (session-scoped, rolling 50) added for `/unzip`. Entries are plain data with a JSON payload precisely so the SQLite implementation can drop in behind the same interface without changing callers. `/history` and `/undo` sit on top of this, and `/copy`, `/move`, `/rename`, and `/toss` should start recording into it when it becomes durable.
- **Per-tab Files navigation history.** Each Files tab is meant to keep its own Back/Forward location history, with rich views excluded (Back/Esc dismisses a rich view, Forward never restores it, Up stays parent-only). Nothing implements it. `ShellViewModel._history` is **command-bar recall**, a different feature that is implemented.
- **File context menu.** The confirmed compact menu is Open / Rename / Copy / Cut / Copy Path / Delete / Properties. The only `ContextMenu` in the app is on sidebar Locations. This also covers the "copy a file path" gap already recorded as an open question.
- **Complex-operation preview**, **partial-success batch operations**, **file collision handling**, **privilege handling (UAC elevation)**, and **locked/read-only file handling**. All confirmed under Safety and Recovery; none exist. `IFileSystemOperations` performs single operations and throws on failure.
- **Intelligent task delegation** — long copy/move/unzip/tidy work moving to a dedicated task tab with progress and accumulated conflicts. The Workspace Surface System names task tabs as a third surface family; only rich views and terminal tabs exist.
- **Virtual Files locations** — representing non-folder locations in the Files workspace while distinguishing them from real paths.
- **AI-assisted filesystem interpretation.** Confirmed as a capability under Intelligence, with the interface explicitly undecided. Nothing exists, which is correct — do not invent the interface.

### Confirmed-but-partial

- **Terminal panes (split).** Confirmed under Terminal Workspace. Tabs exist; panes do not.
- **Preferred external terminal.** `/ext` launches a terminal; there is no *preference* for which one.
- **Contextual session names.** Titles are `Tool · folder`. The spec's intent (`CODEX · MyApp`, project-aware) is only partly met.
- **Folder sizes / direct size visibility.** Confirmed under Filesystem. The listing shows `—` for directories; no folder sizing exists. `/info` was to carry this.

### Doc drift found during the sweep

- `FEATURES.md` "`/interactive` — Not Version One" still says "Version one does not store user-defined interactive routing rules." The owner reversed this on 2026-08-26 and Settings now stores them; `DECISIONS.md` records the supersession. FEATURES has not been updated to match.
- `FEATURES.md` still lists `/delete` where `/toss` shipped (above).

Both are the owner's documents to change; they are recorded here rather than edited unilaterally.

### Known Problems
- **Archive Undo is still session-scoped and intentionally not durable.** Durable `/history` and
  `/undo` require the specified SQLite journal and remain the recommended next feature slice.
- **One multi-archive `/unzip` invocation currently records one journal entry per archive.** The
  result-line Undo therefore reverses the latest recorded archive, not necessarily the whole typed
  invocation. Durable history should define and implement a batch envelope so one user action has one
  history/Undo identity.
- **Archive Undo does not yet detect user edits made after extraction/compression.** It knows which
  paths Filekin wrote, but it does not compare identity/hash/timestamps before removing them. The
  durable Undo design must refuse or surface a conflict instead of deleting a user-modified output.
- **Detachment lasts while Filekin remains open.** Closing the whole application ends the process and
  therefore the in-process archive worker; cross-launch job persistence/resume is not specified.
- Every Files surface is now real: the listing, path bar, sorting, navigation, selection, command bar, `/recycle`, `/places`, `/drives`, hosted terminal tabs, and the settings-backed Location lifecycle. Nothing in the sidebar is a static preview any more.
- **Places and Drives rows have no App-level unit tests**, for the same reason `SelectAdjacentWorkspace` has none: there is no test project for `Filekin.App`. `DriveItemViewModel.SpaceText`/`UsageFraction` and `PlaceItemViewModel.Symbol` are covered by live QA only. If an App test project ever appears, these are good early candidates alongside `SelectAdjacentWorkspace`.
- The Places/Drives hover highlight is **not** gated on the `Tag` flag the Files and Recycle Bin lists use, so a stationary pointer keeps showing a hover row after keyboard paging. It is distinguishable from the keyboard row (which also draws the accent focus outline) and these are single-select navigation lists, so the Recycle Bin's multi-select ambiguity does not apply. Left as-is deliberately.
- **`/drives` updates live only for volumes**, which is everything that gets a drive letter: USB storage, memory cards, media inserted into an existing optical drive, and mapped/unmapped drive letters. A device that never receives a drive letter — **a phone connected over MTP** is the realistic case — is not a volume, never appears in `DriveInfo.GetDrives()`, and so cannot appear in `/drives` at all, live refresh or not. That is a `/drives` scope limit, not a refresh bug.
- A **network mapping that reconnects on its own** (rather than being mapped now) may not broadcast a volume arrival. Window re-activation still catches it.
- `ShellViewModel.SelectAdjacentWorkspace` (the Ctrl+Tab cycling order) has no unit test, because there is no test project for `Filekin.App` and adding one for a small index calculation over a WPF `ObservableCollection` was not worth the structural change. It is verified by live QA instead. If an App test project ever appears, this is a good first candidate.
- **Full screen-reader text exposure is not implemented.** `TerminalControl` has only a basic automation peer (`Document` control type with a name and help text); the cell grid is not exposed as text to assistive technology.
- Terminal mouse reporting is implemented for presses, releases, wheel and motion. Not implemented: the focus-reporting (`?1004`), synchronized-output (`?2026`) and kitty-keyboard (`ESC[>1u`) modes that Claude Code also requests. Ignoring them is safe and those tools fall back correctly.
- Terminal selection is drag-only: there is no double-click word select, triple-click line select, `Ctrl+A` select-all, or shift-click extend. `Ctrl+A` is deliberately left to the shell, where PSReadLine binds it to `SelectAll` for the current line.
- **Leaving a full-screen TUI does not restore the previous screen.** This is ConPTY/conhost behavior, reproduced from a raw capture (see the Work Completed entry). Nothing in Filekin can restore content conhost never re-sends.
- **A hosted terminal inherits Filekin's environment**, which is correct, but means `NO_COLOR`, `TERM`, and similar variables from however Filekin was launched flow into the shell and its children. This caused a false "colours are broken" reading during QA.
- The terminal renderer implements the documented/common VT subset, not every xterm extension. OSC window-title and hyperlink commands are deliberately ignored, because confirmed Filekin tab titles describe launch context rather than tracking shell title changes.
- The Files list and sidebar expose raw view-model `ToString()` output as their automation names (`Filekin.App.ViewModels.FileRowViewModel`, `NavItem { Symbol = /, … }`). This predates the terminal work but is a real accessibility defect worth a focused pass.
- Selection is not preserved across a re-sort (the listing is rebuilt); navigation clears selection by design. Preserving selection across a header re-sort is a minor refinement if wanted.
- `FileLauncher.Open` swallows launch failures (no association / shell refusal) silently to avoid crashing the shell; a user-visible error path belongs with the command-execution work, not the listing.
- **The two real-Recycle-Bin integration tests do not run on CI, by design.** `WindowsRecycleBin` reads the bin through `Shell.Application`, and on a GitHub-hosted runner a recycled file never reaches the bin at all, so the round trip cannot be verified there. They failed on their first CI run (`33008547374`) for that reason, not a code defect. They now carry `[TestCategory("RequiresInteractiveShell")]` and the CI workflow runs `--filter "TestCategory!=RequiresInteractiveShell"`. **Real coverage comes only from desktop runs**, so run the unfiltered suite locally before trusting a green CI: desktop is 117/117, CI is 115. An earlier attempt to infer the capability at runtime (skip when the bin lists empty) was wrong and is not in the code — the runner's bin does enumerate, it simply never receives the file. Do not weaken these assertions to make CI pass.
- **About is still a label with nothing behind it.** Settings is now a real surface; About was not in scope and has no owner-specified content.
- **The Settings option rows have no App-level unit tests**, for the same reason Places and Drives do not: there is no test project for `Filekin.App`. `SettingsOptionViewModel.Marker`, the accent swatch, and the category panel switching are covered by the offscreen harness described in the Live QA Notes, not by tests.
- **Theme and accent are not covered by automated tests either** — `ThemeManager` needs a live `Application`, so both are verified by rendered captures. The parts that can be tested headlessly (settings normalisation, the startup resolver, the interactive registry, the WM_SETTINGCHANGE filter) are.
- **`Follow system` reports dark when the Windows preference cannot be read at all** (a locked-down or missing `Personalize` key). That matches Filekin's own default, so it is indistinguishable from having no preference, but it is a fallback rather than a true reading.
- **A theme swap rebuilds every brush.** It is visually instant on this machine, but it is a whole-dictionary replacement, not an animated transition; a very large Files listing has not been measured under a swap.
- `ConPtyTerminalSession` builds the root command line as `"<pwsh>" -NoLogo -NoExit -Command "Set-Location …; <CommandText>"`. The startup `CommandText` is appended verbatim; commands containing embedded double quotes are out of scope for v1 (known interactive tools are simple tokens). A dedicated argument/quoting model is future work.
- Auto-launching the interactive tool via `-Command` differs slightly from the spike, which launched the child by typing it at the prompt after a readiness marker. The `-Command` path is validated for PowerShell and a benign startup command; it should still be exercised against a real TUI (claude/codex) once a terminal surface exists.
- The `ITerminalSession` boundary emits raw VT/ANSI bytes by design; the cell renderer, keyboard protocol, scrollback, selection and mouse reporting all sit above it. Only assistive-text exposure is still absent.
- The command classifier tokenizes with a plain whitespace split (matching the spike). It is not quote-aware, so an executable path containing spaces is not parsed as a single token for classification. The raw input is still what the shell/terminal executes; only the interactive-vs-finite decision uses the naive split.
- `InteractiveCommandRegistry` ships the same minimal built-in set (claude, codex, pwsh, powershell, cmd, ssh; `python`/`python3` interactive only with no args) and now also accepts user rules from Settings. Broadening the **built-in** list is still deliberately deferred; a user who needs `vim` adds it themselves.
- `CommandRouter` builds a basic `tool · folder` tab title. Final title/casing/rename behavior is a UI-layer concern and is not settled.
- The finite shell result contract still captures success/error streams as completed string collections; streaming output, other PowerShell streams, native exit status, and result presentation remain unimplemented.
- `Microsoft.PowerShell.SDK` brings a substantial runtime dependency graph; publishing/trimming/self-contained packaging behavior still needs production validation.
- 2026-08-25 — **ConPTY resize propagation is environment-dependent.** Hard evidence from a diagnostic build on the GitHub-hosted CI runner: after `session.Resize(120×40)` and polling `RawUI` for ~10s, the hosted PowerShell reported `win=80x24;buf=80x24` — the child's window/buffer size did **not** change, even though the native `ResizePseudoConsole` call **succeeded** (`Resize` did not throw; the test reached its assertion). On an interactive desktop the child does observe the resize (width→120 within ~1s). Root cause is the headless runner's ConPTY/console host not delivering the size change to pwsh's `RawUI`, not our Coord mapping (verified correct: `X=Columns, Y=Rows`). Because child-`RawUI` observation cannot be asserted reliably across environments, the earlier width-polling assertion was wrong to require it; `ResizeIsAcceptedAndTheSessionStaysUsable` now asserts only the boundary contract this type owns — the resize is accepted by the live pseudoconsole and the session keeps working afterward. End-to-end resize was already validated on a real desktop by the spike (criterion F). If a production feature ever needs guaranteed child-visible resize, investigate the headless-runner ConPTY delivery (candidate: conhost/OpenConsole under a non-interactive session) rather than re-adding a flaky `RawUI` assertion. (Superseded the earlier "RESOLVED via width polling" note, which passed locally but still failed on CI.)

### Recommended Next Step

**The archive commands are complete. The next feature should be durable `/history` + `/undo`, starting
with the owner-visible safety and grouping contract rather than a SQLite schema in isolation.**

0. **Settle history/Undo semantics with the owner.** Define one entry per typed invocation (including
   multi-archive extraction), modified-output conflict behavior, what `/undo` targets, and how the
   rolling 50-operation view communicates partial/non-undoable operations. Then implement the SQLite
   `IOperationJournal` store and place `/history` and `/undo` over that contract.
1. **When adding a preference, use the category matching the built subject.** The Archives category
   is the fifth category because archive behavior is now real. Operation history, updates, and the
   default-shell preference still have no empty Settings shells.
2. **Accessibility pass**, which is now the largest known quality gap and spans two things: the terminal cell grid is not exposed as text to a screen reader, and the Files list and sidebar expose raw view-model `ToString()` output as automation names. Both are listed under **Known Problems**. The second is cheap and worth doing regardless.
3. **Before touching the terminal again**, read the **Live QA Notes for the WPF App** section. Three separate "bugs" in this project turned out to be conhost behaviour or a faulty probe, and each cost significant time to disprove. Capture the raw ConPTY stream before changing product code.
4. **Keep the terminal layering intact**: raw bytes in `ITerminalSession`, deterministic VT state in the platform-neutral `TerminalEmulator`, drawing and input in `TerminalControl`, session/dispatcher state in `TerminalTabViewModel`, collection and selection in `ShellViewModel`, window focus and confirmation in `MainWindow`. Every parser fix gets a focused Core test. `Filekin.Core` must not reference WPF.
5. **Respect the keyboard contract** recorded under **Immediate Next Task**. Filekin claims exactly four combinations from a focused terminal. Adding a fifth needs an owner decision, because every key taken is a key some hosted tool loses.

Deliberately **not** done, and why:

- **Copying a file path from the Files list** — the owner observed that text selection was missing across the app. The terminal and command output were fixed; the Files list is a *filesystem* selection by design, so a "copy path" command or shortcut is a separate, unspecified feature. Recorded as an open question rather than invented.
- **A committed UI-automation QA harness** — genuinely useful, but developer tooling the owner has not asked for. See the Live QA Notes for what to rebuild if wanted.
- **Focus reporting (`?1004`), synchronized output (`?2026`), and the kitty keyboard protocol (`ESC[>1u`)** — Claude Code requests all three. Ignoring them is safe and it falls back correctly, so they were left alone rather than speculatively implemented.

Other backlog: batch `@selection` into `/copy`/`/move`/`/toss`; restore/delete verb localization (the shell "Restore" verb match is English-only).

### Sources consulted for the Settings work

- `WM_SETTINGCHANGE` and the `ImmersiveColorSet` area name — the only broadcast Windows sends when the light/dark app mode changes; there is no dedicated theme message.
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme` — the value the Windows Settings app writes for "Choose your default app mode".
- WPF pack URIs — `pack://application:,,,/<AssemblyShortName>;component/<path>` is the only form that resolves independently of the entry assembly. Filekin's assembly short name is `Filekin`.
- `Microsoft.Win32.OpenFolderDialog` — the folder picker added to WPF in .NET 8; no shell interop needed for "Choose folder…".
- A `ResourceDictionary` without `x:Class` cannot carry event handlers; a bubbled `Button.Click` on the owning items control is the supported alternative.

## Live QA Notes for the WPF App

Most of the terminal defects in this project were invisible to unit tests and only showed up by
driving the running app. What follows is what actually worked, and the traps that cost real time.

**Driving the app.** Start the Release build, put the window in the foreground, send input with
`System.Windows.Forms.SendKeys` plus `mouse_event`, and capture the window with `PrintWindow`
(flag 2) into a PNG. UI Automation (`System.Windows.Automation`) finds and invokes named controls -
this is why `AutomationProperties.Name` on buttons is worth keeping accurate. Call
`SetProcessDPIAware()` in the driving process first, or `GetWindowRect` returns virtualised
coordinates and the capture comes out cropped.

**Never send input without confirming the foreground window.** `SetForegroundWindow` can be refused
by Windows, and the keystrokes then land in whatever window *does* hold the foreground. During this
work that meant a `Ctrl+Shift+T` and a pasted command went into a second Filekin instance the owner
had open. Any harness must check `GetForegroundWindow() == targetHwnd` **after** trying to focus, and
refuse to send input otherwise. Also check for more than one running instance before starting.

**When input cannot reach the app at all, render offscreen instead of skipping verification.** On
2026-08-26 the Settings work could not be driven at all: `SetForegroundWindow` was refused every
time, and synthetic `mouse_event` clicks never arrived either — a `PrintWindow` capture after each
click showed the UI unchanged, while the process was confirmed alive and `Responding = True`. The
answer was a throwaway WPF console project in the scratchpad with a `ProjectReference` to
`src/Filekin.App/Filekin.App.csproj`:

```text
new Filekin.App.App() + app.InitializeComponent()   loads the merged resource dictionaries
new MainWindow(); window.Show()                     real window, real styles, real view model
(ShellViewModel)window.DataContext                  DataContext is public - drive the real VM
Dispatcher.PushFrame with a DispatcherTimer         pump instead of Application.Run
RenderTargetBitmap over (FrameworkElement)Content   capture without needing the foreground
```

This exercises real XAML, real styles, real bindings, and real view-model code — it is not a mock —
and it caught a genuine defect a running-app test would have missed, because the harness is *not*
the entry assembly and so tripped the relative-pack-URI bug described in **Work Completed**. Pump the
dispatcher after `Show()` before capturing, or `ActualWidth` is still zero. Delete the harness after
the run; it is not product code.

**Back up `%AppData%\Filekin\settings.json` before any QA that changes preferences**, and restore it
afterwards. The harness writes to the user's real settings file, because that is the path the product
uses.

**A running app locks the build output.** `Filekin.exe` holds `Filekin.Core.dll` and
`Filekin.Infrastructure.Windows.dll`, so a build fails with MSB3027 while it is open. Close the app
before building, and confirm which instance is yours before killing anything.

**Probing what the shell receives.** `[Console]::ReadKey` inside the tab is fine for keyboard checks
(it reported `KEY=[M] CHAR=[109] MOD=[Alt]` for the Alt fix) but it **only surfaces key records and
silently drops mouse input**, so it cannot be used to test mouse reporting. Use a raw-stdin reader
instead - a small node script with `process.stdin.setRawMode(true)` that appends to a file is the
most reliable probe, because reading a file back is unambiguous while reading a screenshot is not.

**ConPTY forwards a mouse-mode request only after the client enables raw/VT input.** A probe that
wrote `ESC[?1000h` *before* `setRawMode(true)` had the request swallowed by conhost and the emulator
reported `tracking=None`; the same probe with raw mode first reported `tracking=ButtonEvent`. The
first result looks exactly like a Filekin bug and is not one. When a mode appears to be ignored,
capture the raw ConPTY stream before changing any product code.

**A mapped codepoint is not a correct glyph.** The Places rows shipped a page icon because `E8B7` was assumed to be "Folder". `GlyphTypeface.CharacterToGlyphMap.ContainsKey(0xE8B7)` returns `true`, so a coverage check confirms nothing — the codepoint is mapped, just to the wrong picture. Render the candidates to a bitmap with `FormattedText` in `Segoe MDL2 Assets` and look at them. `ED25` is the folder; `E8B7` is a page; `E753` is the cloud; `EDA2` is the drive.

**Capturing the raw ConPTY stream** is the fastest way to settle "is this us or conhost". A
throwaway MSTest in `Filekin.Infrastructure.Windows.Tests` that starts a `ConPtyTerminalHost`
session, subscribes to `OutputReceived`, writes a command, and dumps the bytes with `ESC` replaced by
a visible marker answered three separate questions in this project (alternate-screen restore,
truecolour passthrough, mouse-mode forwarding). Delete it before committing.

**Colour looks broken when the environment says so.** `NO_COLOR` in Filekin's own environment is
inherited by the hosted shell and its children: PowerShell flips `$PSStyle.OutputRendering` to
`PlainText` and strips ANSI from its own output, and node-based tools disable colour entirely. Launch
Filekin from a clean environment before concluding anything about colour.

The harness used here was throwaway PowerShell in the agent scratchpad and is **not** in the
repository. Committing a maintained version would be a reasonable future addition, but it is
developer tooling that has not been requested, so it was not added unilaterally.

## Evidence / Documentation Sources

Record authoritative sources here when they materially affect implementation or architectural conclusions.

- [ProcessStartInfo.UseShellExecute](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.useshellexecute?view=net-10.0) — Windows shell execution can launch registered documents as well as executables; this supports `/run` using normal file associations for documents/shortcuts while hosted-terminal targets take the ConPTY path.
- [System.Reflection.PortableExecutable.Subsystem](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.portableexecutable.subsystem?view=net-10.0) — the PE subsystem values distinguish `WindowsGui` (2) from `WindowsCui` (3), used by the WIP resolver to route concrete console images to hosted terminals without executing them first.

- [SYSLIB1051 — source-generated P/Invoke unsupported types](https://learn.microsoft.com/en-us/dotnet/fundamentals/syslib-diagnostics/syslib1051) and [Source generation for P/Invokes](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation) — `LibraryImport` cannot marshal `StringBuilder`; a pinned `Span<char>` is the supported output-buffer shape. `LibraryImport` also does **not** append the `A`/`W` entry-point suffix that `DllImport` + `CharSet` does, so the exact export name must be spelled out. Verified against shlwapi's export table that the export is `SHLoadIndirectString`, with no `W`.
- [SHLoadIndirectString](https://learn.microsoft.com/en-us/windows/win32/api/shlwapi/nf-shlwapi-shloadindirectstring) — resolves the `@dll,-id` indirect strings that sync-root registrations use for `DisplayNameResource`, giving each cloud provider its own localized name instead of a hardcoded vendor list.
- [WM_DEVICECHANGE](https://learn.microsoft.com/en-us/windows/win32/devio/wm-devicechange), [DBT_DEVICEARRIVAL](https://learn.microsoft.com/en-us/windows/win32/devio/dbt-devicearrival), and [DEV_BROADCAST_HDR](https://learn.microsoft.com/en-us/windows/win32/api/dbt/ns-dbt-dev_broadcast_hdr) — official documentation that **volume** notifications are broadcast to every top-level window with no `RegisterDeviceNotification` call, and the header layout `/drives` reads the device type from. Device *interface* notifications would require registration; drive letters do not.
- [DriveInfo.IsReady](https://learn.microsoft.com/en-us/dotnet/api/system.io.driveinfo.isready?view=net-10.0) — documents that querying a drive that is not ready throws; combined with the blocking cost of reaching a dead network mapping, this is why each drive is probed on its own task under a timeout.
- [about_Pwsh — `-WorkingDirectory`](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_pwsh?view=powershell-7.6) and [Set-Location](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.management/set-location?view=powershell-7.5) — PowerShell can set a new process's initial directory or an individual runspace's current location, but those mechanisms do not own Filekin's app-level Files startup preference; Filekin must not rewrite the user's PowerShell profile to implement it.
- [StorageProviderSyncRootManager.GetCurrentSyncRoots](https://learn.microsoft.com/en-us/uwp/api/windows.storage.provider.storageprovidersyncrootmanager.getcurrentsyncroots?view=winrt-26100) and [Integrate a Cloud Storage Provider](https://learn.microsoft.com/en-us/windows/win32/shell/integrate-cloud-storage) — Windows exposes the current user's registered modern and legacy sync roots, including provider/account identity and filesystem root; `/places` should consume this registration instead of hardcoding OneDrive/Dropbox/iCloud paths.
- [System.Text.Json deserialization](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/deserialization) and [unmapped-member handling](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/missing-members) — official .NET 10 behavior used by the readable settings loader; unknown properties are ignored by default and are explicitly captured here so a later save preserves them.
- [File.Replace](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace?view=net-10.0) — official same-volume replacement API used after writing settings to a same-directory temporary file; passing a null backup name replaces without creating a backup.
- [Environment.GetFolderPath](https://learn.microsoft.com/en-us/dotnet/api/system.environment.getfolderpath?view=net-10.0) — official API used to locate the current user's Application Data folder before appending `Filekin\settings.json`.
- [Windows PowerShell Host Quickstart](https://learn.microsoft.com/en-us/powershell/scripting/developer/hosting/windows-powershell-host-quickstart?view=powershell-7.6) — official hosted PowerShell SDK/runspace entry point.
- [Creating Runspaces](https://learn.microsoft.com/en-us/powershell/scripting/developer/hosting/creating-runspaces?view=powershell-7.6) — official runspace hosting model.
- [RunspaceFactory.CreateRunspace](https://learn.microsoft.com/en-us/dotnet/api/system.management.automation.runspaces.runspacefactory.createrunspace?view=powershellsdk-7.6.0) — official API surface.
- [Get-Location](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.management/get-location?view=powershell-7.6) — official note that each runspace has its own current directory and that it differs from `Environment.CurrentDirectory`.
- [about_Providers](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_providers?view=powershell-7.6) and [about_Registry_Provider](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_registry_provider?view=powershell-7.6) — provider identity and `HKLM:` semantics.
- [Creating a Pseudoconsole session](https://learn.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session) — official pipe, `STARTUPINFOEX`, process creation, resize, independent drain, and teardown guidance.
- [CreatePseudoConsole](https://learn.microsoft.com/en-us/windows/console/createpseudoconsole), [ResizePseudoConsole](https://learn.microsoft.com/en-us/windows/console/resizepseudoconsole), and [ClosePseudoConsole](https://learn.microsoft.com/en-us/windows/console/closepseudoconsole) — official ConPTY API contracts.
- [Microsoft Terminal MiniTerm C# sample](https://github.com/microsoft/terminal/tree/main/samples/ConPTY/MiniTerm) — Microsoft-owned reference implementation.
- [Microsoft Terminal discussion: redirected parent stdio](https://github.com/microsoft/terminal/discussions/15814) — maintainer explanation and reproduced `STARTF_USESTDHANDLES` workaround for redirected hosts.
- [Microsoft.PowerShell.SDK 7.6.5](https://www.nuget.org/packages/Microsoft.PowerShell.SDK/) — current stable hosting package used by the spike.
- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy) — .NET 10 is the current active LTS release; the production scaffold targets .NET 10.
- [WPF documentation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/) and [What's new in WPF for .NET 10](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100) — official production UI framework baseline.
- [WM_GETMINMAXINFO](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-getminmaxinfo), [MonitorFromWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-monitorfromwindow), and [MONITORINFO](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-monitorinfo) — the custom-chrome window overrides native maximize bounds with the nearest monitor's taskbar-excluding work area. This is required because a `WindowStyle=None` WPF window can otherwise maximize over the taskbar.
- [GNU GPLv3 license text](https://www.gnu.org/licenses/gpl-3.0.txt) — canonical source for the repository `LICENSE`.
- [PowerShell.InvokeAsync](https://learn.microsoft.com/en-us/dotnet/api/system.management.automation.powershell.invokeasync?view=powershellsdk-7.6.0) and [PowerShell.StopAsync](https://learn.microsoft.com/en-us/dotnet/api/system.management.automation.powershell.stopasync?view=powershellsdk-7.6.0) — supported asynchronous invocation/cancellation APIs informing the production boundary.
- [Runspace.SessionStateProxy](https://learn.microsoft.com/en-us/dotnet/api/system.management.automation.runspaces.runspace.sessionstateproxy?view=powershellsdk-7.6.0), [PathIntrinsics](https://learn.microsoft.com/en-us/dotnet/api/system.management.automation.pathintrinsics?view=powershellsdk-7.6.0), and [PathInfo](https://learn.microsoft.com/en-us/dotnet/api/system.management.automation.pathinfo?view=powershellsdk-7.6.0) — direct runspace location inspection and provider/path identity without an extra `Get-Location` pipeline.
- [Clear-RecycleBin](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.management/clear-recyclebin?view=powershell-7.5) — the one built-in PowerShell Recycle Bin command on Windows; empties all current-user bins or specified drive bins, confirms by default, and supports `-Force`/`-Confirm:$false`. The installed PowerShell 7.6 environment exposes only this `*Recycle*` cmdlet in `Microsoft.PowerShell.Management`; there are no built-in Get/Restore-RecycleBin cmdlets.
- [Source-generated P/Invoke (LibraryImport)](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation) and [SYSLIB1062](https://learn.microsoft.com/en-us/dotnet/fundamentals/syslib-diagnostics/syslib1062) — the ConPTY interop uses `LibraryImport`, which requires `AllowUnsafeBlocks=true` for its generated marshalling; enabled on `Filekin.Infrastructure.Windows` only. The 2026-08-25 re-fetch of "Creating a Pseudoconsole session" confirmed the pipe/`STARTUPINFOEX`/independent-drain/teardown order the production session implements.

## Product Questions Requiring Owner Decision

Record genuinely unspecified user-visible/product/architecture decisions here rather than silently choosing them.

- **Keyboard binding for workspace/tab switching — RESOLVED 2026-08-26.** The owner chose `Ctrl+Tab` (and `Ctrl+Shift+Tab` to go back), explicitly requiring that it not steal other keys from the hosted shell. Implemented at the window ahead of the terminal-input branch and marked handled, so it is the only keystroke Filekin claims while a terminal has focus; `Tab`, `Shift+Tab`, `Ctrl+C`, `Escape` and `Y`/`N` still belong to the shell. Recorded in `DECISIONS.md`.
- **Terminal mouse selection/copy — RESOLVED 2026-08-26.** Implemented after the owner pointed out that copy/paste keys were useless with nothing selectable. Drag-select with `Ctrl+C` / `Ctrl+Shift+C` copy; see the copy-key decision in `DECISIONS.md`.
- **Terminal mouse reporting — RESOLVED 2026-08-26.** Implemented after the owner reported that scrolling was dead inside Claude Code. A program that asks for the mouse gets it; Shift overrides so the terminal's own selection stays reachable. See `DECISIONS.md`.
- **Assistive-text exposure for the terminal in v1? — open.** Exposing the cell grid as text to screen readers is still unimplemented and unspecified.
- **Agent relay / MCP server — recorded as Proposed 2026-08-26.** The owner asked whether Filekin could let Claude and Codex trade work, with an agent watching its own rate-limit window and handing off before it runs out, so two five-hour windows give roughly ten hours of continuous work. Written into `FEATURES.md` under Proposed as **Agent Relay Mailbox**, **Agent Turn Indicator**, **Agent Budget Watch**, and **Filekin MCP Server**. Nothing is implemented and nothing is committed to v1. The open questions are listed in `FEATURES.md` under "Still Proposed / Unresolved".
- **Live drive arrival/removal in `/drives` — RESOLVED 2026-08-26.** The owner asked whether an `E:` that becomes available, or a plugged-in USB stick, memory card, or phone, updates the view while it is on screen. It did not, and the owner approved implementing it. `/drives` now refreshes from `WM_DEVICECHANGE`; see **Work Completed**. The phone case is a `/drives` scope limit rather than a refresh problem — see **Known Problems**.
- **Copying a file path from the Files list — open.** The owner noted that "text selection is nowhere to be found" in the app. The Files list is intentionally a *filesystem* selection, not a text selection, so copying a path (or a list of paths) to the clipboard would be a distinct command or shortcut. Nothing in `FEATURES.md` or `UX-DESIGN.md` defines it, so it was not invented here.

- **Hosted terminal PowerShell profile — decided 2026-08-25.** Default is **load the profile** (`TerminalSessionRequest.LoadProfile = true`), so a hosted tab behaves like the user's real shell; new users are unaffected because a fresh PowerShell has no profile. It becomes a **user setting** (load vs. skip) when the settings system exists, with load remaining the default; a "skip profile" toggle serves users who want a clean, fast, can't-break shell. No code change needed now — the flag already exists. Tests pin `LoadProfile = false` for determinism.
- **Command-bar `@` vs. PowerShell's own `@` — RESOLVED 2026-08-25.** In the Files command bar, a token matching a known workspace reference (`@thisfolder`, `@selection`, a user Location) is always resolved as that reference — even when it would also be valid PowerShell splatting (for example `@selection` read as splatting `$selection`). Only tokens matching no known reference pass through untouched to the shell. A user needing splatting for a colliding variable name uses an independent terminal tab, which gets no `/`/`@` preprocessing. Recorded in DECISIONS.md ("Known Command-Bar References Win Over PowerShell Splatting"). This unblocks the `@` reference resolver.
- **Does the command-bar runspace load the user's PowerShell profile? — open.** Terminal tabs load the profile (decided above), but the persistent command-bar runspace currently does not (it uses `InitialSessionState.CreateDefault2()`, which does not run `$PROFILE`). Decide whether the command bar should reflect the user's profile aliases/functions, or intentionally stay a clean, predictable session. Note that not loading it also reduces the chance of a profile-defined command colliding with `/`/`@` handling.
- **Terminal root process: shell-as-root vs. tool-as-root — RESOLVED 2026-08-25.** `DECISIONS.md` had two stale entries ("Proposed — App-Owned Interactive Terminal Sessions" and "2026-08-24 — Interactive Tool Is the Primary Hosted Process") saying the launched tool is the terminal's primary process. That contradicted `ARCHITECTURE.md`, `ENGINEERING-GUARDRAILS.md`, and the CLAUDE.md invariants, which require **PowerShell as the root process** (tool runs as a child; prompt returns when the tool exits; tab closes when the root shell exits) — the model the shipped `ConPtyTerminalSession` implements. The owner confirmed shell-as-root; both `DECISIONS.md` entries are now marked **Superseded on 2026-08-25** and kept for history. Follow-up: the adjacent "Proposed — Preserve Completed and Failed Terminal Output" section still reflects the tool-as-root worldview (an inactive tab preserving output) and should be revisited against `ARCHITECTURE.md`'s "do not leave behind an exited terminal tab" rule when the terminal renderer/UI is built.

Confirmed by the owner on 2026-08-25:

- Unknown interactive fallback is a one-time fresh **Run in terminal** relaunch. There is no live promotion and no persistent user-defined routing rule in v1.
- Non-filesystem provider delegation creates a fresh ConPTY-backed PowerShell at the requested provider path. Files retains/restores its filesystem runspace location, and arbitrary runspace state is not transferred.
