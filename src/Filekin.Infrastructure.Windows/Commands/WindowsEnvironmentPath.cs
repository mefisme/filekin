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
}
