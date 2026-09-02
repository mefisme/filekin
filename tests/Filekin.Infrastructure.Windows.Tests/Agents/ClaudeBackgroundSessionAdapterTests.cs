using System.Text.Json;
using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.Infrastructure.Windows.Tests.Agents;

[TestClass]
public sealed class ClaudeBackgroundSessionAdapterTests
{
    private static readonly string[] FilekinToolsOnly = ["mcp__filekin"];

    private static readonly string[] AuthStatusArguments = ["auth", "status", "--json"];
    private static readonly string[] StopArguments = ["stop", "7c5dcf5d"];

    private string _directory = null!;
    private string _projectDirectory = null!;
    private string _configurationDirectory = null!;
    private AgentMcpLaunchConfiguration _mcpConfiguration = null!;

    [TestInitialize]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"Filekin-Claude-adapter-{Guid.NewGuid():N}");
        _projectDirectory = Path.Combine(_directory, "project");
        _configurationDirectory = Path.Combine(_directory, "user-config");
        Directory.CreateDirectory(_projectDirectory);
        var projectId = Guid.NewGuid();
        _mcpConfiguration = new AgentMcpLaunchConfiguration(
            AgentProvider.ClaudeCode,
            projectId,
            Path.Combine(_directory, "Filekin.Mcp.exe"),
            _projectDirectory,
            [
                "--project",
                projectId.ToString("D"),
                "--provider",
                "claude",
                "--state-db",
                Path.Combine(_directory, "coordination.db"),
            ]);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestMethod]
    public void LaunchPlanPreviewsSharedCheckoutWithoutWritingProjectSettings()
    {
        var plan = ClaudeBackgroundSessionAdapter.CreateLaunchPlan(
            _projectDirectory,
            "Claude relay",
            "Continue from Filekin's handoff.",
            _mcpConfiguration);

        StringAssert.Contains(plan.ApprovalDescription, "shared checkout");
        Assert.IsFalse(Directory.Exists(Path.Combine(_projectDirectory, ".claude")));

        using var settings = JsonDocument.Parse(plan.SettingsPreviewJson);
        Assert.AreEqual(
            "none",
            settings.RootElement.GetProperty("worktree").GetProperty("bgIsolation").GetString());
        var rateLimitHook = settings.RootElement
            .GetProperty("hooks")
            .GetProperty("StopFailure")[0];
        Assert.AreEqual("rate_limit", rateLimitHook.GetProperty("matcher").GetString());
        var handler = rateLimitHook.GetProperty("hooks")[0];
        Assert.AreEqual("mcp_tool", handler.GetProperty("type").GetString());
        Assert.AreEqual("filekin", handler.GetProperty("server").GetString());
        Assert.AreEqual("filekin_report_usage_limit", handler.GetProperty("tool").GetString());
        Assert.AreEqual(
            "${session_id}",
            handler.GetProperty("input").GetProperty("nativeSessionId").GetString());

        using var configuration = JsonDocument.Parse(plan.McpConfigurationJson);
        var server = configuration.RootElement.GetProperty("mcpServers").GetProperty("filekin");
        Assert.AreEqual("stdio", server.GetProperty("type").GetString());
        Assert.AreEqual(Path.GetFullPath(_mcpConfiguration.ExecutablePath), server.GetProperty("command").GetString());
        Assert.AreEqual("claude", server.GetProperty("args")[3].GetString());
    }

    [TestMethod]
    public void TheOnlyThingAllowedIsFilekinsOwnCoordinationTools()
    {
        var plan = ClaudeBackgroundSessionAdapter.CreateLaunchPlan(
            _projectDirectory,
            "Claude relay",
            "Continue from Filekin's handoff.",
            _mcpConfiguration);

        using var settings = JsonDocument.Parse(plan.SettingsPreviewJson);
        var allowed = settings.RootElement
            .GetProperty("permissions")
            .GetProperty("allow")
            .EnumerateArray()
            .Select(rule => rule.GetString())
            .ToArray();

        // Without this the session stops at a permission prompt before it can clock in. It is not a
        // bypass: only Filekin's own tools are named, and nothing widens file or command permissions.
        CollectionAssert.AreEqual(FilekinToolsOnly, allowed);
        Assert.IsFalse(
            settings.RootElement.TryGetProperty("permissionMode", out _),
            "Filekin never sets a permission mode in the settings it passes.");
        StringAssert.Contains(plan.ApprovalDescription, "coordination tools");
    }

    [TestMethod]
    public void LaunchPlanPreviewsAStatusLineFixedToThisProject()
    {
        var plan = ClaudeBackgroundSessionAdapter.CreateLaunchPlan(
            _projectDirectory,
            "Claude relay",
            "Continue from Filekin's handoff.",
            _mcpConfiguration);

        using var settings = JsonDocument.Parse(plan.SettingsPreviewJson);
        var statusLine = settings.RootElement.GetProperty("statusLine");
        Assert.AreEqual("command", statusLine.GetProperty("type").GetString());
        var command = statusLine.GetProperty("command").GetString();
        Assert.IsNotNull(command);
        Assert.AreEqual(
            ClaudeStatusLineCommand.CreateShellCommand(
                _mcpConfiguration.ExecutablePath,
                new ClaudeStatusLineRequest(
                    _mcpConfiguration.ProjectId,
                    _projectDirectory,
                    _mcpConfiguration.Arguments[5])),
            command);
        StringAssert.Contains(command, _mcpConfiguration.ProjectId.ToString("D"));
        StringAssert.Contains(command, "--provider claude");
        StringAssert.Contains(plan.ApprovalDescription, "status-line");
        Assert.IsFalse(Directory.Exists(Path.Combine(_projectDirectory, ".claude")));
    }

    [TestMethod]
    public void LaunchPlanRejectsNonFilekinOrWrongProjectMcpConfiguration()
    {
        var codex = _mcpConfiguration with { Provider = AgentProvider.Codex };
        var wrongFolder = _mcpConfiguration with { WorkingDirectory = _directory };

        Assert.Throws<ArgumentException>(() => ClaudeBackgroundSessionAdapter.CreateLaunchPlan(
            _projectDirectory,
            "relay",
            "prompt",
            codex));
        Assert.Throws<ArgumentException>(() => ClaudeBackgroundSessionAdapter.CreateLaunchPlan(
            _projectDirectory,
            "relay",
            "prompt",
            wrongFolder));
    }

    [TestMethod]
    public async Task ApprovedLaunchPreflightsSubscriptionAndVerifiesSharedCheckout()
    {
        var runner = new FakeClaudeCliProcessRunner(
            Success("{\"loggedIn\":true,\"authMethod\":\"claude.ai\",\"apiProvider\":\"firstParty\",\"subscriptionType\":\"max\"}"),
            Success("backgrounded · 7c5dcf5d\r\n"),
            Success(SessionJson(_projectDirectory, "blocked", "waiting")));
        var adapter = Adapter(runner);
        var approved = Plan().ApproveSharedCheckout();

        var session = await adapter.LaunchAsync(approved);

        Assert.AreEqual("7c5dcf5d", session.NativeId);
        Assert.AreEqual(ClaudeBackgroundLifecycle.NeedsInput, session.Lifecycle);
        Assert.IsTrue(session.RequiresOwnerAttention);
        Assert.HasCount(3, runner.Calls);
        CollectionAssert.AreEqual(
            AuthStatusArguments,
            runner.Calls[0].Arguments.ToArray());
        Assert.AreEqual("--bg", runner.Calls[1].Arguments[0]);
        CollectionAssert.Contains(runner.Calls[1].Arguments.ToArray(), "--strict-mcp-config");
        var settingsIndex = runner.Calls[1].Arguments.ToList().IndexOf("--settings");
        Assert.IsGreaterThanOrEqualTo(0, settingsIndex);
        using var settings = JsonDocument.Parse(runner.Calls[1].Arguments[settingsIndex + 1]);
        Assert.AreEqual(
            "rate_limit",
            settings.RootElement
                .GetProperty("hooks")
                .GetProperty("StopFailure")[0]
                .GetProperty("matcher")
                .GetString());
        CollectionAssert.DoesNotContain(runner.Calls[1].Arguments.ToArray(), "--dangerously-skip-permissions");
        Assert.AreEqual("Continue from Filekin's handoff.", runner.Calls[1].Arguments[^1]);
        CollectionAssert.AreEqual(
            new[] { "agents", "--json", "--cwd", Path.GetFullPath(_projectDirectory), "--all" },
            runner.Calls[2].Arguments.ToArray());
    }

    [TestMethod]
    public async Task LiveIdleResponseIsNotReportedAsAQuestion()
    {
        var runner = new FakeClaudeCliProcessRunner(
            Success(SessionJson(_projectDirectory, "blocked", "idle", waitingFor: null, processId: 1234)));
        var adapter = Adapter(runner);

        var session = await adapter.ReadAsync(_projectDirectory, "7c5dcf5d");

        Assert.IsNotNull(session);
        Assert.AreEqual(ClaudeBackgroundLifecycle.Idle, session.Lifecycle);
        Assert.IsFalse(session.RequiresOwnerAttention);
    }

    [TestMethod]
    public async Task ACompletedTurnWithALiveProcessIsStillAnIdleSession()
    {
        var runner = new FakeClaudeCliProcessRunner(
            Success(SessionJson(_projectDirectory, "done", "idle", waitingFor: null, processId: 1234)));
        var adapter = Adapter(runner);

        var session = await adapter.ReadAsync(_projectDirectory, "7c5dcf5d");

        Assert.IsNotNull(session);
        Assert.AreEqual(ClaudeBackgroundLifecycle.Idle, session.Lifecycle);
        Assert.AreEqual(1234, session.ProcessId);
        Assert.IsFalse(session.RequiresOwnerAttention);
    }

    [TestMethod]
    public async Task ACompletedTurnWithoutAProcessIsTerminal()
    {
        var runner = new FakeClaudeCliProcessRunner(
            Success(SessionJson(_projectDirectory, "done", "idle", waitingFor: null, processId: null)));
        var adapter = Adapter(runner);

        var session = await adapter.ReadAsync(_projectDirectory, "7c5dcf5d");

        Assert.IsNotNull(session);
        Assert.AreEqual(ClaudeBackgroundLifecycle.Completed, session.Lifecycle);
        Assert.IsNull(session.ProcessId);
    }

    [TestMethod]
    public async Task SpecificWaitingReasonStillRequiresTheOwner()
    {
        var runner = new FakeClaudeCliProcessRunner(
            Success(SessionJson(
                _projectDirectory,
                "blocked",
                "idle",
                waitingFor: "permission prompt",
                processId: 1234)));
        var adapter = Adapter(runner);

        var session = await adapter.ReadAsync(_projectDirectory, "7c5dcf5d");

        Assert.IsNotNull(session);
        Assert.AreEqual(ClaudeBackgroundLifecycle.NeedsInput, session.Lifecycle);
        Assert.IsTrue(session.RequiresOwnerAttention);
    }

    [TestMethod]
    public async Task HistoricalIdleRowWithoutAProcessIsStopped()
    {
        var runner = new FakeClaudeCliProcessRunner(
            Success(SessionJson(_projectDirectory, "blocked", "idle", waitingFor: null, processId: null)));
        var adapter = Adapter(runner);

        var session = await adapter.ReadAsync(_projectDirectory, "7c5dcf5d");

        Assert.IsNotNull(session);
        Assert.AreEqual(ClaudeBackgroundLifecycle.Stopped, session.Lifecycle);
        Assert.IsFalse(session.RequiresOwnerAttention);
    }

    [TestMethod]
    public async Task ChosenModelAndEffortArePassedOnlyToThisLaunch()
    {
        var runner = new FakeClaudeCliProcessRunner(
            Success("{\"loggedIn\":true,\"authMethod\":\"claude.ai\",\"apiProvider\":\"firstParty\",\"subscriptionType\":\"max\"}"),
            Success("backgrounded · 7c5dcf5d\r\n"),
            Success(SessionJson(_projectDirectory, "running", "working")));
        var adapter = Adapter(runner);

        await adapter.LaunchAsync(
            Plan().ApproveSharedCheckout(),
            model: "sonnet",
            effort: "high");

        var arguments = runner.Calls[1].Arguments.ToArray();
        var modelIndex = Array.IndexOf(arguments, "--model");
        var effortIndex = Array.IndexOf(arguments, "--effort");
        Assert.IsGreaterThanOrEqualTo(0, modelIndex);
        Assert.IsGreaterThanOrEqualTo(0, effortIndex);
        Assert.AreEqual("sonnet", arguments[modelIndex + 1]);
        Assert.AreEqual("high", arguments[effortIndex + 1]);
    }

    [TestMethod]
    [DataRow(AgentWorkMode.WorkOnItsOwn, "auto")]
    [DataRow(AgentWorkMode.LookDontTouch, "plan")]
    public async Task ExplicitWorkModeUsesClaudesMatchingPermissionMode(
        AgentWorkMode workMode,
        string expectedPermissionMode)
    {
        var runner = new FakeClaudeCliProcessRunner(
            Success("{\"loggedIn\":true,\"authMethod\":\"claude.ai\",\"apiProvider\":\"firstParty\",\"subscriptionType\":\"max\"}"),
            Success("backgrounded · 7c5dcf5d\r\n"),
            Success(SessionJson(_projectDirectory, "running", "working")));
        var adapter = Adapter(runner);

        await adapter.LaunchAsync(Plan().ApproveSharedCheckout(), workMode);

        var arguments = runner.Calls[1].Arguments.ToArray();
        var permissionModeIndex = Array.IndexOf(arguments, "--permission-mode");
        Assert.IsGreaterThanOrEqualTo(0, permissionModeIndex);
        Assert.AreEqual(expectedPermissionMode, arguments[permissionModeIndex + 1]);
    }

    [TestMethod]
    public async Task AFollowUpTurnResumesTheExistingClaudeConversation()
    {
        var runner = new FakeClaudeCliProcessRunner(
            Success("{\"loggedIn\":true,\"authMethod\":\"claude.ai\",\"apiProvider\":\"firstParty\",\"subscriptionType\":\"max\"}"),
            Success("backgrounded · 7c5dcf5d\r\n"),
            Success(SessionJson(_projectDirectory, "running", "working")));
        var adapter = Adapter(runner);

        var session = await adapter.LaunchAsync(
            Plan().ApproveSharedCheckout(),
            resumeSessionId: "conversation-1");

        var arguments = runner.Calls[1].Arguments.ToArray();
        var resumeIndex = Array.IndexOf(arguments, "--resume");
        Assert.IsGreaterThanOrEqualTo(0, resumeIndex);
        Assert.AreEqual("conversation-1", arguments[resumeIndex + 1]);
        Assert.AreEqual("conversation-1", session.ConversationSessionId);
    }

    [TestMethod]
    public async Task LaunchRefusesUnprovenSubscriptionBeforeBackgroundCommand()
    {
        var runner = new FakeClaudeCliProcessRunner(
            Success("{\"loggedIn\":true,\"authMethod\":\"api_key\",\"apiProvider\":\"firstParty\",\"subscriptionType\":null}"));
        var adapter = Adapter(runner);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.LaunchAsync(Plan().ApproveSharedCheckout()));

        StringAssert.Contains(exception.Message, "subscription mode");
        Assert.HasCount(1, runner.Calls);
    }

    [TestMethod]
    public async Task LaunchStopsSessionThatClaudeReportsInAnotherCheckout()
    {
        var wrongCheckout = Path.Combine(_directory, "worktree");
        var runner = new FakeClaudeCliProcessRunner(
            Success("{\"loggedIn\":true,\"authMethod\":\"claude.ai\",\"apiProvider\":\"firstParty\",\"subscriptionType\":\"pro\"}"),
            Success("backgrounded · 7c5dcf5d"),
            Success(SessionJson(wrongCheckout, "running", "working")),
            Success("stopped 7c5dcf5d"));
        var adapter = Adapter(runner);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.LaunchAsync(Plan().ApproveSharedCheckout()));

        StringAssert.Contains(exception.Message, "shared project checkout");
        Assert.HasCount(4, runner.Calls);
        CollectionAssert.AreEqual(
            StopArguments,
            runner.Calls[3].Arguments.ToArray());
    }

    [TestMethod]
    public async Task FailedValidationSurfacesNativeIdWhenAutomaticStopAlsoFails()
    {
        var runner = new FakeClaudeCliProcessRunner(
            Success("{\"loggedIn\":true,\"authMethod\":\"claude.ai\",\"apiProvider\":\"firstParty\",\"subscriptionType\":\"pro\"}"),
            Success("backgrounded · 7c5dcf5d"),
            Success("[]"),
            new ClaudeCliProcessResult(1, string.Empty, "stop refused"));
        var adapter = Adapter(runner);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.LaunchAsync(Plan().ApproveSharedCheckout()));

        StringAssert.Contains(exception.Message, "7c5dcf5d");
        StringAssert.Contains(exception.Message, "Agent View");
        Assert.IsInstanceOfType<AggregateException>(exception.InnerException);
    }

    private ClaudeBackgroundSessionAdapter Adapter(FakeClaudeCliProcessRunner runner)
    {
        var detector = new ClaudeBillingOverrideDetector(_ => null, _configurationDirectory);
        return new ClaudeBackgroundSessionAdapter(new ClaudeCliClient("claude", detector, runner));
    }

    private ClaudeBackgroundLaunchPlan Plan() =>
        ClaudeBackgroundSessionAdapter.CreateLaunchPlan(
            _projectDirectory,
            "Claude relay",
            "Continue from Filekin's handoff.",
            _mcpConfiguration);

    private static ClaudeCliProcessResult Success(string output) => new(0, output, string.Empty);

    private static string SessionJson(
        string workingDirectory,
        string state,
        string status,
        string? waitingFor = null,
        int? processId = 1234) =>
        JsonSerializer.Serialize(new[]
        {
            new
            {
                id = "7c5dcf5d",
                sessionId = "conversation-1",
                cwd = workingDirectory,
                kind = "background",
                state,
                status,
                waitingFor = waitingFor ?? (state == "blocked" && status != "idle" ? "permission prompt" : null),
                pid = processId,
                startedAt = 1787954400000L,
            },
        });

    private sealed class FakeClaudeCliProcessRunner(params ClaudeCliProcessResult[] results)
        : IClaudeCliProcessRunner
    {
        private readonly Queue<ClaudeCliProcessResult> _results = new(results);

        public List<ClaudeCliCall> Calls { get; } = [];

        public Task<ClaudeCliProcessResult> RunAsync(
            string executable,
            IReadOnlyCollection<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(new ClaudeCliCall(executable, arguments.ToArray(), workingDirectory));
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed record ClaudeCliCall(
        string Executable,
        IReadOnlyList<string> Arguments,
        string WorkingDirectory);
}
