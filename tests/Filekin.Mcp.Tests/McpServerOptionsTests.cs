using Filekin.Core.Agents;

namespace Filekin.Mcp.Tests;

[TestClass]
public sealed class McpServerOptionsTests
{
    [TestMethod]
    public void ParseFixesProjectProviderAndDatabaseIdentity()
    {
        var projectId = Guid.NewGuid();
        var databasePath = Path.GetFullPath("agent-state.db");

        var options = McpServerOptions.Parse(
            [
                "--project",
                projectId.ToString("D"),
                "--provider",
                "claude-code",
                "--state-db",
                databasePath,
            ]);

        Assert.AreEqual(projectId, options.ProjectId);
        Assert.AreEqual(AgentProvider.ClaudeCode, options.Provider);
        Assert.AreEqual(databasePath, options.StateDatabasePath);
    }

    [TestMethod]
    public void ParseRejectsMissingDuplicateAndUnknownIdentityOptions()
    {
        Assert.Throws<ArgumentException>(() => McpServerOptions.Parse([]));
        Assert.Throws<ArgumentException>(
            () => McpServerOptions.Parse(
                ["--project", Guid.NewGuid().ToString(), "--provider", "other"]));
        Assert.Throws<ArgumentException>(
            () => McpServerOptions.Parse(
                [
                    "--project",
                    Guid.NewGuid().ToString(),
                    "--project",
                    Guid.NewGuid().ToString(),
                    "--provider",
                    "codex",
                ]));
        Assert.Throws<ArgumentException>(
            () => McpServerOptions.Parse(["--unexpected", "value"]));
        Assert.Throws<ArgumentException>(
            () => McpServerOptions.Parse(
                [
                    "--project",
                    Guid.NewGuid().ToString(),
                    "--provider",
                    "codex",
                    "--state-db",
                    "relative.db",
                ]));
    }

    [TestMethod]
    public void HandoffReasonUsesStableExternalNames()
    {
        Assert.AreEqual(
            AgentHandoffReason.WorkCompleted,
            FilekinAgentTools.ParseReason("work_completed"));
        Assert.AreEqual(
            AgentHandoffReason.UsageThreshold,
            FilekinAgentTools.ParseReason("usage_threshold"));
        Assert.AreEqual(
            AgentHandoffReason.UserRequested,
            FilekinAgentTools.ParseReason("user_requested"));
        Assert.Throws<ArgumentException>(() => FilekinAgentTools.ParseReason("0"));
    }
}
