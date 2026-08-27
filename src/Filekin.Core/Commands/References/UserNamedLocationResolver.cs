namespace Filekin.Core.Commands.References;

/// <summary>
/// Resolves the user's ordered, settings-backed Locations. Replacing the set publishes one immutable
/// snapshot so command execution never observes a partially refreshed configuration.
/// </summary>
public sealed class UserNamedLocationResolver : INamedLocationResolver
{
    private Dictionary<string, string> _locations =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public void Replace(IEnumerable<NamedLocation> locations)
    {
        ArgumentNullException.ThrowIfNull(locations);

        var replacement = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var location in locations)
        {
            ArgumentNullException.ThrowIfNull(location);
            replacement.TryAdd(location.Name, location.Path);
        }

        _locations = replacement;
    }

    public bool TryResolve(string name, out string path)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (_locations.TryGetValue(name, out var resolved))
        {
            path = resolved;
            return true;
        }

        path = string.Empty;
        return false;
    }
}
