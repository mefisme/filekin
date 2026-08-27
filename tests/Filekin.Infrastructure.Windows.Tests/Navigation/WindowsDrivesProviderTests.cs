using Filekin.Core.Navigation;
using Filekin.Infrastructure.Windows.Navigation;

namespace Filekin.Infrastructure.Windows.Tests.Navigation;

/// <summary>
/// <see cref="DriveInfo"/> cannot be faked, so these run against the real machine and assert the
/// shape the <c>/drives</c> surface depends on rather than a fixed set of drives.
/// </summary>
[TestClass]
public sealed class WindowsDrivesProviderTests
{
    [TestMethod]
    public void DrivesAreSortedByRootAndIncludeTheSystemDrive()
    {
        var drives = new WindowsDrivesProvider().GetDrives();

        var roots = drives.Select(drive => drive.Root).ToArray();
        CollectionAssert.AreEqual(
            roots.OrderBy(root => root, StringComparer.OrdinalIgnoreCase).ToArray(),
            roots);

        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
        Assert.IsTrue(
            drives.Any(drive =>
                string.Equals(drive.Root, systemRoot, StringComparison.OrdinalIgnoreCase) && drive.IsAvailable),
            $"The system drive {systemRoot} should be listed as available.");
    }

    [TestMethod]
    public void CapacityIsPresentExactlyWhenTheDriveIsAvailable()
    {
        var drives = new WindowsDrivesProvider().GetDrives();

        foreach (var drive in drives)
        {
            Assert.IsTrue(Path.IsPathFullyQualified(drive.Root), $"{drive.Root} should be fully qualified.");

            if (drive.IsAvailable)
            {
                // A ready volume always reports capacity; free space can never exceed the total.
                Assert.IsNotNull(drive.TotalBytes, $"{drive.Root} is available but reports no capacity.");
                Assert.IsNotNull(drive.FreeBytes, $"{drive.Root} is available but reports no free space.");
                Assert.IsTrue(drive.FreeBytes <= drive.TotalBytes, $"{drive.Root} reports more free than total.");
            }
            else
            {
                // An unavailable drive stays visible, but Filekin must not invent numbers for it.
                Assert.IsNull(drive.TotalBytes, $"{drive.Root} is unavailable but reports a capacity.");
                Assert.IsNull(drive.FreeBytes, $"{drive.Root} is unavailable but reports free space.");
                Assert.AreEqual(string.Empty, drive.Label);
            }
        }
    }

    [TestMethod]
    public void EveryAssignedDriveIsReportedExactlyOnce()
    {
        var drives = new WindowsDrivesProvider().GetDrives();

        var assigned = DriveInfo.GetDrives().Select(drive => drive.Name).ToArray();
        CollectionAssert.AreEquivalent(assigned, drives.Select(drive => drive.Root).ToArray());
        Assert.AreEqual(drives.Count, drives.Select(drive => drive.Root).Distinct().Count());
    }

    [TestMethod]
    public void KindIsNeverGuessedForAnAvailableFixedDrive()
    {
        var drives = new WindowsDrivesProvider().GetDrives();

        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
        var system = drives.Single(drive =>
            string.Equals(drive.Root, systemRoot, StringComparison.OrdinalIgnoreCase));

        Assert.AreEqual(DriveKind.Local, system.Kind);
    }
}
