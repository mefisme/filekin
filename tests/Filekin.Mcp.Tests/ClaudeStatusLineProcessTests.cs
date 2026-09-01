using System.Diagnostics;
using System.Text;
using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;
using Microsoft.Data.Sqlite;

namespace Filekin.Mcp.Tests;

/// <summary>
/// Runs the companion in status-line mode exactly as Claude Code would: a separate process with the
/// documented JSON on stdin. No provider, model usage, or network access is involved.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ClaudeStatusLineProcessTests
{
    private string _directory = null!;
    private string _projectFolder = null!;
    private string _databasePath = null!;

    [TestInitialize]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"Filekin-status-line-process-{Guid.NewGuid():N}");
        _projectFolder = Path.Combine(_directory, "project");
        _databasePath = Path.Combine(_directory, "state.db");
        Directory.CreateDirectory(_projectFolder);
    }

    [TestCleanup]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task StatusLineProcessRecordsTheProjectsClaudeUsageAndPrintsNothing()
    {
        var project = await CreateProjectAsync();

        var result = await RunAsync(project.Id, StatusJson(_projectFolder, 23.5, 41.2));

        Assert.AreEqual(0, result.ExitCode, result.StandardError);
        Assert.AreEqual(string.Empty, result.StandardOutput.Trim());

        using var store = new SqliteAgentProjectStore(_databasePath);
        var stored = await store.ReadUsageObservationAsync(AgentProvider.ClaudeCode);
        Assert.IsNotNull(stored);
        Assert.HasCount(2, stored.Windows);
        Assert.AreEqual(23.5, stored.Windows[0].UsedPercent);
        Assert.AreEqual(41.2, stored.Windows[1].UsedPercent);

        var state = await store.LoadAsync(project.Id);
        Assert.IsNotNull(state);
        Assert.IsNull(state.Lease);
        Assert.AreEqual(
            AgentConnectionState.Offline,
            state.Participant(AgentProvider.ClaudeCode).ConnectionState);
    }

    [TestMethod]
    public async Task StatusLineProcessRefusesAnotherCheckoutAndAnUnknownProject()
    {
        var project = await CreateProjectAsync();
        var otherFolder = Path.Combine(_directory, "other");
        Directory.CreateDirectory(otherFolder);

        var foreign = await RunAsync(project.Id, StatusJson(otherFolder, 5, 5));
        Assert.AreEqual(1, foreign.ExitCode);
        StringAssert.Contains(foreign.StandardError, "another checkout");

        var unknown = await RunAsync(Guid.NewGuid(), StatusJson(_projectFolder, 5, 5));
        Assert.AreEqual(1, unknown.ExitCode);

        using var store = new SqliteAgentProjectStore(_databasePath);
        Assert.IsNull(await store.ReadUsageObservationAsync(AgentProvider.ClaudeCode));
    }

    [TestMethod]
    public async Task StatusLineProcessAcceptsAPayloadWithoutRateLimitsAndRefusesGarbage()
    {
        var project = await CreateProjectAsync();

        var pending = await RunAsync(
            project.Id,
            $$"""
              { "cwd": {{Json(_projectFolder)}}, "context_window": { "used_percentage": 8 } }
              """);
        Assert.AreEqual(0, pending.ExitCode, pending.StandardError);

        var garbage = await RunAsync(project.Id, "not json");
        Assert.AreEqual(1, garbage.ExitCode);
        StringAssert.Contains(garbage.StandardError, "status JSON");

        using var store = new SqliteAgentProjectStore(_databasePath);
        Assert.IsNull(await store.ReadUsageObservationAsync(AgentProvider.ClaudeCode));
    }

    [TestMethod]
    public async Task StatusLineProcessRefusesArgumentsOutsideTheFixedForm()
    {
        var project = await CreateProjectAsync();

        var result = await RunAsync(
            [
                ClaudeStatusLineCommand.ModeArgument,
                "--project",
                project.Id.ToString("D"),
                "--provider",
                "codex",
                "--folder",
                _projectFolder,
                "--state-db",
                _databasePath,
            ],
            StatusJson(_projectFolder, 5, 5));

        Assert.AreEqual(1, result.ExitCode);
        StringAssert.Contains(result.StandardError, "fixed project-scoped arguments");
    }

    private async Task<AgentProjectState> CreateProjectAsync()
    {
        var project = AgentProjectCoordinator.Create(_projectFolder);
        using (var store = new SqliteAgentProjectStore(_databasePath))
        {
            await store.SaveAsync(project);
        }

        SqliteConnection.ClearAllPools();
        return project;
    }

    private Task<ProcessResult> RunAsync(Guid projectId, string statusJson) =>
        RunAsync(
            [
                ClaudeStatusLineCommand.ModeArgument,
                "--project",
                projectId.ToString("D"),
                "--provider",
                "claude",
                "--folder",
                _projectFolder,
                "--state-db",
                _databasePath,
            ],
            statusJson);

    private static async Task<ProcessResult> RunAsync(string[] arguments, string statusJson)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.ArgumentList.Add(typeof(FilekinAgentTools).Assembly.Location);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The Filekin companion did not start.");
        await process.StandardInput.WriteAsync(statusJson);
        process.StandardInput.Close();
        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    private static string StatusJson(string folderPath, double fiveHour, double sevenDay) =>
        $$"""
          {
            "session_id": "8f2a4c1e",
            "cwd": {{Json(folderPath)}},
            "workspace": {
              "current_dir": {{Json(folderPath)}},
              "project_dir": {{Json(folderPath)}}
            },
            "rate_limits": {
              "five_hour": { "used_percentage": {{fiveHour}}, "resets_at": 1738425600 },
              "seven_day": { "used_percentage": {{sevenDay}}, "resets_at": 1738857600 }
            }
          }
          """;

    private static string Json(string value) => System.Text.Json.JsonSerializer.Serialize(value);

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
