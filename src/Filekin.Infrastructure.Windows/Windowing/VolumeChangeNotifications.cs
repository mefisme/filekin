using System.Runtime.InteropServices;

namespace Filekin.Infrastructure.Windows.Windowing;

/// <summary>
/// Recognizes the broadcast Windows sends when a drive letter appears or disappears, so a view of
/// the assigned drives can re-enumerate instead of showing a stale row.
/// </summary>
public static class VolumeChangeNotifications
{
    /// <summary><c>WM_DEVICECHANGE</c>.</summary>
    public const int DeviceChangeMessage = 0x0219;

    private const int DeviceArrival = 0x8000;          // DBT_DEVICEARRIVAL
    private const int DeviceRemoveComplete = 0x8004;   // DBT_DEVICEREMOVECOMPLETE
    private const int VolumeDeviceType = 2;            // DBT_DEVTYP_VOLUME

    // DEV_BROADCAST_HDR is { DWORD dbch_size; DWORD dbch_devicetype; DWORD dbch_reserved; }, so the
    // device type sits one DWORD in.
    private const int DeviceTypeOffset = sizeof(int);

    /// <summary>
    /// Whether this <c>WM_DEVICECHANGE</c> reports a volume arriving or leaving.
    /// </summary>
    /// <remarks>
    /// Windows broadcasts volume events to every top-level window, so no <c>RegisterDeviceNotification</c>
    /// call is needed. This covers USB storage, memory cards, media inserted into an existing optical
    /// drive, and drive letters being mapped or unmapped. A device that never receives a drive letter —
    /// a phone connected over MTP, for example — is not a volume, does not appear in
    /// <see cref="System.IO.DriveInfo.GetDrives"/>, and is deliberately not reported here.
    /// </remarks>
    public static bool IsVolumeChange(IntPtr wParam, IntPtr lParam)
    {
        // Read the pointer-sized wParam widened rather than truncated; the event codes fit in 32 bits
        // but the explicit narrowing conversion is not safe to write unchecked (CA2020).
        var eventType = wParam.ToInt64();
        if (eventType is not (DeviceArrival or DeviceRemoveComplete) || lParam == IntPtr.Zero)
        {
            return false;
        }

        return Marshal.ReadInt32(lParam, DeviceTypeOffset) == VolumeDeviceType;
    }
}
