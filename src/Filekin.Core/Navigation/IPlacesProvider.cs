namespace Filekin.Core.Navigation;

/// <summary>Discovers the current user's common folders and registered cloud sync roots.</summary>
public interface IPlacesProvider
{
    IReadOnlyList<PlaceLocation> GetPlaces();
}
