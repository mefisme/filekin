using Filekin.Core.Discovery;
using Filekin.Infrastructure.Windows.Commands;
using Filekin.Infrastructure.Windows.Inspection.Interop;
using Microsoft.Win32;

namespace Filekin.Infrastructure.Windows.Discovery;

internal sealed record WindowsApplicationRegistration(
    string DisplayName,
    string? ExecutablePath,
    string? InstallLocation,
    string Source);

internal sealed record WindowsApplicationRegistrationOutcome(
    IReadOnlyList<WindowsApplicationRegistration> Registrations,
    int UnreadableLocations);

internal interface IWindowsApplicationRegistrationSource
{
    WindowsApplicationRegistrationOutcome GetRegistrations(CancellationToken cancellationToken);
}

internal sealed class WindowsApplicationRegistrationSource : IWindowsApplicationRegistrationSource
{
    private const string AppPaths = @"Software\Microsoft\Windows\CurrentVersion\App Paths";
    private const string Uninstall = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    public WindowsApplicationRegistrationOutcome GetRegistrations(CancellationToken cancellationToken)
    {
        var results = new List<WindowsApplicationRegistration>();
        var unreadable = 0;
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                cancellationToken.ThrowIfCancellationRequested();
                unreadable += ReadAppPaths(hive, view, results, cancellationToken);
                unreadable += ReadUninstall(hive, view, results, cancellationToken);
            }
        }

        return new WindowsApplicationRegistrationOutcome(results, unreadable);
    }

    private static int ReadAppPaths(
        RegistryHive hive,
        RegistryView view,
        List<WindowsApplicationRegistration> results,
        CancellationToken cancellationToken)
    {
        var unreadable = 0;
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var root = baseKey.OpenSubKey(AppPaths);
            if (root is null)
            {
                return unreadable;
            }

            foreach (var name in root.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var key = root.OpenSubKey(name);
                    var executable = CleanRegisteredPath(
                        key?.GetValue(null, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string);
                    results.Add(new WindowsApplicationRegistration(
                        Path.GetFileNameWithoutExtension(name),
                        executable,
                        executable is null ? null : Path.GetDirectoryName(executable),
                        $"App Paths · {ScopeName(hive)}"));
                }
                catch (Exception ex) when (IsUnavailableRegistration(ex))
                {
                    // A malformed or unavailable registration must not hide its healthy siblings.
                    unreadable++;
                }
            }
        }
        catch (Exception ex) when (IsUnavailableRegistration(ex))
        {
            // One unavailable registry view does not invalidate the others.
            unreadable++;
        }

        return unreadable;
    }

    private static int ReadUninstall(
        RegistryHive hive,
        RegistryView view,
        List<WindowsApplicationRegistration> results,
        CancellationToken cancellationToken)
    {
        var unreadable = 0;
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var root = baseKey.OpenSubKey(Uninstall);
            if (root is null)
            {
                return unreadable;
            }

            foreach (var name in root.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var key = root.OpenSubKey(name);
                    if (key?.GetValue("DisplayName") is not string displayName || string.IsNullOrWhiteSpace(displayName))
                    {
                        continue;
                    }

                    var icon = key.GetValue("DisplayIcon", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                    var location = key.GetValue("InstallLocation", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                    results.Add(new WindowsApplicationRegistration(
                        displayName,
                        CleanRegisteredPath(icon),
                        CleanRegisteredPath(location),
                        $"Installed app · {ScopeName(hive)}"));
                }
                catch (Exception ex) when (IsUnavailableRegistration(ex))
                {
                    // A malformed or unavailable registration must not hide its healthy siblings.
                    unreadable++;
                }
            }
        }
        catch (Exception ex) when (IsUnavailableRegistration(ex))
        {
            // One unavailable registry view does not invalidate the others.
            unreadable++;
        }

        return unreadable;
    }

    private static string? CleanRegisteredPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = value.Trim().Trim('"');
        var iconIndex = cleaned.LastIndexOf(',');
        if (iconIndex > 2 && int.TryParse(cleaned[(iconIndex + 1)..], out _))
        {
            cleaned = cleaned[..iconIndex].Trim().Trim('"');
        }

        return cleaned;
    }

    private static string ScopeName(RegistryHive hive) =>
        hive == RegistryHive.CurrentUser ? "User" : "Machine";

    private static bool IsUnavailableRegistration(Exception exception) =>
        exception is ArgumentException or IOException or UnauthorizedAccessException or
            System.Security.SecurityException;
}

internal interface IWindowsShortcutSource
{
    WindowsShortcutEnumerationOutcome GetShortcutPaths(CancellationToken cancellationToken);

    string? TryGetTarget(string shortcutPath, out bool unreadable);
}

internal sealed record WindowsShortcutEnumerationOutcome(
    IReadOnlyList<string> ShortcutPaths,
    int UnreadableLocations);

internal sealed class WindowsStartMenuShortcutSource : IWindowsShortcutSource
{
    public WindowsShortcutEnumerationOutcome GetShortcutPaths(CancellationToken cancellationToken)
    {
        var shortcuts = new List<string>();
        var unreadable = 0;
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                 }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            try
            {
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                };
                foreach (var shortcut in Directory.EnumerateFiles(root, "*.lnk", options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    shortcuts.Add(shortcut);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // The other Start Menu root and every other discovery source remain useful.
                unreadable++;
            }
        }

        return new WindowsShortcutEnumerationOutcome(shortcuts, unreadable);
    }

    public string? TryGetTarget(string shortcutPath, out bool unreadable)
    {
        var details = ShellLinkInterop.TryRead(shortcutPath);
        unreadable = details is null;
        return details?.Target;
    }
}

internal sealed record WindowsWherePathValues(
    string? ProcessPath,
    string? UserPath,
    string? MachinePath,
    IReadOnlySet<string> PathExtensions)
{
    public static WindowsWherePathValues Current() => new(
        Environment.GetEnvironmentVariable("Path"),
        Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User),
        Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine),
        ParseExtensions(Environment.GetEnvironmentVariable("PATHEXT")));

    public IEnumerable<(string Directory, string Source)> ConfiguredDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (value, source) in new[]
                 {
                     (UserPath, "PATH · User"),
                     (MachinePath, "PATH · Machine"),
                     (ProcessPath, "PATH · Current process"),
                 })
        {
            foreach (var raw in (value ?? string.Empty).Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (WindowsEnvironmentPath.NormalizeDirectory(raw) is { } directory &&
                    Directory.Exists(directory) && seen.Add(directory))
                {
                    yield return (directory, source);
                }
            }
        }
    }

    public WherePathScope ScopeOf(string directory)
    {
        var scope = WherePathScope.None;
        if (WindowsEnvironmentPath.ContainsDirectory(ProcessPath, directory))
        {
            scope |= WherePathScope.Process;
        }

        if (WindowsEnvironmentPath.ContainsDirectory(UserPath, directory))
        {
            scope |= WherePathScope.User;
        }

        if (WindowsEnvironmentPath.ContainsDirectory(MachinePath, directory))
        {
            scope |= WherePathScope.Machine;
        }

        return scope;
    }

    private static HashSet<string> ParseExtensions(string? value)
    {
        var extensions = (value ?? ".COM;.EXE;.BAT;.CMD")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static extension => extension.StartsWith('.') ? extension : "." + extension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return extensions;
    }
}
