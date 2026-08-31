using System.Globalization;

namespace Filekin.Infrastructure.Windows.Agents;

/// <summary>
/// The project and provider a Filekin status-line helper process is fixed to. Like the MCP identity,
/// it comes from the command line at launch and is never taken from the status-line input.
/// </summary>
public sealed record ClaudeStatusLineRequest(
    Guid ProjectId,
    string ProjectFolderPath,
    string StateDatabasePath);

/// <summary>
/// Builds and parses the fixed command line for Filekin's Claude status-line helper. Claude Code runs
/// a status-line command through Git Bash when it is installed and through PowerShell otherwise, so
/// the command uses the one form both shells accept: a bare <c>powershell</c> program name, forward
/// slashes in every path, and quoting that survives both parsers.
/// </summary>
public static class ClaudeStatusLineCommand
{
    public const string ModeArgument = "--status-line";

    public static string CreateShellCommand(string executablePath, ClaudeStatusLineRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(request);
        if (request.ProjectId == Guid.Empty)
        {
            throw new ArgumentException("The agent project id cannot be empty.", nameof(request));
        }

        var executable = ShellPath(executablePath, nameof(executablePath));
        var folder = ShellPath(request.ProjectFolderPath, nameof(request));
        var stateDatabase = ShellPath(request.StateDatabasePath, nameof(request));
        var inner = string.Create(
            CultureInfo.InvariantCulture,
            $"& {executable} {ModeArgument} --project {request.ProjectId:D} --provider claude --folder {folder} --state-db {stateDatabase}");
        return $"powershell -NoProfile -Command \"{inner}\"";
    }

    /// <summary>
    /// Reads the fixed argument form back. It accepts nothing else, so the helper cannot be pointed at
    /// another project, another provider, or another state database by a longer command line.
    /// </summary>
    public static bool TryParseArguments(
        IReadOnlyList<string> arguments,
        out ClaudeStatusLineRequest request)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        request = null!;
        if (arguments.Count != 9 ||
            arguments[0] != ModeArgument ||
            arguments[1] != "--project" ||
            !Guid.TryParseExact(arguments[2], "D", out var projectId) ||
            projectId == Guid.Empty ||
            arguments[3] != "--provider" ||
            arguments[4] != "claude" ||
            arguments[5] != "--folder" ||
            !Path.IsPathFullyQualified(arguments[6]) ||
            arguments[7] != "--state-db" ||
            !Path.IsPathFullyQualified(arguments[8]))
        {
            return false;
        }

        request = new ClaudeStatusLineRequest(
            projectId,
            Path.GetFullPath(arguments[6]),
            Path.GetFullPath(arguments[8]));
        return true;
    }

    private static string ShellPath(string path, string parameterName)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"'{path}' must be a fully qualified path.", parameterName);
        }

        var fullPath = Path.GetFullPath(path).Replace('\\', '/');
        if (fullPath.Contains('\'', StringComparison.Ordinal) ||
            fullPath.Contains('"', StringComparison.Ordinal) ||
            fullPath.AsSpan().ContainsAny('\r', '\n'))
        {
            throw new ArgumentException(
                $"'{path}' cannot be used in a Claude status-line command because it contains a quote or line break.",
                parameterName);
        }

        return $"'{fullPath}'";
    }
}
