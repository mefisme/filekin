using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.Infrastructure.Windows.Tests.Agents;

/// <summary>
/// The path a person actually uses: Filekin starts a real agent through <see cref="AgentRunService"/>,
/// waits for it to clock in, gives it the turn, and the agent does the work in the approved folder.
/// These consume real subscription usage, so each one is opt-in through its own switch.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LiveAgentRunTests
{
    private const string Approval =
        "Agents may work in this folder itself. This folder is safe to work in.";

    private const string ExpectedFileName = "hello.txt";
    private const string ExpectedText = "hello";

    private const string Objective =
        "Create a file named hello.txt in this folder containing exactly the word hello, then stop. "
        + "Do not create anything else.";

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("RequiresLiveProvider")]
    [TestCategory("ConsumesSubscriptionUsage")]
    public Task CodexStartedByFilekinDoesTheWorkInTheApprovedFolder() =>
        RunAsync(AgentProvider.Codex, "FILEKIN_RUN_LIVE_AGENT_RUN_CODEX");

    [TestMethod]
    [TestCategory("RequiresLiveProvider")]
    [TestCategory("ConsumesSubscriptionUsage")]
    public Task ClaudeStartedByFilekinDoesTheWorkInTheApprovedFolder() =>
        RunAsync(AgentProvider.ClaudeCode, "FILEKIN_RUN_LIVE_AGENT_RUN_CLAUDE");

    /// <summary>
    /// The relay: Filekin starts one agent, the user asks it to hand over, and the partner that was
    /// never running is started at that moment and takes the turn.
    /// </summary>
    [TestMethod]
    [TestCategory("RequiresLiveProvider")]
    [TestCategory("ConsumesSubscriptionUsage")]
    public async Task PassingTheTurnStartsThePartnerThatWasNotRunning()
    {
        const string RunVariable = "FILEKIN_RUN_LIVE_AGENT_RELAY";
        if (!string.Equals(Environment.GetEnvironmentVariable(RunVariable), "1", StringComparison.Ordinal))
        {
            Assert.Inconclusive($"Set {RunVariable}=1 to run this explicit subscription-backed relay.");
        }

        var (store, service, runtime, project) = await PrepareAsync();
        try
        {
            var started = await service.StartAsync(project.Id, AgentProvider.Codex);
            Assert.AreEqual(AgentProvider.Codex, started.ActiveAgent);
            CollectionAssert.AreEqual(
                new[] { AgentProvider.Codex },
                service.RunningAgents(project.Id).ToArray(),
                "Filekin does not keep the partner running and idle.");

            var passing = await service.PassTheTurnAsync(project.Id);
            Assert.AreEqual(AgentProjectStatus.HandoffPending, passing.Status);
            TestContext.WriteLine("Hand-over requested; waiting for Codex to finish safely.");

            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(8);
            AgentProjectState? state = null;
            while (DateTimeOffset.UtcNow < deadline)
            {
                state = await store.LoadAsync(project.Id);
                if (state?.ActiveAgent == AgentProvider.ClaudeCode ||
                    state?.Status is AgentProjectStatus.NeedsAttention)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(3));
            }

            TestContext.WriteLine($"Final status: {state?.Status}; holder: {state?.ActiveAgent}");
            TestContext.WriteLine($"Handoff: {state?.LastHandoff?.Summary ?? "none"}");
            TestContext.WriteLine($"Reason: {state?.AttentionReason ?? "none"}");
            Assert.AreEqual(
                AgentProvider.ClaudeCode,
                state?.ActiveAgent,
                $"The partner never took the turn. Status {state?.Status}; reason {state?.AttentionReason}.");
            Assert.IsNotNull(state?.LastHandoff, "A relay leaves the written handoff behind.");
            CollectionAssert.Contains(
                service.RunningAgents(project.Id).ToArray(),
                AgentProvider.ClaudeCode,
                "The partner was started at the moment there was something to hand over.");
        }
        finally
        {
            await StopQuietlyAsync(service, project.Id);
            await service.DisposeAsync();
            await runtime.DisposeAsync();
            store.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }
    }

    private async Task<(SqliteAgentProjectStore Store, AgentRunService Service,
        AgentCoordinationRuntime Runtime, AgentProjectState Project)> PrepareAsync()
    {
        var mcpExecutablePath = ReleaseMcpExecutablePath();
        Assert.IsTrue(File.Exists(mcpExecutablePath), $"Build the Release MCP executable first: {mcpExecutablePath}");

        var projectFolder = Path.GetFullPath(
            Environment.GetEnvironmentVariable("FILEKIN_LIVE_RUN_FOLDER")
            ?? Path.Combine("D:", Path.DirectorySeparatorChar.ToString(), "github", "agent-test"));
        Assert.IsTrue(Directory.Exists(projectFolder), $"The QA folder does not exist: {projectFolder}");
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"Filekin-live-run-{Guid.NewGuid():N}",
            "state.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        TestContext.WriteLine($"Live run in {projectFolder}; state at {databasePath}");
        var leftover = Path.Combine(projectFolder, ExpectedFileName);
        if (File.Exists(leftover))
        {
            File.Delete(leftover);
        }

        var store = new SqliteAgentProjectStore(databasePath);
        await store.SaveAsync(AgentProjectCoordinator.Create(projectFolder, Objective));
        var project = await store.LoadByFolderAsync(projectFolder);
        Assert.IsNotNull(project);
        project = await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.GrantSharedCheckoutConsent(
                current,
                DateTimeOffset.UtcNow,
                Approval,
                SharedFolderTrust.TrustThisFolder));

        // A relay is exactly when one agent is running low, so the safety limit must not be the thing
        // that stops the hand-over from reaching anybody.
        project = await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.SetWorkOnLowAllowance(current, allowed: true));

        var policy = new AgentCoordinationPolicy(10, 30, TimeSpan.FromMinutes(10));
        var runtime = new AgentCoordinationRuntime(
            store,
            policy,
            mcpExecutablePath,
            TimeProvider.System,
            TimeSpan.FromMinutes(5));
        await runtime.StartAsync();
        var service = new AgentRunService(
            runtime,
            store,
            new AgentProjectCoordinator(policy),
            new NativeAgentSessionLauncher(),
            TimeProvider.System,
            clockInTimeout: TimeSpan.FromMinutes(3),
            clockInPollInterval: TimeSpan.FromSeconds(2));
        return (store, service, runtime, project);
    }

    private async Task RunAsync(AgentProvider provider, string runVariable)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(runVariable), "1", StringComparison.Ordinal))
        {
            Assert.Inconclusive($"Set {runVariable}=1 to run this explicit subscription-backed run.");
        }

        var mcpExecutablePath = ReleaseMcpExecutablePath();
        Assert.IsTrue(File.Exists(mcpExecutablePath), $"Build the Release MCP executable first: {mcpExecutablePath}");

        // The owner's own throwaway QA folder, so a real run can be inspected afterwards. The
        // coordination database is deliberately kept outside it: Filekin writes nothing into a project.
        var projectFolder = Environment.GetEnvironmentVariable("FILEKIN_LIVE_RUN_FOLDER")
            ?? Path.Combine("D:", Path.DirectorySeparatorChar.ToString(), "github", "agent-test");
        projectFolder = Path.GetFullPath(projectFolder);
        Assert.IsTrue(Directory.Exists(projectFolder), $"The QA folder does not exist: {projectFolder}");
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"Filekin-live-run-{Guid.NewGuid():N}",
            "state.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        TestContext.WriteLine($"Live run in {projectFolder}; state at {databasePath}");
        var existingFile = Path.Combine(projectFolder, ExpectedFileName);
        if (File.Exists(existingFile))
        {
            File.Delete(existingFile);
        }

        using var store = new SqliteAgentProjectStore(databasePath);
        var coordinator = new AgentProjectCoordinator(
            new AgentCoordinationPolicy(10, 30, TimeSpan.FromMinutes(10)));
        await store.SaveAsync(AgentProjectCoordinator.Create(projectFolder, Objective));
        var project = await store.LoadByFolderAsync(projectFolder);
        Assert.IsNotNull(project);
        project = await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.GrantSharedCheckoutConsent(
                current,
                DateTimeOffset.UtcNow,
                Approval,
                SharedFolderTrust.TrustThisFolder));

        await using var runtime = new AgentCoordinationRuntime(
            store,
            new AgentCoordinationPolicy(10, 30, TimeSpan.FromMinutes(10)),
            mcpExecutablePath,
            TimeProvider.System,
            TimeSpan.FromMinutes(5));
        await runtime.StartAsync();
        await using var service = new AgentRunService(
            runtime,
            store,
            coordinator,
            new NativeAgentSessionLauncher(),
            TimeProvider.System,
            clockInTimeout: TimeSpan.FromMinutes(3),
            clockInPollInterval: TimeSpan.FromSeconds(2));

        var expectedFile = Path.Combine(projectFolder, ExpectedFileName);
        try
        {
            var started = await service.StartAsync(project.Id, provider);
            TestContext.WriteLine($"Turn granted to {started.ActiveAgent} with status {started.Status}.");
            Assert.AreEqual(provider, started.ActiveAgent);
            Assert.AreEqual(AgentProjectStatus.Working, started.Status);

            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(5);
            while (!File.Exists(expectedFile) && DateTimeOffset.UtcNow < deadline)
            {
                var state = await store.LoadAsync(project.Id);
                if (state?.Status is AgentProjectStatus.NeedsAttention or AgentProjectStatus.Paused)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(3));
            }

            var final = await store.LoadAsync(project.Id);
            TestContext.WriteLine($"Final status: {final?.Status}; reason: {final?.AttentionReason}");
            TestContext.WriteLine($"Agent said: {service.LastReport(project.Id, provider) ?? "nothing"}");
            TestContext.WriteLine(
                $"Folder now holds: {string.Join(", ", Directory.GetFileSystemEntries(projectFolder).Select(Path.GetFileName))}");

            Assert.IsTrue(
                File.Exists(expectedFile),
                $"{provider} did not create {ExpectedFileName}. "
                + $"Status {final?.Status}; reason {final?.AttentionReason}; "
                + $"agent said: {service.LastReport(project.Id, provider) ?? "nothing"}");
            StringAssert.Contains(
                await File.ReadAllTextAsync(expectedFile),
                ExpectedText,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await StopQuietlyAsync(service, project.Id);
            await EndAnyBackgroundSessionAsync(store, project.Id, provider, projectFolder);
            await service.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }
    }

    /// <summary>
    /// A run that never reached the turn leaves nothing for the lease-owner stop to end, and a live
    /// Claude session keeps its Filekin MCP companion alive, which then locks the Release build. The
    /// recorded session identity is Filekin's own, so cleanup can always name the session it opened.
    /// </summary>
    private async Task EndAnyBackgroundSessionAsync(
        SqliteAgentProjectStore store,
        Guid projectId,
        AgentProvider provider,
        string projectFolder)
    {
        if (provider != AgentProvider.ClaudeCode)
        {
            return;
        }

        var state = await store.LoadAsync(projectId);
        if (state?.Participant(provider).NativeSessionId is not { Length: > 0 } nativeSessionId)
        {
            return;
        }

        try
        {
            var stopped = await new ClaudeBackgroundSessionAdapter()
                .StopAsync(projectFolder, nativeSessionId);
            TestContext.WriteLine(
                $"Background session {nativeSessionId}: {stopped?.Lifecycle.ToString() ?? "already gone"}.");
        }
#pragma warning disable CA1031 // Cleanup must not hide the real assertion failure.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            TestContext.WriteLine($"Could not confirm the background session stopped: {exception.Message}");
        }
    }

    private async Task StopQuietlyAsync(AgentRunService service, Guid projectId)
    {
        try
        {
            var state = await service.RequestStopAsync(projectId);
            TestContext.WriteLine($"Stop requested; status {state.Status}.");
        }
#pragma warning disable CA1031 // Cleanup must not hide the real assertion failure.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            TestContext.WriteLine($"Nothing to stop: {exception.Message}");
        }
    }

    private static string ReleaseMcpExecutablePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Filekin.sln")))
        {
            directory = directory.Parent;
        }

        Assert.IsNotNull(directory, "The repository root could not be found.");
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
