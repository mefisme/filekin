using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.Infrastructure.Windows.Tests.Agents;

[TestClass]
public sealed class CodexAppServerLaunchPlanTests
{
    [TestMethod]
    public void CoordinationPlanAddsARequiredProjectFixedMcpWithoutPolicyOverrides()
    {
        var projectId = Guid.Parse("7F7BFCB0-9D10-46E9-BCEA-D76066AC17B2");
        var executable = Path.GetFullPath(Path.Combine("tools", "Filekin MCP.exe"));
        var folder = Path.GetFullPath(Path.Combine("projects", "Filekin test"));
        var mcp = new AgentMcpLaunchConfiguration(
            AgentProvider.Codex,
            projectId,
            executable,
            folder,
            Array.AsReadOnly(new[]
            {
                "--project",
                projectId.ToString("D"),
                "--provider",
                "codex",
                "--state-db",
                Path.GetFullPath(Path.Combine("state", "state.db")),
            }));

        var plan = CodexAppServerLaunchPlan.CreateCoordination(mcp);

        Assert.AreEqual("codex", plan.ExecutablePath);
        Assert.AreEqual("app-server", plan.Arguments[0]);
        Assert.AreEqual("--stdio", plan.Arguments[1]);
        var overrides = ReadOverrides(plan.Arguments);
        var prefix = $"mcp_servers.filekin_coordination_{projectId:N}";
        Assert.AreEqual(System.Text.Json.JsonSerializer.Serialize(executable), overrides[$"{prefix}.command"]);
        StringAssert.Contains(overrides[$"{prefix}.args"], projectId.ToString("D"));
        Assert.AreEqual(System.Text.Json.JsonSerializer.Serialize(folder), overrides[$"{prefix}.cwd"]);
        Assert.AreEqual("true", overrides[$"{prefix}.enabled"]);
        Assert.AreEqual("true", overrides[$"{prefix}.required"]);
        StringAssert.Contains(overrides[$"{prefix}.enabled_tools"], "filekin_submit_handoff");
        Assert.IsFalse(overrides.Keys.Any(key => key.Contains("approval", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(overrides.Keys.Any(key => key.Contains("sandbox", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void CoordinationPlanRejectsAnotherProviderIdentity()
    {
        var mcp = new AgentMcpLaunchConfiguration(
            AgentProvider.ClaudeCode,
            Guid.NewGuid(),
            Path.GetFullPath("Filekin.Mcp.exe"),
            Path.GetFullPath("project"),
            Array.Empty<string>());

        Assert.Throws<ArgumentException>(
            () => CodexAppServerLaunchPlan.CreateCoordination(mcp));
    }

    [TestMethod]
    public async Task UnboundInspectionClientCannotStartACodexTurn()
    {
        await using var client = new CodexAppServerClient();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.StartThreadAsync(Path.GetFullPath("project")));

        StringAssert.Contains(exception.Message, "fixed project/provider");
    }

    [TestMethod]
    public async Task BoundClientRefusesAnotherProjectFolderBeforeStartingAProcess()
    {
        var projectId = Guid.NewGuid();
        var mcp = new AgentMcpLaunchConfiguration(
            AgentProvider.Codex,
            projectId,
            Path.GetFullPath("Filekin.Mcp.exe"),
            Path.GetFullPath("project-a"),
            Array.Empty<string>());
        await using var client = new CodexAppServerClient(mcp, "missing-codex-for-test.exe");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.StartThreadAsync(Path.GetFullPath("project-b")));

        StringAssert.Contains(exception.Message, "does not match");
    }

    private static Dictionary<string, string> ReadOverrides(IReadOnlyList<string> arguments)
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 2; index < arguments.Count; index += 2)
        {
            Assert.AreEqual("--config", arguments[index]);
            var assignment = arguments[index + 1];
            var separator = assignment.IndexOf('=');
            Assert.IsGreaterThan(0, separator);
            overrides.Add(assignment[..separator], assignment[(separator + 1)..]);
        }

        return overrides;
    }
}
