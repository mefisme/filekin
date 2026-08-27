using Filekin.Core.Commands.References;

namespace Filekin.Infrastructure.Windows.Settings;

/// <summary>Where Files should open, and the notice to show when it is not what the user asked for.</summary>
public sealed record StartupLocationResult(string Path, string? Notice);

/// <summary>
/// Turns the durable <c>openFilesAtLaunch</c> preference into an actual folder for one launch.
///
/// A saved-Location target is resolved through the same catalog the sidebar and <c>@name</c>
/// references use, so changing that Location's path changes the next launch destination. A target
/// that cannot be opened right now — a removed Location, a disconnected network share, an unplugged
/// drive — falls back to Home for this launch with a small non-blocking notice, and the preference
/// itself is never rewritten (ARCHITECTURE.md — "Startup Files Location").
/// </summary>
public static class StartupLocationResolver
{
    /// <summary>The current user's profile folder: the default, and the fallback for every failure.</summary>
    public static string HomePath => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static StartupLocationResult Resolve(
        StartupLocation preference,
        INamedLocationResolver savedLocations,
        Func<string, bool>? folderExists = null)
    {
        ArgumentNullException.ThrowIfNull(preference);
        ArgumentNullException.ThrowIfNull(savedLocations);

        var exists = folderExists ?? Directory.Exists;
        var home = HomePath;

        switch (preference.Target)
        {
            case StartupTarget.Location:
                if (preference.Name is not { Length: > 0 } name)
                {
                    return new StartupLocationResult(home, null);
                }

                if (!savedLocations.TryResolve(name, out var locationPath))
                {
                    return new StartupLocationResult(
                        home,
                        $"@{name} is no longer a saved Location. Filekin opened Home.");
                }

                return exists(locationPath)
                    ? new StartupLocationResult(locationPath, null)
                    : new StartupLocationResult(
                        home,
                        $"@{name} ({locationPath}) is not available right now. Filekin opened Home.");

            case StartupTarget.Folder:
                if (preference.Path is not { Length: > 0 } folder)
                {
                    return new StartupLocationResult(home, null);
                }

                return exists(folder)
                    ? new StartupLocationResult(folder, null)
                    : new StartupLocationResult(
                        home,
                        $"{folder} is not available right now. Filekin opened Home.");

            default:
                return new StartupLocationResult(home, null);
        }
    }
}
