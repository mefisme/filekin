using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.Infrastructure.Windows.Tests.Agents;

[TestClass]
public sealed class ClaudeStatusLineCommandTests
{
    private static readonly Guid ProjectId = Guid.ParseExact(
        "3f1d0f52-9a0b-4a2f-9a6b-2c8b5f7b1d44",
        "D");

    [TestMethod]
    public void CommandUsesTheFormBothWindowsStatusLineShellsAccept()
    {
        var command = ClaudeStatusLineCommand.CreateShellCommand(
            @"C:\Program Files\Filekin\Filekin.Mcp.exe",
            new ClaudeStatusLineRequest(
                ProjectId,
                @"D:\github\filekin",
                @"C:\Users\example\AppData\Roaming\Filekin\state.db"));

        // Claude Code runs a status line through Git Bash when it exists and PowerShell otherwise, so
        // the program name stays bare and every path uses forward slashes inside single quotes.
        Assert.AreEqual(
            "powershell -NoProfile -Command \"& 'C:/Program Files/Filekin/Filekin.Mcp.exe' " +
            "--status-line --project 3f1d0f52-9a0b-4a2f-9a6b-2c8b5f7b1d44 --provider claude " +
            "--folder 'D:/github/filekin' " +
            "--state-db 'C:/Users/example/AppData/Roaming/Filekin/state.db'\"",
            command);
        Assert.IsFalse(command.Contains('\\', StringComparison.Ordinal));
    }

    [TestMethod]
    public void CommandRequiresFullyQualifiedQuoteFreePaths()
    {
        var request = new ClaudeStatusLineRequest(ProjectId, @"D:\github\filekin", @"D:\state.db");

        Assert.ThrowsExactly<ArgumentException>(() =>
            ClaudeStatusLineCommand.CreateShellCommand("Filekin.Mcp.exe", request));
        Assert.ThrowsExactly<ArgumentException>(() =>
            ClaudeStatusLineCommand.CreateShellCommand(
                @"D:\tools\Filekin.Mcp.exe",
                request with { ProjectFolderPath = "relative\\folder" }));
        Assert.ThrowsExactly<ArgumentException>(() =>
            ClaudeStatusLineCommand.CreateShellCommand(
                @"D:\tools\Filekin.Mcp.exe",
                request with { ProjectFolderPath = @"D:\it's\a folder" }));
        Assert.ThrowsExactly<ArgumentException>(() =>
            ClaudeStatusLineCommand.CreateShellCommand(
                @"D:\tools\Filekin.Mcp.exe",
                request with { ProjectId = Guid.Empty }));
    }

    [TestMethod]
    public void ArgumentsRoundTripThroughTheFixedForm()
    {
        var request = new ClaudeStatusLineRequest(ProjectId, @"D:\github\filekin", @"D:\state.db");
        var command = ClaudeStatusLineCommand.CreateShellCommand(@"D:\tools\Filekin.Mcp.exe", request);
        var arguments = ExtractArguments(command);

        Assert.IsTrue(ClaudeStatusLineCommand.TryParseArguments(arguments, out var parsed));
        Assert.AreEqual(request.ProjectId, parsed.ProjectId);
        Assert.AreEqual(Path.GetFullPath(request.ProjectFolderPath), parsed.ProjectFolderPath);
        Assert.AreEqual(Path.GetFullPath(request.StateDatabasePath), parsed.StateDatabasePath);
    }

    [TestMethod]
    public void OnlyTheFixedProjectAndProviderArgumentFormIsAccepted()
    {
        string[] valid =
        [
            "--status-line",
            "--project",
            ProjectId.ToString("D"),
            "--provider",
            "claude",
            "--folder",
            @"D:\github\filekin",
            "--state-db",
            @"D:\state.db",
        ];

        Assert.IsTrue(ClaudeStatusLineCommand.TryParseArguments(valid, out _));

        Assert.IsFalse(ClaudeStatusLineCommand.TryParseArguments([], out _));
        Assert.IsFalse(ClaudeStatusLineCommand.TryParseArguments(valid[1..], out _));
        Assert.IsFalse(ClaudeStatusLineCommand.TryParseArguments([.. valid, "--extra"], out _));
        Assert.IsFalse(ClaudeStatusLineCommand.TryParseArguments(Replace(valid, 4, "codex"), out _));
        Assert.IsFalse(ClaudeStatusLineCommand.TryParseArguments(
            Replace(valid, 2, Guid.Empty.ToString("D")),
            out _));
        Assert.IsFalse(ClaudeStatusLineCommand.TryParseArguments(Replace(valid, 2, "not-a-guid"), out _));
        Assert.IsFalse(ClaudeStatusLineCommand.TryParseArguments(Replace(valid, 6, "relative"), out _));
        Assert.IsFalse(ClaudeStatusLineCommand.TryParseArguments(Replace(valid, 8, "relative.db"), out _));
    }

    private static string[] Replace(string[] arguments, int index, string value)
    {
        var copy = arguments.ToArray();
        copy[index] = value;
        return copy;
    }

    /// <summary>Reads the helper arguments back out of the single-quoted PowerShell payload.</summary>
    private static string[] ExtractArguments(string command)
    {
        var payload = command[(command.IndexOf("-Command \"& ", StringComparison.Ordinal) + 12)..^1];
        var arguments = new List<string>();
        var index = 0;
        while (index < payload.Length)
        {
            if (payload[index] == ' ')
            {
                index++;
                continue;
            }

            if (payload[index] == '\'')
            {
                var end = payload.IndexOf('\'', index + 1);
                arguments.Add(payload[(index + 1)..end].Replace('/', Path.DirectorySeparatorChar));
                index = end + 1;
                continue;
            }

            var space = payload.IndexOf(' ', index);
            space = space < 0 ? payload.Length : space;
            arguments.Add(payload[index..space]);
            index = space;
        }

        // The first token is the executable itself.
        return [.. arguments.Skip(1)];
    }
}
