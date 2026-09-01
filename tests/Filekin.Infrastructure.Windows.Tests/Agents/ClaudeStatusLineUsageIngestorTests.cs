using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;
using Microsoft.Data.Sqlite;

namespace Filekin.Infrastructure.Windows.Tests.Agents;

[TestClass]
[DoNotParallelize]
public sealed class ClaudeStatusLineUsageIngestorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 9, 0, 0, TimeSpan.Zero);

    private static readonly string[] ExpectedStoredText =
    [
        "2026-08-30T09:00:00.0000000+00:00",
        "claude:five_hour",
        "2026-08-30T09:00:00.0000000+00:00",
        "claude:seven_day",
    ];

    private string _directory = null!;
    private string _projectFolder = null!;
    private string _databasePath = null!;
    private Guid _projectId;

    [TestInitialize]
    public async Task SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"Filekin-status-line-{Guid.NewGuid():N}");
        _projectFolder = Path.Combine(_directory, "project");
        _databasePath = Path.Combine(_directory, "state.db");
        Directory.CreateDirectory(_projectFolder);
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = AgentProjectCoordinator.Create(_projectFolder);
        await store.SaveAsync(project);
        _projectId = project.Id;
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
    public async Task DocumentedStatusPayloadBecomesThisProjectsClaudeUsage()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);

        var outcome = await IngestAsync(store, StatusJson(_projectFolder, 23.5, 41.2), Now);

        Assert.AreEqual(ClaudeStatusLineIngestion.Recorded, outcome);
        var stored = await store.ReadUsageObservationAsync(AgentProvider.ClaudeCode);
        Assert.IsNotNull(stored);
        Assert.AreEqual(Now, stored.ObservedAt);
        Assert.HasCount(2, stored.Windows);
        Assert.AreEqual("claude:five_hour", stored.Windows[0].Name);
        Assert.AreEqual(23.5, stored.Windows[0].UsedPercent);
        Assert.AreEqual(TimeSpan.FromHours(5), stored.Windows[0].WindowDuration);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1738425600), stored.Windows[0].ResetsAt);
        Assert.AreEqual("claude:seven_day", stored.Windows[1].Name);
        Assert.AreEqual(41.2, stored.Windows[1].UsedPercent);
        Assert.AreEqual(TimeSpan.FromDays(7), stored.Windows[1].WindowDuration);
    }

    [TestMethod]
    public async Task NothingButTheParsedQuotaWindowsReachesStateDb()
    {
        using (var store = new SqliteAgentProjectStore(_databasePath))
        {
            Assert.AreEqual(
                ClaudeStatusLineIngestion.Recorded,
                await IngestAsync(store, StatusJson(_projectFolder, 10, 20), Now));
        }

        var storedText = new List<string>();
        await using (var connection = new SqliteConnection($"Data Source={_databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT o.observed_at, w.name
                FROM agent_usage_observations o
                JOIN agent_usage_observation_windows w
                    ON w.provider = o.provider
                ORDER BY w.name;
                """;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                storedText.Add(reader.GetString(0));
                storedText.Add(reader.GetString(1));
            }
        }

        CollectionAssert.AreEqual(ExpectedStoredText, storedText);
    }

    [TestMethod]
    public async Task PayloadWithoutRateLimitsLeavesUsageUnknown()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);

        var outcome = await IngestAsync(
            store,
            $$"""
              {
                "session_id": "8f2a",
                "workspace": { "current_dir": {{Json(_projectFolder)}}, "project_dir": {{Json(_projectFolder)}} },
                "context_window": { "used_percentage": 8 }
              }
              """,
            Now);

        Assert.AreEqual(ClaudeStatusLineIngestion.NoUsageReported, outcome);
        Assert.IsNull(await store.ReadUsageObservationAsync(AgentProvider.ClaudeCode));
    }

    [TestMethod]
    public async Task PayloadFromAnotherCheckoutIsRefused()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var otherFolder = Path.Combine(_directory, "other");
        Directory.CreateDirectory(otherFolder);

        var outcome = await IngestAsync(store, StatusJson(otherFolder, 5, 5), Now);

        Assert.AreEqual(ClaudeStatusLineIngestion.ForeignProject, outcome);
        Assert.IsNull(await store.ReadUsageObservationAsync(AgentProvider.ClaudeCode));
    }

    [TestMethod]
    public async Task WorkingDirectoryBeneathTheProjectFolderStillBelongsToIt()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var nested = Path.Combine(_projectFolder, "src");
        Directory.CreateDirectory(nested);

        var outcome = await IngestAsync(
            store,
            $$"""
              {
                "cwd": {{Json(nested)}},
                "rate_limits": { "five_hour": { "used_percentage": 4 } }
              }
              """,
            Now);

        Assert.AreEqual(ClaudeStatusLineIngestion.Recorded, outcome);
    }

    [TestMethod]
    public async Task PayloadWithoutAWorkspaceCannotBeAttributedToTheProject()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);

        var outcome = await IngestAsync(
            store,
            """{ "rate_limits": { "five_hour": { "used_percentage": 4 } } }""",
            Now);

        Assert.AreEqual(ClaudeStatusLineIngestion.ForeignProject, outcome);
    }

    [TestMethod]
    public async Task MalformedOrOversizedInputIsRefusedWithoutStoringAnything()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);

        Assert.AreEqual(ClaudeStatusLineIngestion.Malformed, await IngestAsync(store, "not json", Now));
        Assert.AreEqual(ClaudeStatusLineIngestion.Malformed, await IngestAsync(store, "[]", Now));
        Assert.AreEqual(ClaudeStatusLineIngestion.Malformed, await IngestAsync(store, string.Empty, Now));
        Assert.AreEqual(
            ClaudeStatusLineIngestion.Malformed,
            await IngestAsync(
                store,
                new string('a', ClaudeStatusLineUsageIngestor.MaximumInputLength + 1),
                Now));
        Assert.IsNull(await store.ReadUsageObservationAsync(AgentProvider.ClaudeCode));
    }

    [TestMethod]
    public async Task AnOlderObservationNeverReplacesAFresherOne()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);

        Assert.AreEqual(
            ClaudeStatusLineIngestion.Recorded,
            await IngestAsync(store, StatusJson(_projectFolder, 30, 30), Now));
        Assert.AreEqual(
            ClaudeStatusLineIngestion.Superseded,
            await IngestAsync(store, StatusJson(_projectFolder, 1, 1), Now.AddSeconds(-1)));

        var stored = await store.ReadUsageObservationAsync(AgentProvider.ClaudeCode);
        Assert.IsNotNull(stored);
        Assert.AreEqual(30, stored.Windows[0].UsedPercent);

        Assert.AreEqual(
            ClaudeStatusLineIngestion.Recorded,
            await IngestAsync(store, StatusJson(_projectFolder, 55, 60), Now.AddMinutes(1)));
        stored = await store.ReadUsageObservationAsync(AgentProvider.ClaudeCode);
        Assert.IsNotNull(stored);
        Assert.AreEqual(55, stored.Windows[0].UsedPercent);
        Assert.AreEqual(60, stored.Windows[1].UsedPercent);
    }

    [TestMethod]
    public async Task IngestionCannotChangeLeaseOrParticipantState()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);

        Assert.AreEqual(
            ClaudeStatusLineIngestion.Recorded,
            await IngestAsync(store, StatusJson(_projectFolder, 12, 13), Now));

        var state = await store.LoadAsync(_projectId);
        Assert.IsNotNull(state);
        Assert.IsNull(state.Lease);
        var claude = state.Participant(AgentProvider.ClaudeCode);
        Assert.AreEqual(AgentConnectionState.Offline, claude.ConnectionState);
        Assert.AreEqual(AgentTurnState.ClockedOut, claude.TurnState);
        Assert.IsNull(claude.Usage);
        Assert.IsNull(claude.NativeSessionId);
    }

    private async Task<ClaudeStatusLineIngestion> IngestAsync(
        SqliteAgentProjectStore store,
        string json,
        DateTimeOffset observedAt)
    {
        var ingestor = new ClaudeStatusLineUsageIngestor(
            store,
            new ClaudeStatusLineRequest(_projectId, _projectFolder, _databasePath),
            new FixedTimeProvider(observedAt));
        using var reader = new StringReader(json);
        return await ingestor.IngestAsync(reader);
    }

    private static string StatusJson(string folderPath, double fiveHour, double sevenDay) =>
        $$"""
          {
            "session_id": "8f2a4c1e",
            "transcript_path": "C:/Users/example/.claude/projects/session.jsonl",
            "cwd": {{Json(folderPath)}},
            "workspace": {
              "current_dir": {{Json(folderPath)}},
              "project_dir": {{Json(folderPath)}}
            },
            "model": { "id": "claude-opus-5", "display_name": "Opus" },
            "rate_limits": {
              "five_hour": { "used_percentage": {{fiveHour}}, "resets_at": 1738425600 },
              "seven_day": { "used_percentage": {{sevenDay}}, "resets_at": 1738857600 }
            }
          }
          """;

    private static string Json(string value) => System.Text.Json.JsonSerializer.Serialize(value);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
