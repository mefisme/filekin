using System.Security;
using Filekin.Core.Navigation;

namespace Filekin.Infrastructure.Windows.Navigation;

/// <summary>Reads the drives assigned on this machine, keeping unavailable ones visible.</summary>
public sealed class WindowsDrivesProvider : IDrivesProvider
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    private readonly object _probeGate = new();
    private readonly Dictionary<string, Task<DriveLocation>> _activeProbes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<IReadOnlyList<WindowsDriveProbe>> _enumerate;
    private readonly TimeSpan _probeTimeout;

    public WindowsDrivesProvider()
        : this(DefaultProbes, ProbeTimeout)
    {
    }

    internal WindowsDrivesProvider(
        Func<IReadOnlyList<WindowsDriveProbe>> enumerate,
        TimeSpan probeTimeout)
    {
        _enumerate = enumerate ?? throw new ArgumentNullException(nameof(enumerate));
        ArgumentOutOfRangeException.ThrowIfLessThan(probeTimeout, TimeSpan.Zero);
        _probeTimeout = probeTimeout;
    }

    public IReadOnlyList<DriveLocation> GetDrives()
    {
        IReadOnlyList<WindowsDriveProbe> drives;
        try
        {
            drives = _enumerate();
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
        Task<DriveLocation>[] probes;
        lock (_probeGate)
        {
            probes = new Task<DriveLocation>[drives.Count];
            for (var index = 0; index < drives.Count; index++)
            {
                var drive = drives[index];
                if (!_activeProbes.TryGetValue(drive.Root, out var probe) || probe.IsCompleted)
                {
                    probe = Task.Factory.StartNew(
                        drive.Probe,
                        CancellationToken.None,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default);
                    _activeProbes[drive.Root] = probe;
                }

                probes[index] = probe;
            }

            var assigned = drives.Select(static drive => drive.Root).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var stale in _activeProbes
                         .Where(pair => pair.Value.IsCompleted && !assigned.Contains(pair.Key))
                         .Select(static pair => pair.Key)
                         .ToArray())
            {
                _activeProbes.Remove(stale);
            }
        }

        try
        {
            _ = Task.WaitAll(probes, _probeTimeout);
        }
        catch (AggregateException)
        {
            // A single failed probe is reported as an unavailable row below.
        }

        var located = new List<DriveLocation>(drives.Count);
        for (var index = 0; index < drives.Count; index++)
        {
            located.Add(probes[index].Status == TaskStatus.RanToCompletion
                ? probes[index].Result
                : Unavailable(drives[index].Root, drives[index].DriveType));
        }

        lock (_probeGate)
        {
            foreach (var completed in _activeProbes
                         .Where(static pair => pair.Value.IsCompleted)
                         .Select(static pair => pair.Key)
                         .ToArray())
            {
                _activeProbes.Remove(completed);
            }
        }

        return [.. located.OrderBy(drive => drive.Root, StringComparer.OrdinalIgnoreCase)];
    }

    private static IReadOnlyList<WindowsDriveProbe> DefaultProbes() =>
        [.. DriveInfo.GetDrives().Select(drive => new WindowsDriveProbe(
            drive.Name,
            drive.DriveType,
            () => Probe(drive)))];

    private static DriveLocation Probe(DriveInfo drive)
    {
        try
        {
            if (!drive.IsReady)
            {
                return Unavailable(drive.Name, drive.DriveType);
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
            return Unavailable(drive.Name, drive.DriveType);
        }
    }

    private static DriveLocation Unavailable(string root, DriveType driveType) =>
        new(root, string.Empty, MapKind(driveType), IsAvailable: false, null, null);

    private static DriveKind MapKind(DriveType type) => type switch
    {
        DriveType.Fixed => DriveKind.Local,
        DriveType.Removable => DriveKind.Removable,
        DriveType.Network => DriveKind.Network,
        DriveType.CDRom => DriveKind.Optical,
        _ => DriveKind.Other,
    };
}

internal sealed record WindowsDriveProbe(
    string Root,
    DriveType DriveType,
    Func<DriveLocation> Probe);
