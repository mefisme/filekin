using System.Security;
using Filekin.Core.Navigation;

namespace Filekin.Infrastructure.Windows.Navigation;

/// <summary>Reads the drives assigned on this machine, keeping unavailable ones visible.</summary>
public sealed class WindowsDrivesProvider : IDrivesProvider
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    public IReadOnlyList<DriveLocation> GetDrives()
    {
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return [];
        }

        // Name and DriveType are local metadata, but IsReady, VolumeLabel, and the capacity
        // properties can block for seconds while Windows tries to wake a sleeping removable device
        // or reach a disconnected network mapping. Each drive is probed on its own task so one dead
        // mapping cannot hold up the whole view; whatever has not answered within the timeout is
        // reported as unavailable rather than waited on (UX-DESIGN.md — Files · Drives).
        //
        // LongRunning, not Task.Run: these probes block, and a thread-pool probe that is still queued
        // when the timeout expires is indistinguishable from a dead drive. Under load that reported
        // the system drive as unavailable, which is both wrong and alarming. A dedicated thread per
        // drive is the honest cost of putting a wall-clock limit on a blocking call.
        var probes = Array.ConvertAll(
            drives,
            drive => Task.Factory.StartNew(
                () => Probe(drive),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default));
        try
        {
            _ = Task.WaitAll(probes, ProbeTimeout);
        }
        catch (AggregateException)
        {
            // A single failed probe is reported as an unavailable row below.
        }

        var located = new List<DriveLocation>(drives.Length);
        for (var index = 0; index < drives.Length; index++)
        {
            located.Add(probes[index].Status == TaskStatus.RanToCompletion
                ? probes[index].Result
                : Unavailable(drives[index]));
        }

        return [.. located.OrderBy(drive => drive.Root, StringComparer.OrdinalIgnoreCase)];
    }

    private static DriveLocation Probe(DriveInfo drive)
    {
        try
        {
            if (!drive.IsReady)
            {
                return Unavailable(drive);
            }

            return new DriveLocation(
                drive.Name,
                (drive.VolumeLabel ?? string.Empty).Trim(),
                MapKind(drive.DriveType),
                IsAvailable: true,
                drive.AvailableFreeSpace,
                drive.TotalSize);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return Unavailable(drive);
        }
    }

    private static DriveLocation Unavailable(DriveInfo drive) =>
        new(drive.Name, string.Empty, MapKind(drive.DriveType), IsAvailable: false, null, null);

    private static DriveKind MapKind(DriveType type) => type switch
    {
        DriveType.Fixed => DriveKind.Local,
        DriveType.Removable => DriveKind.Removable,
        DriveType.Network => DriveKind.Network,
        DriveType.CDRom => DriveKind.Optical,
        _ => DriveKind.Other,
    };
}
