using System.Globalization;
using System.Text;
using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.Infrastructure.Windows.Tests.Agents;

/// <summary>
/// The whole relay, exactly as a person runs it: Filekin starts one agent, the agents take turns
/// through their own written handoffs, and nobody presses anything between entries. It passes only
/// when the relay file really holds the entries afterwards, because an agent that says it wrote
/// something is not evidence that it did.
/// </summary>
/// <remarks>
/// <para>
/// This consumes real subscription usage on both accounts, so it is opt-in through its own switch.
/// The coordination database is kept outside the project folder: Filekin writes nothing into a
/// project it is coordinating.
/// </para>
/// <para>
/// It runs two jobs, because finishing one is not the same as being able to start the next: the
/// first ends with a reported completion, and the second is a fresh objective on the same folder and
/// the same conversations. A relay that can only ever do its first job is still broken.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed partial class LiveTenEntryRelayTests
{
    private const string RunVariable = "FILEKIN_RUN_LIVE_TEN_ENTRY_RELAY";

    /// <remarks>
    /// The same file the project's own instructions name. It used to be `handoff_text.txt` while
    /// `AGENTS.md` said `handoff_test.txt`, and the objective below tells the agents to follow those
    /// instructions exactly — so an agent that did as it was told wrote to the other file and the run
    /// failed counting entries that were never missing.
    /// </remarks>
    private const string RelayFileName = "handoff_test.txt";
    private const int EntriesPerJob = 10;

    /// <remarks>
    /// A relay costs real subscription usage on both accounts, so it runs on modest settings by
    /// default and each one is overridable. The defaults are deliberately not the smallest models
    /// available: the fault this test hunts is an agent ending its turn without handing over, and the
    /// weakest model is the likeliest to do exactly that, which spends a whole run to prove nothing.
    /// </remarks>
    private static string CodexModel =>
        Environment.GetEnvironmentVariable("FILEKIN_LIVE_RELAY_CODEX_MODEL") ?? "gpt-5.6-luna";

    private static string ClaudeModel =>
        Environment.GetEnvironmentVariable("FILEKIN_LIVE_RELAY_CLAUDE_MODEL") ?? "sonnet";

    private static string Effort =>
        Environment.GetEnvironmentVariable("FILEKIN_LIVE_RELAY_EFFORT") ?? "low";

    /// <remarks>
    /// How many jobs to run. Two is the real test; one exists so a scarce account can prove the first
    /// job without paying for the second.
    /// </remarks>
    private static int Jobs => Number("FILEKIN_LIVE_RELAY_JOBS", 2);

    /// <remarks>
    /// A stalled relay costs exactly as much as a working one while it waits, so this run gives up on
    /// silence rather than on a clock. Nothing written and nobody taking the turn for this long is a
    /// fault worth reporting now, with what was true at that moment, instead of at a deadline that
    /// only proves the same thing later and more expensively.
    /// </remarks>
    private static TimeSpan Stall => TimeSpan.FromMinutes(Number("FILEKIN_LIVE_RELAY_STALL_MINUTES", 5));

    private static TimeSpan JobLimit =>
        TimeSpan.FromMinutes(Number("FILEKIN_LIVE_RELAY_JOB_MINUTES", 20));

    /// <remarks>
    /// Who opens the relay. It decides more than the order of the lines: with an even target, the
    /// agent that did not start is the one that writes the last entry and reports the objective done.
    /// Starting Codex therefore never asks Codex to finish a job, and finishing is a different tool
    /// call from handing over — the one an agent is likeliest to skip, because its work is done and
    /// stopping looks like the same thing.
    /// </remarks>
    private static AgentProvider Starter =>
        string.Equals(
            Environment.GetEnvironmentVariable("FILEKIN_LIVE_RELAY_STARTER"),
            "claude",
            StringComparison.OrdinalIgnoreCase)
            ? AgentProvider.ClaudeCode
            : AgentProvider.Codex;

    private const string Approval =
        "Agents may work in this folder itself. This folder is safe to work in.";

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("RequiresLiveProvider")]
    [TestCategory("ConsumesSubscriptionUsage")]
    public async Task TheRelayReachesTenEntriesWithoutAnybodyPressingAnything()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(RunVariable), "1", StringComparison.Ordinal))
        {
            Assert.Inconclusive($"Set {RunVariable}=1 to run this explicit subscription-backed relay.");
        }

        var mcpExecutablePath = ReleaseMcpExecutablePath();
        Assert.IsTrue(File.Exists(mcpExecutablePath), $"Build the Release MCP first: {mcpExecutablePath}");

        var projectFolder = Path.GetFullPath(
            Environment.GetEnvironmentVariable("FILEKIN_LIVE_RUN_FOLDER")
            ?? Path.Combine("D:", Path.DirectorySeparatorChar.ToString(), "GitHub", "agent-test"));
        Assert.IsTrue(Directory.Exists(projectFolder), $"The QA folder does not exist: {projectFolder}");

        var relayFile = Path.Combine(projectFolder, RelayFileName);
        if (File.Exists(relayFile))
        {
            File.Delete(relayFile);
        }

        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"Filekin-live-relay-{Guid.NewGuid():N}",
            "state.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        TestContext.WriteLine($"Relay in {projectFolder}; state at {databasePath}");

        var policy = new AgentCoordinationPolicy(10, 30, TimeSpan.FromMinutes(10));
        using var store = new SqliteAgentProjectStore(databasePath);
        await store.SaveAsync(AgentProjectCoordinator.Create(projectFolder, ObjectiveFor(EntriesPerJob)));
        var project = await store.LoadByFolderAsync(projectFolder);
        Assert.IsNotNull(project);
        project = await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.GrantSharedCheckoutConsent(
                current,
                DateTimeOffset.UtcNow,
                Approval,
                AgentWorkMode.WorkOnItsOwn));

        // A relay is exactly when one agent runs low, so the safety limit must not be what stops the
        // hand-over from ever reaching the partner. An account paying with credits reads as out of
        // allowance and must still be allowed to work.
        project = await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.SetWorkOnLowAllowance(current, allowed: true));

        TestContext.WriteLine(
            $"Codex on {CodexModel} ({Effort}); Claude on {ClaudeModel} ({Effort}). "
            + $"{Starter} opens, so the other agent reports the objective done. "
            + $"{Jobs} job(s) of {EntriesPerJob} entries; stall {Stall.TotalMinutes:0} min, "
            + $"limit {JobLimit.TotalMinutes:0} min per job.");
        project = await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.ChooseModel(current, AgentProvider.Codex, CodexModel, Effort));
        project = await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.ChooseModel(current, AgentProvider.ClaudeCode, ClaudeModel, Effort));

        await using var runtime = new AgentCoordinationRuntime(
            store,
            policy,
            mcpExecutablePath,
            TimeProvider.System,
            TimeSpan.FromMinutes(5));
        await runtime.StartAsync();
        await using var service = new AgentRunService(
            runtime,
            store,
            new AgentProjectCoordinator(policy),
            new NativeAgentSessionLauncher(),
            TimeProvider.System,
            clockInTimeout: TimeSpan.FromMinutes(3),
            clockInPollInterval: TimeSpan.FromSeconds(2));

        try
        {
            for (var job = 1; job <= Jobs; job++)
            {
                var target = job * EntriesPerJob;
                if (job > 1)
                {
                    // Only a finished project accepts the next objective, so this also proves the
                    // first job really ended rather than merely stopping with ten entries written.
                    TestContext.WriteLine($"--- job {job}: asking for {target} entries ---");
                    await runtime.StartNewObjectiveAsync(project.Id, ObjectiveFor(target));
                }

                await RunOneJobAsync(service, store, project.Id, relayFile, job, target);
            }
        }
        finally
        {
            TestContext.WriteLine("Ending every session this run opened.");
            var failure = await service.StopAllSessionsAsync();
            if (failure is not null)
            {
                TestContext.WriteLine($"Cleanup problem: {failure}");
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }
    }

    /// <summary>
    /// Runs one job to its target and proves the project reported itself finished afterwards.
    /// </summary>
    private async Task RunOneJobAsync(
        AgentRunService service,
        SqliteAgentProjectStore store,
        Guid projectId,
        string relayFile,
        int job,
        int targetEntries)
    {
        var started = await service.StartAsync(projectId, Starter);
        TestContext.WriteLine($"Job {job}: started {started.ActiveAgent}.");

        var order = new List<AgentProvider>();
        if (started.ActiveAgent is { } first)
        {
            order.Add(first);
        }

        var deadline = DateTimeOffset.UtcNow + JobLimit;
        var lastProgressAt = DateTimeOffset.UtcNow;
        AgentProvider? lastHolder = started.ActiveAgent;
        var lastEntries = CountEntries(relayFile);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            var state = await store.LoadAsync(projectId);
            Assert.IsNotNull(state, "The project disappeared from the coordination store mid-relay.");

            var entries = CountEntries(relayFile);
            if (entries != lastEntries)
            {
                lastEntries = entries;
                lastProgressAt = DateTimeOffset.UtcNow;
                TestContext.WriteLine(
                    $"{DateTimeOffset.Now:HH:mm:ss}  entries={entries}  status={state.Status}  "
                    + $"holder={state.ActiveAgent?.ToString() ?? "none"}");
            }

            if (state.ActiveAgent is { } holder && holder != lastHolder)
            {
                order.Add(holder);
                lastHolder = holder;
                lastProgressAt = DateTimeOffset.UtcNow;
                TestContext.WriteLine($"{DateTimeOffset.Now:HH:mm:ss}  turn moved to {holder}.");
            }

            if (entries >= targetEntries && state.Status == AgentProjectStatus.Completed)
            {
                TestContext.WriteLine($"Job {job}: finished with {entries} entries.");
                AssertAlternated(order, job);
                return;
            }

            if (state.Status == AgentProjectStatus.NeedsAttention)
            {
                Assert.Fail(Diagnosis($"Job {job} stopped and asked for a person", state, service, entries, order));
            }

            // Nobody is working and nothing is left to hand over: the relay has stalled rather than
            // finished, and saying so beats waiting out the deadline on a paid account.
            if (state.Lease is null &&
                state.Status is AgentProjectStatus.Ready or AgentProjectStatus.Paused &&
                service.LiveSessions().Count == 0)
            {
                Assert.Fail(Diagnosis($"Job {job} stalled with nobody working", state, service, entries, order));
            }

            if (DateTimeOffset.UtcNow - lastProgressAt > Stall)
            {
                Assert.Fail(Diagnosis(
                    $"Job {job} wrote nothing and moved no turn for {Stall.TotalMinutes:0} minutes",
                    state,
                    service,
                    entries,
                    order));
            }
        }

        var timedOut = await store.LoadAsync(projectId);
        Assert.Fail(Diagnosis(
            $"Job {job} ran past {JobLimit.TotalMinutes:0} minutes",
            timedOut,
            service,
            CountEntries(relayFile),
            order));
    }

    private void AssertAlternated(List<AgentProvider> order, int job)
    {
        TestContext.WriteLine($"Job {job} turn order: {string.Join(" -> ", order)}");
        Assert.IsTrue(
            order.Count >= 4,
            $"The turn must actually alternate between the agents; it went {string.Join(" -> ", order)}.");
        CollectionAssert.Contains(order.ToArray(), AgentProvider.Codex);
        CollectionAssert.Contains(order.ToArray(), AgentProvider.ClaudeCode);
    }

    /// <summary>
    /// Everything that was true when the relay stopped. A paid run is worth one failure message that
    /// answers why, rather than a count that only says it did not finish.
    /// </summary>
    private static string Diagnosis(
        string headline,
        AgentProjectState? state,
        AgentRunService service,
        int entries,
        List<AgentProvider> order)
    {
        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"{headline}. Entries written: {entries}.");
        if (state is null)
        {
            report.AppendLine("The project could not be read back from the store.");
            return report.ToString();
        }

        report.AppendLine(CultureInfo.InvariantCulture, $"Status: {state.Status}.");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"Attention: {state.AttentionReason ?? "none"}.");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"Lease: {state.Lease?.Owner.ToString() ?? "nobody"}.");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"Turn order so far: {string.Join(" -> ", order)}.");

        foreach (var participant in state.Participants.Values)
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"  {participant.Provider}: connection={participant.ConnectionState} "
                + $"turn={participant.TurnState} session={participant.NativeSessionId ?? "none"} "
                + $"last report={service.LastReport(state.Id, participant.Provider) ?? "none"}");
        }

        report.AppendLine(CultureInfo.InvariantCulture,
            $"Pending handoff: {Describe(state.PendingHandoff)}.");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"Last handoff: {Describe(state.LastHandoff)}.");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"Live sessions Filekin is watching: {service.LiveSessions().Count}.");
        return report.ToString();
    }

    private static string Describe(AgentHandoff? handoff) =>
        handoff is null
            ? "none"
            : $"{handoff.From} -> {handoff.To}, accepted {handoff.AcceptedAt?.ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? "never"}, "
            + $"\"{handoff.Summary}\"";

    private static string ObjectiveFor(int entries) =>
        "Read AGENTS.md (Codex) or CLAUDE.md (Claude) and follow the relay rules there exactly. "
        + $"Append exactly one numbered entry to {RelayFileName} per turn, never rewrite entries that "
        + $"are already in the file, then hand over. The job is finished when the file holds {entries} "
        + "entries.";

    private static int Number(string variable, int fallback) =>
        int.TryParse(
            Environment.GetEnvironmentVariable(variable),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed) && parsed > 0
            ? parsed
            : fallback;

    /// <remarks>
    /// An entry is a line that opens with its number and a dot. This used to require the first two
    /// characters to both be digits, which is true of "10." and of nothing else in a ten-entry relay:
    /// a finished run counted as one entry, the target was never reached, and the run failed on its
    /// own stall timer minutes after the agents had done the job correctly and stopped.
    /// </remarks>
    private static string[] ReadEntries(string relayFile) =>
        !File.Exists(relayFile)
            ? []
            : File.ReadAllLines(relayFile)
                .Select(line => line.Trim())
                .Where(line => EntryLine().IsMatch(line))
                .ToArray();

    [System.Text.RegularExpressions.GeneratedRegex(@"^\d+\.\s")]
    private static partial System.Text.RegularExpressions.Regex EntryLine();

    private static int CountEntries(string relayFile) => ReadEntries(relayFile).Length;

    private static string ReleaseMcpExecutablePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Filekin.sln")))
        {
            directory = directory.Parent;
        }

        Assert.IsNotNull(directory, "The repository root could not be found from the test output folder.");
        return Path.Combine(
            directory.FullName,
            "src",
            "Filekin.Mcp",
            "bin",
            "Release",
            "net10.0-windows",
            "Filekin.Mcp.exe");
    }
}
