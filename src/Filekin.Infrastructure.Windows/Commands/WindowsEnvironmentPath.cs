namespace Filekin.Infrastructure.Windows.Commands;

/// <summary>
/// Builds the effective executable search path from Filekin's inherited process environment plus
/// the current machine/user values. A desktop process can outlive a PATH update, so inheriting its
/// original block alone makes newly installed command-line tools invisible until Filekin restarts.
/// </summary>
public static class WindowsEnvironmentPath
{
    public static string GetCurrent() => Merge(
        Environment.GetEnvironmentVariable("PATH"),
        Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
        Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User));

    /// <summary>The persistent machine and current-user lists, excluding process-only changes.</summary>
    public static string GetConfigured() => Merge(
        Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
        Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User));

    public static string Merge(params string?[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<string>();
        foreach (var value in values)
        {
            foreach (var entry in (value ?? string.Empty).Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var expanded = Environment.ExpandEnvironmentVariables(entry.Trim('"'));
                if (expanded.Length > 0 && seen.Add(expanded))
                {
                    entries.Add(expanded);
                }
            }
        }

        return string.Join(Path.PathSeparator, entries);
    }

    /// <summary>
    /// Reports which Windows environment layers already contain <paramref name="directory"/>.
    /// Comparisons expand variables, remove harmless quotes/trailing separators, and ignore case.
    /// </summary>
    public static Core.Discovery.WherePathScope ScopeOf(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var scope = Core.Discovery.WherePathScope.None;
        if (ContainsDirectory(Environment.GetEnvironmentVariable("PATH"), directory))
        {
            scope |= Core.Discovery.WherePathScope.Process;
        }

        if (ContainsDirectory(
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
                directory))
        {
            scope |= Core.Discovery.WherePathScope.User;
        }

        if (ContainsDirectory(
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
                directory))
        {
            scope |= Core.Discovery.WherePathScope.Machine;
        }

        return scope;
    }

    internal static bool ContainsDirectory(string? pathValue, string directory)
    {
        var wanted = NormalizeDirectory(directory);
        if (wanted is null)
        {
            return false;
        }

        return (pathValue ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeDirectory)
            .Any(entry => entry is not null && string.Equals(entry, wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Removes every normalized entry present in <paramref name="entriesToRemove"/>.</summary>
    internal static string Without(string? value, string? entriesToRemove)
    {
        var removed = (entriesToRemove ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeDirectory)
            .Where(static entry => entry is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var kept = (value ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeDirectory)
            .Where(entry => entry is not null && !removed.Contains(entry));
        return string.Join(Path.PathSeparator, kept!);
    }

    internal static string? NormalizeDirectory(string value)
    {
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
            return expanded.Length == 0
                ? null
                : Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
