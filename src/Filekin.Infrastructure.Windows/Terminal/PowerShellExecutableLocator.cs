namespace Filekin.Infrastructure.Windows.Terminal;

/// <summary>
/// Resolves the PowerShell executable used as the root process of a hosted terminal.
/// PowerShell 7 (<c>pwsh.exe</c>) is preferred when present; Windows PowerShell
/// (<c>powershell.exe</c>) is the always-available fallback on Windows.
/// </summary>
public static class PowerShellExecutableLocator
{
    public static string Resolve()
    {
        var pwsh = FindOnPath("pwsh.exe");
        if (pwsh is not null)
        {
            return pwsh;
        }

        var windowsPowerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        if (File.Exists(windowsPowerShell))
        {
            return windowsPowerShell;
        }

        throw new InvalidOperationException(
            "No PowerShell executable (pwsh.exe or powershell.exe) was found on this system.");
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (var directory in path.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(directory, fileName);
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry (invalid path characters) is skipped rather than fatal.
                continue;
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
