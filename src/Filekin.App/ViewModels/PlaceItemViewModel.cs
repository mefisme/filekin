using Filekin.Core.Navigation;

namespace Filekin.App.ViewModels;

/// <summary>Presentation state for one direct-navigation row in the <c>/places</c> rich view.</summary>
public sealed class PlaceItemViewModel(PlaceLocation place, bool startsSection)
{
    public PlaceLocation Place { get; } = place ?? throw new ArgumentNullException(nameof(place));

    public string Name => Place.Name;

    public string Path => Place.Path;

    // Segoe MDL2 Assets: ED25 is the folder, E753 the cloud. E8B7 is a page, not a folder.
    public string Symbol => Place.Kind == PlaceKind.Cloud ? "\uE753" : "\uED25";

    public string SectionTitle => Place.Kind == PlaceKind.Cloud ? "CLOUD" : "COMMON";

    public bool StartsSection { get; } = startsSection;

    public string AutomationName => $"{Name}, {Path}";
}
