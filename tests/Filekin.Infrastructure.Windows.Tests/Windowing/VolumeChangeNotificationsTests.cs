using System.Runtime.InteropServices;
using Filekin.Infrastructure.Windows.Windowing;

namespace Filekin.Infrastructure.Windows.Tests.Windowing;

[TestClass]
public sealed class VolumeChangeNotificationsTests
{
    private const int DeviceArrival = 0x8000;
    private const int DeviceRemoveComplete = 0x8004;
    private const int DeviceQueryRemove = 0x8001;
    private const int VolumeDeviceType = 2;
    private const int PortDeviceType = 3;

    [TestMethod]
    public void VolumeArrivalAndRemovalAreReported()
    {
        using var header = DeviceBroadcastHeader(VolumeDeviceType);

        Assert.IsTrue(VolumeChangeNotifications.IsVolumeChange(DeviceArrival, header.Pointer));
        Assert.IsTrue(VolumeChangeNotifications.IsVolumeChange(DeviceRemoveComplete, header.Pointer));
    }

    [TestMethod]
    public void OtherDeviceTypesAreIgnored()
    {
        // A serial port announcing itself must not make Filekin re-enumerate drives.
        using var header = DeviceBroadcastHeader(PortDeviceType);

        Assert.IsFalse(VolumeChangeNotifications.IsVolumeChange(DeviceArrival, header.Pointer));
    }

    [TestMethod]
    public void OtherEventTypesAreIgnored()
    {
        // A remove *query* is a request, not a completed change; acting on it would re-enumerate
        // while the volume is still present.
        using var header = DeviceBroadcastHeader(VolumeDeviceType);

        Assert.IsFalse(VolumeChangeNotifications.IsVolumeChange(DeviceQueryRemove, header.Pointer));
    }

    [TestMethod]
    public void ANullPayloadIsIgnored()
    {
        // Windows sends event types such as DBT_DEVNODES_CHANGED with no DEV_BROADCAST_HDR at all.
        Assert.IsFalse(VolumeChangeNotifications.IsVolumeChange(DeviceArrival, IntPtr.Zero));
    }

    /// <summary>Builds a DEV_BROADCAST_HDR: size, device type, reserved.</summary>
    private static Header DeviceBroadcastHeader(int deviceType)
    {
        var block = Marshal.AllocHGlobal(sizeof(int) * 3);
        Marshal.WriteInt32(block, 0, sizeof(int) * 3);
        Marshal.WriteInt32(block, sizeof(int), deviceType);
        Marshal.WriteInt32(block, sizeof(int) * 2, 0);
        return new Header(block);
    }

    private sealed class Header(IntPtr pointer) : IDisposable
    {
        public IntPtr Pointer { get; } = pointer;

        public void Dispose() => Marshal.FreeHGlobal(Pointer);
    }
}
