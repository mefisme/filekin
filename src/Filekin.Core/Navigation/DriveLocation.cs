namespace Filekin.Core.Navigation;

/// <summary>
/// One assigned filesystem drive in Filekin's temporary <c>/drives</c> surface. Capacity is null
/// when the drive is not ready, so an unavailable drive stays visible without inventing numbers.
/// </summary>
public sealed record DriveLocation(
    string Root,
    string Label,
    DriveKind Kind,
    bool IsAvailable,
    long? FreeBytes,
    long? TotalBytes);

public enum DriveKind
{
    Local,
    Removable,
    Network,
    Optical,
    Other,
}
