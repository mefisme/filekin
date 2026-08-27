using System.Globalization;
using Filekin.Core.FileSystem;
using Filekin.Core.Navigation;

namespace Filekin.App.ViewModels;

/// <summary>Presentation state for one row in the <c>/drives</c> rich view.</summary>
public sealed class DriveItemViewModel(DriveLocation drive)
{
    public DriveLocation Drive { get; } = drive ?? throw new ArgumentNullException(nameof(drive));

    public string Root => Drive.Root;

    public string Label => string.IsNullOrEmpty(Drive.Label) ? "—" : Drive.Label;

    public bool IsAvailable => Drive.IsAvailable;

    public string TypeText => Drive.Kind switch
    {
        DriveKind.Local => "Local",
        DriveKind.Removable => "USB",
        DriveKind.Network => "Network",
        DriveKind.Optical => "Optical",
        _ => "Other",
    };

    /// <summary>Free-of-total for a ready drive, or the concise reason an assigned drive cannot be opened.</summary>
    public string SpaceText
    {
        get
        {
            if (!Drive.IsAvailable)
            {
                // A removable or optical drive that is assigned but not ready is almost always an
                // empty bay or slot; a network mapping is unreachable.
                return Drive.Kind is DriveKind.Removable or DriveKind.Optical ? "No media" : "Unavailable";
            }

            return Drive is { FreeBytes: { } free, TotalBytes: { } total } && total > 0
                ? $"{ByteSize.Format(free)} free of {ByteSize.Format(total)}"
                : "—";
        }
    }

    /// <summary>Whether a usage bar has real capacity behind it.</summary>
    public bool HasUsage => Drive is { IsAvailable: true, FreeBytes: { } free, TotalBytes: { } total } &&
        total > 0 && free <= total;

    /// <summary>Used fraction of the volume, 0 to 1. Only meaningful when <see cref="HasUsage"/>.</summary>
    public double UsageFraction => HasUsage
        ? (Drive.TotalBytes!.Value - Drive.FreeBytes!.Value) / (double)Drive.TotalBytes.Value
        : 0d;

    public string AutomationName => string.Create(
        CultureInfo.CurrentCulture,
        $"{Root} {Label}, {TypeText}, {SpaceText}");
}
