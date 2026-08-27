namespace Filekin.Core.Navigation;

/// <summary>Enumerates the drives assigned on this machine, including unavailable ones.</summary>
public interface IDrivesProvider
{
    IReadOnlyList<DriveLocation> GetDrives();
}
