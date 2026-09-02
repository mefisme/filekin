using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.Infrastructure.Windows.Tests.Agents;

[TestClass]
public sealed class AgentSessionAttachCommandTests
{
    private static readonly Guid Project = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private const string ClaudeSession = "e6bc70ea-b9ba-4ded-894f-1a6652b478b1";
    private const string CodexThread = "01a05b44-2b6a-7773-b338-bfb1c4cefa2b";

    private static AgentMcpLaunchConfiguration CodexIdentity(
        string executablePath = @"C:\Program Files\Filekin\Filekin.Mcp.exe",
        string workingDirectory = @"D:\GitHub\agent-test") =>
        new(
            AgentProvider.Codex,
            Project,
            executablePath,
            workingDirectory,
            [
                "--project",
                Project.ToString("D"),
                "--provider",
                "codex",
                "--state-db",
                @"C:\Users\someone\AppData\Roaming\Filekin\state.db",
            ]);

    [TestMethod]
    public void ClaudeAttachesToTheProcessItAlreadyStarted()
    {
        // Attach opens the running background process, which still holds the --mcp-config Filekin
        // gave it, so nothing about the coordination server has to be repeated here.
        var command = AgentSessionAttachCommand.Create(
            AgentProvider.ClaudeCode,
            ClaudeSession,
            out var refusal);

        Assert.AreEqual($"claude attach {ClaudeSession}", command);
        Assert.AreEqual(AgentSessionAttachRefusal.None, refusal);
    }

    [TestMethod]
    public void ClaudeNeedsNoCoordinationIdentity() =>
        Assert.IsNotNull(
            AgentSessionAttachCommand.Create(AgentProvider.ClaudeCode, ClaudeSession, out _),
            "Claude keeps the MCP configuration of the process attach opens.");

    [TestMethod]
    public void CodexResumeCarriesTheCoordinationServerBack()
    {
        // A resumed Codex process reads only the user's own config.toml, and Filekin never writes
        // its server there, so without these overrides the session would have the conversation and
        // none of the coordination tools.
        var command = AgentSessionAttachCommand.Create(
            AgentProvider.Codex,
            CodexThread,
            out var refusal,
            CodexIdentity());

        Assert.AreEqual(AgentSessionAttachRefusal.None, refusal);
        Assert.IsNotNull(command);
        StringAssert.StartsWith(command, "codex resume --config ");
        StringAssert.EndsWith(command, $" {CodexThread}", "Codex reads the id positionally, after its options.");
        StringAssert.Contains(command, $"mcp_servers.filekin_coordination_{Project:N}.command=");
        StringAssert.Contains(command, "Filekin.Mcp.exe");
        StringAssert.Contains(command, "filekin_clock_in");
        StringAssert.Contains(command, ".required=true");
    }

    [TestMethod]
    public void CodexOverridesMatchTheAppServerExactly()
    {
        // One session must not get a different coordination server depending on which door it came
        // through, so both paths render the same overrides from the same source.
        var command = AgentSessionAttachCommand.Create(
            AgentProvider.Codex,
            CodexThread,
            out _,
            CodexIdentity());

        foreach (var expected in CodexAppServerLaunchPlan.CoordinationConfigOverrides(
            CodexAppServerLaunchPlan.Normalize(CodexIdentity())))
        {
            StringAssert.Contains(command, expected, $"The resumed session is missing {expected}.");
        }
    }

    [TestMethod]
    public void CodexOverridesArePowerShellLiterals()
    {
        // The overrides carry TOML strings whose quotes and backslashes must reach Codex unchanged.
        var command = AgentSessionAttachCommand.Create(
            AgentProvider.Codex,
            CodexThread,
            out _,
            CodexIdentity());

        StringAssert.Contains(command, "--config 'mcp_servers.");
        Assert.AreEqual(
            0,
            command!.Count(character => character == '\'') % 2,
            "Every quoted override must be closed.");
    }

    [TestMethod]
    public void CodexWithoutItsCoordinationIdentityIsRefused()
    {
        Assert.IsNull(
            AgentSessionAttachCommand.Create(AgentProvider.Codex, CodexThread, out var refusal),
            "Resuming Codex without the coordination server would leave it unable to clock in.");
        Assert.AreEqual(AgentSessionAttachRefusal.MissingCoordinationIdentity, refusal);
    }

    [TestMethod]
    public void ClaudesIdentityIsNotAcceptedForCodex()
    {
        var claudeIdentity = CodexIdentity() with { Provider = AgentProvider.ClaudeCode };

        Assert.IsNull(
            AgentSessionAttachCommand.Create(AgentProvider.Codex, CodexThread, out var refusal, claudeIdentity));
        Assert.AreEqual(AgentSessionAttachRefusal.MissingCoordinationIdentity, refusal);
    }

    [TestMethod]
    public void ALiveCodexThreadIsNotResumedIntoASecondCopy()
    {
        // Claude's attach is built for a live session. Codex resume starts a new process, so doing
        // it while Filekin's App Server still holds the thread would fork one conversation in two.
        Assert.IsNull(
            AgentSessionAttachCommand.Create(
                AgentProvider.Codex,
                CodexThread,
                out var refusal,
                CodexIdentity(),
                codexThreadIsLive: true));
        Assert.AreEqual(AgentSessionAttachRefusal.LiveCodexThread, refusal);
    }

    [TestMethod]
    public void ALiveSessionIsExactlyWhatClaudeAttachIsFor() =>
        Assert.IsNotNull(
            AgentSessionAttachCommand.Create(
                AgentProvider.ClaudeCode,
                ClaudeSession,
                out _,
                coordinationIdentity: null,
                codexThreadIsLive: true),
            "The live-thread rule is a Codex rule and must not touch Claude.");

    [TestMethod]
    public void SurroundingWhitespaceIsNotPartOfTheId() =>
        Assert.AreEqual(
            $"claude attach {ClaudeSession}",
            AgentSessionAttachCommand.Create(AgentProvider.ClaudeCode, $"  {ClaudeSession}\t", out _));

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void NoSessionMeansNoCommand(string? nativeSessionId)
    {
        Assert.IsNull(
            AgentSessionAttachCommand.Create(AgentProvider.ClaudeCode, nativeSessionId, out var refusal));
        Assert.AreEqual(AgentSessionAttachRefusal.NoSession, refusal);
    }

    [TestMethod]
    // A session id reaches Filekin from a provider and leaves it on a command line, so anything
    // that is not the shape both providers emit is refused rather than quoted.
    [DataRow("e6bc70ea; Remove-Item C:\\ -Recurse")]
    [DataRow("e6bc70ea b9ba")]
    [DataRow("$(whoami)")]
    [DataRow("e6bc70ea`nwhoami")]
    [DataRow("e6bc70ea'; whoami; '")]
    [DataRow("../../etc/passwd")]
    [DataRow("short")]
    [DataRow("-not-a-leading-dash")]
    [DataRow("zzzzzzzz-b9ba-4ded-894f-1a6652b478b1")]
    public void AnIdFilekinWillNotPutOnACommandLineIsRefused(string nativeSessionId)
    {
        Assert.IsNull(
            AgentSessionAttachCommand.Create(AgentProvider.ClaudeCode, nativeSessionId, out var claudeRefusal),
            "Claude must refuse an id it cannot recognise.");
        Assert.AreEqual(AgentSessionAttachRefusal.UnrecognizedSessionId, claudeRefusal);

        Assert.IsNull(
            AgentSessionAttachCommand.Create(
                AgentProvider.Codex,
                nativeSessionId,
                out var codexRefusal,
                CodexIdentity()),
            "Codex must refuse an id it cannot recognise.");
        Assert.AreEqual(AgentSessionAttachRefusal.UnrecognizedSessionId, codexRefusal);
    }

    [TestMethod]
    public void EveryRefusalSaysSomething()
    {
        foreach (var refusal in Enum.GetValues<AgentSessionAttachRefusal>())
        {
            var explanation = AgentSessionAttachCommand.Explain(AgentProvider.Codex, refusal);
            if (refusal == AgentSessionAttachRefusal.None)
            {
                Assert.AreEqual(string.Empty, explanation);
                continue;
            }

            Assert.IsFalse(
                string.IsNullOrWhiteSpace(explanation),
                $"{refusal} reaches a person and must be explained.");
        }
    }

    [TestMethod]
    public void TheTerminalTitleNamesTheProviderAndFolder() =>
        Assert.AreEqual(
            "Claude Code CLI · agent-test",
            AgentSessionAttachCommand.Title(AgentProvider.ClaudeCode, @"D:\GitHub\agent-test"));

    [TestMethod]
    public void ATrailingSeparatorDoesNotEmptyTheTitle() =>
        Assert.AreEqual(
            "Codex CLI · agent-test",
            AgentSessionAttachCommand.Title(AgentProvider.Codex, @"D:\GitHub\agent-test\"));
}
