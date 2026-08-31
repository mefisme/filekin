using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.Infrastructure.Windows.Tests.Agents;

[TestClass]
public sealed class ClaudeAgentUsageSourceTests
{
    private const string SubscriptionAccountJson =
        """
        { "loggedIn": true, "authMethod": "claude.ai", "apiProvider": "firstParty", "subscriptionType": "max" }
        """;

    private const string ApiBilledAccountJson =
        """
        { "loggedIn": true, "authMethod": "apiKey", "apiProvider": "anthropic" }
        """;

    private static readonly DateTimeOffset Now = new(2026, 8, 30, 10, 0, 0, TimeSpan.Zero);

    private string _projectFolder = null!;

    [TestInitialize]
    public void SetUp()
    {
        _projectFolder = Path.Combine(Path.GetTempPath(), $"Filekin-claude-usage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectFolder);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_projectFolder))
        {
            Directory.Delete(_projectFolder, recursive: true);
        }
    }

    [TestMethod]
    public async Task StoredStatusLineObservationsBecomeTheProjectsClaudeUsage()
    {
        var projectId = Guid.NewGuid();
        var observations = new FakeObservationStore();
        var source = CreateSource(observations, projectId, SubscriptionAccountJson, SubscriptionAccountJson);

        Assert.IsFalse((await source.ReadAsync()).IsKnown);

        observations.Store(
            projectId,
            new AgentUsageSnapshot(
                AgentProvider.ClaudeCode,
                Now,
                [
                    new AgentUsageWindow("claude:five_hour", 23.5, TimeSpan.FromHours(5), null),
                    new AgentUsageWindow("claude:seven_day", 41.2, TimeSpan.FromDays(7), null),
                ]));

        var snapshot = await source.ReadAsync();

        Assert.IsTrue(snapshot.IsKnown);
        Assert.AreEqual(Now, snapshot.ObservedAt);
        Assert.AreEqual(100 - 41.2, snapshot.MinimumRemainingPercent);
    }

    [TestMethod]
    public async Task AnotherProjectsObservationIsNeverRead()
    {
        var projectId = Guid.NewGuid();
        var observations = new FakeObservationStore();
        observations.Store(
            Guid.NewGuid(),
            new AgentUsageSnapshot(
                AgentProvider.ClaudeCode,
                Now,
                [new AgentUsageWindow("claude:five_hour", 5, TimeSpan.FromHours(5), null)]));
        var source = CreateSource(observations, projectId, SubscriptionAccountJson);

        Assert.IsFalse((await source.ReadAsync()).IsKnown);
    }

    [TestMethod]
    public async Task ApiBilledClaudeIsRefusedBeforeAnyObservationIsRead()
    {
        var observations = new FakeObservationStore();
        var source = CreateSource(observations, Guid.NewGuid(), ApiBilledAccountJson);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => source.ReadAsync());
        Assert.AreEqual(0, observations.ReadCount);
    }

    private ClaudeAgentUsageSource CreateSource(
        IAgentUsageObservationStore observations,
        Guid projectId,
        params string[] accountResponses) =>
        new(
            new ClaudeCliClient(
                "claude",
                new ClaudeBillingOverrideDetector(),
                new FakeClaudeCliProcessRunner(accountResponses)),
            _projectFolder,
            observations,
            projectId);

    private sealed class FakeObservationStore : IAgentUsageObservationStore
    {
        private readonly Dictionary<(Guid ProjectId, AgentProvider Provider), AgentUsageSnapshot> _stored = [];

        public int ReadCount { get; private set; }

        public void Store(Guid projectId, AgentUsageSnapshot observation) =>
            _stored[(projectId, observation.Provider)] = observation;

        public Task<bool> RecordUsageObservationAsync(
            Guid projectId,
            AgentUsageSnapshot observation,
            CancellationToken cancellationToken = default)
        {
            Store(projectId, observation);
            return Task.FromResult(true);
        }

        public Task<AgentUsageSnapshot?> ReadUsageObservationAsync(
            Guid projectId,
            AgentProvider provider,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult(_stored.GetValueOrDefault((projectId, provider)));
        }
    }

    private sealed class FakeClaudeCliProcessRunner(params string[] responses) : IClaudeCliProcessRunner
    {
        private readonly Queue<string> _responses = new(responses);

        public Task<ClaudeCliProcessResult> RunAsync(
            string executable,
            IReadOnlyCollection<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ClaudeCliProcessResult(0, _responses.Dequeue(), string.Empty));
        }
    }
}
