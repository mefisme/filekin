using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.Infrastructure.Windows.Tests.Agents;

/// <summary>
/// The whole relay, exactly as a person runs it: Filekin starts one agent, the agents take turns
/// through their own written handoffs, and nobody presses anything between entries. It passes only
/// when <c>handoff_text.txt</c> really holds ten entries afterwards, because an agent that says it
/// wrote something is not evidence that it did.
/// </summary>
/// <remarks>
/// This consumes real subscription usage on both accounts, so it is opt-in through its own switch.
/// The coordination database is kept outside the project folder: Filekin writes nothing into a
/// project it is coordinating.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LiveTenEntryRelayTests
{
    private const string RunVariable = "FILEKIN_RUN_LIVE_TEN_ENTRY_RELAY";
    private const string RelayFileName = "handoff_text.txt";
    private const int RequiredEntries = 10;

    private const string Approval =
        "Agents may work in this folder itself. This folder is safe to work in.";

    private const string Objective =
        "Read AGENTS.md (Codex) or CLAUDE.md (Claude) and follow the relay rules there exactly. "
        + "Append exactly one numbered entry to handoff_text.txt per turn, never rewrite entries that "
        + "are already in the file, then hand over. The job is finished when the file holds ten "
        + "entries.";

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
        await store.SaveAsync(AgentProjectCoordinator.Create(projectFolder, Objective));
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
        // hand-over from ever reaching the partner.
        project = await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.SetWorkOnLowAllowance(current, allowed: true));

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

        var order = new List<AgentProvider>();
        try
        {
            var started = await service.StartAsync(project.Id, AgentProvider.Codex);
            order.Add(AgentProvider.Codex);
            TestContext.WriteLine($"Started {started.ActiveAgent}.");

            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(45);
            AgentProvider? lastHolder = started.ActiveAgent;
            var lastEntries = -1;
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                var state = await store.LoadAsync(project.Id);
                if (state is null)
                {
                    break;
                }

                var entries = CountEntries(relayFile);
                if (entries != lastEntries)
                {
                    lastEntries = entries;
                    TestContext.WriteLine(
                        $"{DateTimeOffset.Now:HH:mm:ss}  entries={entries}  status={state.Status}  "
                        + $"holder={state.ActiveAgent?.ToString() ?? "none"}");
                }

                if (state.ActiveAgent is { } holder && holder != lastHolder)
                {
                    order.Add(holder);
                    lastHolder = holder;
                    TestContext.WriteLine($"{DateTimeOffset.Now:HH:mm:ss}  turn moved to {holder}.");
                }

                if (entries >= RequiredEntries)
                {
                    break;
                }

                if (state.Status == AgentProjectStatus.NeedsAttention)
                {
                    Assert.Fail(
                        $"The relay stopped and asked for a person: {state.AttentionReason}. "
                        + $"Entries written: {entries}.");
                }

                // Nobody is working and nothing is left to hand over: the relay has stalled rather
                // than finished, and saying so beats waiting out the deadline.
                if (state.Lease is null &&
                    state.Status is AgentProjectStatus.Ready or AgentProjectStatus.Paused &&
                    service.LiveSessions().Count == 0)
                {
                    Assert.Fail(
                        $"The relay stalled with nobody working after {entries} entries. "
                        + $"Status {state.Status}; last handoff: {state.LastHandoff?.Summary ?? "none"}.");
                }
            }

            var finalEntries = ReadEntries(relayFile);
            TestContext.WriteLine($"Turn order: {string.Join(" -> ", order)}");
            TestContext.WriteLine($"File holds {finalEntries.Length} entries:");
            foreach (var entry in finalEntries)
            {
                TestContext.WriteLine($"  {entry}");
            }

            Assert.AreEqual(
                RequiredEntries,
                finalEntries.Length,
                "The relay must leave ten real entries in the file, not ten claims that it did.");
            Assert.IsTrue(
                order.Count >= 4,
                $"The turn must actually alternate between the agents; it went {string.Join(" -> ", order)}.");
            CollectionAssert.Contains(order, AgentProvider.Codex);
            CollectionAssert.Contains(order, AgentProvider.ClaudeCode);
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

    private static string[] ReadEntries(string relayFile) =>
        !File.Exists(relayFile)
            ? []
            : File.ReadAllLines(relayFile)
                .Select(line => line.Trim())
                .Where(line => line.Length > 2 && char.IsAsciiDigit(line[0]) && char.IsAsciiDigit(line[1]))
                .ToArray();

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
