using System.Runtime.InteropServices;

namespace Filekin.Infrastructure.Windows.Windowing;

/// <summary>
/// Recognizes the broadcast Windows sends when the light/dark app mode changes, so a window showing
/// <c>theme: system</c> can re-resolve its palette without the user restarting Filekin.
/// </summary>
public static class SystemThemeNotifications
{
    /// <summary><c>WM_SETTINGCHANGE</c>.</summary>
    public const int SettingChangeMessage = 0x001A;

    // Windows does not publish a dedicated theme message. The app-mode change arrives as
    // WM_SETTINGCHANGE with this area name in lParam, alongside many unrelated setting changes.
    private const string ImmersiveColorSet = "ImmersiveColorSet";

    public static bool IsAppThemeChange(int message, IntPtr lParam)
    {
        if (message != SettingChangeMessage || lParam == IntPtr.Zero)
        {
            return false;
        }

        // lParam is a null-terminated wide string owned by the sender; only read it, never free it.
        var area = Marshal.PtrToStringUni(lParam);
        return string.Equals(area, ImmersiveColorSet, StringComparison.Ordinal);
    }
}
