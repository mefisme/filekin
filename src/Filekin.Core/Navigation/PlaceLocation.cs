namespace Filekin.Core.Navigation;

/// <summary>A directly navigable destination in Filekin's temporary <c>/places</c> surface.</summary>
public sealed record PlaceLocation(string Name, string Path, PlaceKind Kind);

public enum PlaceKind
{
    Common,
    Cloud,
}
