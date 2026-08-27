using Filekin.Core.Commands.References;
using Filekin.Core.Navigation;
using Filekin.Infrastructure.Windows.References;

namespace Filekin.Infrastructure.Windows.Navigation;

/// <summary>Builds Filekin's short common-folder list followed by Windows-registered cloud roots.</summary>
public sealed class WindowsPlacesProvider : IPlacesProvider
{
    private static readonly (string Name, string Reference)[] CommonPlaces =
    [
        ("Desktop", "desktop"),
        ("Documents", "documents"),
        ("Downloads", "downloads"),
        ("Pictures", "pictures"),
        ("Music", "music"),
        ("Videos", "videos"),
    ];

    private readonly INamedLocationResolver _knownFolders;
    private readonly IRegisteredCloudRootSource _cloudRoots;
    private readonly Func<string, bool> _directoryExists;

    public WindowsPlacesProvider()
        : this(new WindowsKnownFolderLocations(), new WindowsRegisteredCloudRootSource(), Directory.Exists)
    {
    }

    public WindowsPlacesProvider(
        INamedLocationResolver knownFolders,
        IRegisteredCloudRootSource cloudRoots,
        Func<string, bool>? directoryExists = null)
    {
        _knownFolders = knownFolders ?? throw new ArgumentNullException(nameof(knownFolders));
        _cloudRoots = cloudRoots ?? throw new ArgumentNullException(nameof(cloudRoots));
        _directoryExists = directoryExists ?? Directory.Exists;
    }

    public IReadOnlyList<PlaceLocation> GetPlaces()
    {
        var places = new List<PlaceLocation>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, reference) in CommonPlaces)
        {
            if (_knownFolders.TryResolve(reference, out var path))
            {
                TryAdd(places, paths, name, path, PlaceKind.Common);
            }
        }

        foreach (var root in _cloudRoots.GetCurrentUserRoots()
                     .OrderBy(root => root.DisplayName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(root => root.Path, StringComparer.OrdinalIgnoreCase))
        {
            TryAdd(places, paths, root.DisplayName, root.Path, PlaceKind.Cloud);
        }

        return places;
    }

    private void TryAdd(
        List<PlaceLocation> places,
        HashSet<string> paths,
        string name,
        string path,
        PlaceKind kind)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (Path.IsPathFullyQualified(fullPath) && _directoryExists(fullPath) && paths.Add(fullPath))
            {
                places.Add(new PlaceLocation(name.Trim(), fullPath, kind));
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Invalid provider registrations are ignored independently.
        }
    }
}
