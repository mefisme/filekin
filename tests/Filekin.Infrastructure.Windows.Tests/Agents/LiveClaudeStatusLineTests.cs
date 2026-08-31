using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;
using Microsoft.Data.Sqlite;

namespace Filekin.Infrastructure.Windows.Tests.Agents;

/// <summary>
/// Explicit, gated probe: one very small subscription-backed Claude response in a disposable checkout,
/// only to prove that Filekin's inline status-line helper really receives the documented quota JSON and
/// stores it as this project's Claude usage.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LiveClaudeStatusLineTests
{
    private const string RunVariable = "FILEKIN_RUN_LIVE_CLAUDE_STATUS_LINE";

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("RequiresLiveProvider")]
    [TestCategory("ConsumesSubscriptionUsage")]
    public async Task ClaudeStatusLineDeliversThisProjectsQuotaWindows()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(RunVariable), "1", StringComparison.Ordinal))
        {
            Assert.Inconclusive($"Set {RunVariable}=1 to run this explicit subscription-backed probe.");
        }

        var mcpExecutablePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Filekin.Mcp",
            "bin",
            "Release",
            "net10.0-windows",
            "Filekin.Mcp.exe");
        Assert.IsTrue(File.Exists(mcpExecutablePath), $"Build the Release MCP executable first: {mcpExecutablePath}");

        var probeRoot = Path.Combine(Path.GetTempPath(), $"Filekin-live-status-line-{Guid.NewGuid():N}");
        var projectFolder = Path.Combine(probeRoot, "project");
        var stateDatabasePath = Path.Combine(probeRoot, "state.db");
        Directory.CreateDirectory(projectFolder);
        TestContext.WriteLine($"Disposable probe: {probeRoot}");

        var adapter = new ClaudeBackgroundSessionAdapter();
        ClaudeBackgroundSessionSnapshot? session = null;
        try
        {
            using var store = new SqliteAgentProjectStore(stateDatabasePath);
            var project = AgentProjectCoordinator.Create(projectFolder);
            await store.SaveAsync(project);

            await using var runtime = new AgentCoordinationRuntime(
                store,
                new AgentCoordinationPolicy(10, 30, TimeSpan.FromMinutes(10)),
                mcpExecutablePath);
            await runtime.StartAsync();
            var prepared = await runtime.PrepareProjectAsync(project.Id);
            var claudeMcp = prepared.McpServers.Single(server => server.Provider == AgentProvider.ClaudeCode);

            var plan = ClaudeBackgroundSessionAdapter.CreateLaunchPlan(
                projectFolder,
                "Filekin Claude status-line probe",
                "This is a Filekin quota-reading probe. Reply with the single word ready. Do not read, create, edit, or delete any file, and do not call any tool.",
                claudeMcp);
            TestContext.WriteLine($"Previewed settings: {plan.SettingsPreviewJson}");

            using var launchTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            session = await adapter.LaunchAsync(plan.ApproveSharedCheckout(), launchTimeout.Token);
            TestContext.WriteLine($"Claude session {session.NativeId}: {session.Lifecycle}");

            AgentUsageSnapshot? observation = null;
            using var observationTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            while (!observationTimeout.IsCancellationRequested && observation is null)
            {
                observation = await store.ReadUsageObservationAsync(
                    project.Id,
                    AgentProvider.ClaudeCode,
                    observationTimeout.Token);
                if (observation is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), observationTimeout.Token);
                }
            }

            Assert.IsNotNull(
                observation,
                "Claude did not deliver a status-line quota observation for this project. That means the " +
                "inline status line did not run for a background session, or the account reported no " +
                "rate-limit windows.");
            foreach (var window in observation.Windows)
            {
                TestContext.WriteLine(
                    $"{window.Name}: {window.UsedPercent}% used, resets {window.ResetsAt?.ToString("O") ?? "unknown"}");
            }

            Assert.IsTrue(observation.IsKnown);
            Assert.IsTrue(observation.Windows.All(window => window.Name.StartsWith("claude:", StringComparison.Ordinal)));

            var usageSource = new ClaudeAgentUsageSource(store, project.Id, projectFolder);
            var appVisible = await usageSource.ReadAsync();
            Assert.IsTrue(appVisible.IsKnown, "The app-side usage source did not read the stored observation.");
            Assert.AreEqual(observation.ObservedAt, appVisible.ObservedAt);

            var state = await store.LoadAsync(project.Id);
            Assert.IsNotNull(state);
            Assert.IsNull(state.Lease, "A status-line observation must never create a writer lease.");
        }
        finally
        {
            if (session is not null)
            {
                try
                {
                    var stopped = await adapter.StopAsync(projectFolder, session.NativeId);
                    TestContext.WriteLine($"Stopped probe session: {stopped?.Lifecycle.ToString() ?? "already gone"}");
                }
                catch (InvalidOperationException exception)
                {
                    TestContext.WriteLine($"Could not stop the probe session automatically: {exception.Message}");
                }
            }

            SqliteConnection.ClearAllPools();
            await DeleteDisposableProbeAsync(probeRoot);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Filekin.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the Filekin repository root.");
    }

    private static async Task DeleteDisposableProbeAsync(string probeRoot)
    {
        var expectedParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        var resolved = Path.GetFullPath(probeRoot);
        if (!string.Equals(Path.GetDirectoryName(resolved), expectedParent, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(resolved).StartsWith("Filekin-live-status-line-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Refusing unexpected live-probe cleanup target: {resolved}");
        }

        for (var attempt = 0; Directory.Exists(resolved); attempt++)
        {
            try
            {
                Directory.Delete(resolved, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)));
            }
            catch (UnauthorizedAccessException) when (attempt < 5)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)));
            }
        }
    }
}
