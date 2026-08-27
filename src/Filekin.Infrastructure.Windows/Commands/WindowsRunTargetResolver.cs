using System.Reflection.PortableExecutable;
using Filekin.Core.Commands;

namespace Filekin.Infrastructure.Windows.Commands;

/// <summary>
/// Resolves a <c>/run</c> target from the current Files folder and then PATH, and classifies concrete
/// Windows console images/scripts for hosted-terminal execution. This is deterministic metadata,
/// not a runtime heuristic: GUI images and associated documents remain external launches.
/// </summary>
public sealed class WindowsRunTargetResolver
{
    private static readonly string[] TerminalScriptExtensions =
        [".bat", ".cmd", ".com", ".ps1", ".py"];

    private readonly IInteractiveCommandRegistry _interactiveCommands;
    private readonly Func<string[]> _pathDirectories;
    private readonly string[] _executableExtensions;

    public WindowsRunTargetResolver(IInteractiveCommandRegistry interactiveCommands)
    {
        ArgumentNullException.ThrowIfNull(interactiveCommands);
        _interactiveCommands = interactiveCommands;
        _pathDirectories = static () => SplitPath(WindowsEnvironmentPath.GetCurrent());
        _executableExtensions = SplitExtensions(Environment.GetEnvironmentVariable("PATHEXT"));
    }

    public WindowsRunTargetResolver(
        IInteractiveCommandRegistry interactiveCommands,
        string? path,
        string? pathExtensions)
    {
        ArgumentNullException.ThrowIfNull(interactiveCommands);
        _interactiveCommands = interactiveCommands;
        var fixedPathDirectories = SplitPath(path);
        _pathDirectories = () => fixedPathDirectories;
        _executableExtensions = SplitExtensions(pathExtensions);
    }

    public RunTargetResolution Resolve(
        string target,
        IReadOnlyList<string> arguments,
        string currentFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentFolderPath);

        var resolvedPath = FindExistingTarget(target, currentFolderPath);
        if (resolvedPath is not null && Directory.Exists(resolvedPath))
        {
            return new RunTargetResolution(
                target,
                resolvedPath,
                Path.GetFileName(resolvedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                RunTargetKind.Directory,
                FoundOnDisk: true);
        }

        var launchTarget = resolvedPath ?? target;
        var commandName = NormalizeCommandName(launchTarget);
        var displayName = Path.GetFileName(launchTarget);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = target;
        }

        var kind = _interactiveCommands.IsInteractive(commandName, arguments) ||
                   IsTerminalFile(resolvedPath)
            ? RunTargetKind.Terminal
            : RunTargetKind.External;

        return new RunTargetResolution(target, launchTarget, displayName, kind, resolvedPath is not null);
    }

    /// <summary>
    /// Whether a finite raw-shell command is a concrete console program that can use the delayed
    /// terminal-relaunch offer. PowerShell cmdlets/functions do not resolve here and are not prompted.
    /// </summary>
    public bool IsTerminalCommand(string executable, string currentFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentFolderPath);
        return IsTerminalFile(FindExistingTarget(executable, currentFolderPath));
    }

    private string? FindExistingTarget(string target, string currentFolderPath)
    {
        try
        {
            if (Path.IsPathFullyQualified(target))
            {
                return FindCandidate(target);
            }

            var local = FindCandidate(Path.Combine(currentFolderPath, target));
            if (local is not null)
            {
                return local;
            }

            if (target.Contains(Path.DirectorySeparatorChar) ||
                target.Contains(Path.AltDirectorySeparatorChar))
            {
                return null;
            }

            foreach (var directory in _pathDirectories())
            {
                var candidate = FindCandidate(Path.Combine(directory, target));
                if (candidate is not null)
                {
                    return candidate;
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return null;
        }

        return null;
    }

    private string? FindCandidate(string candidate)
    {
        var fullPath = Path.GetFullPath(candidate);
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            return fullPath;
        }

        if (Path.HasExtension(fullPath))
        {
            return null;
        }

        foreach (var extension in _executableExtensions)
        {
            var withExtension = fullPath + extension;
            if (File.Exists(withExtension))
            {
                return withExtension;
            }
        }

        return null;
    }

    private static bool IsTerminalFile(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        if (TerminalScriptExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new PEReader(stream);
            return reader.PEHeaders.PEHeader?.Subsystem == Subsystem.WindowsCui;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException)
        {
            return false;
        }
    }

    private static string NormalizeCommandName(string target)
    {
        var fileName = Path.GetFileNameWithoutExtension(target);
        return string.IsNullOrWhiteSpace(fileName) ? target : fileName;
    }

    private static string[] SplitPath(string? path) =>
        (path ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Environment.ExpandEnvironmentVariables)
            .Select(static value => value.Trim('"'))
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string[] SplitExtensions(string? extensions)
    {
        var values = (extensions ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static extension => extension.StartsWith('.') ? extension : "." + extension)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return values.Length == 0 ? [".COM", ".EXE", ".BAT", ".CMD"] : values;
    }
}
