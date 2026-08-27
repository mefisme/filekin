using System.Runtime.InteropServices;
using Filekin.Infrastructure.Windows.Windowing;

namespace Filekin.Infrastructure.Windows.Tests.Windowing;

[TestClass]
public sealed class SystemThemeNotificationsTests
{
    private const int OtherMessage = 0x001B;

    [TestMethod]
    public void AnImmersiveColorSetChangeIsReported()
    {
        using var area = Area("ImmersiveColorSet");

        Assert.IsTrue(SystemThemeNotifications.IsAppThemeChange(
            SystemThemeNotifications.SettingChangeMessage,
            area.Pointer));
    }

    [TestMethod]
    public void AnotherSettingAreaIsIgnored()
    {
        // WM_SETTINGCHANGE carries dozens of unrelated areas; re-resolving the palette for each one
        // would repaint the window every time a policy or environment variable changed.
        using var area = Area("Environment");

        Assert.IsFalse(SystemThemeNotifications.IsAppThemeChange(
            SystemThemeNotifications.SettingChangeMessage,
            area.Pointer));
    }

    [TestMethod]
    public void AnotherMessageIsIgnored()
    {
        using var area = Area("ImmersiveColorSet");

        Assert.IsFalse(SystemThemeNotifications.IsAppThemeChange(OtherMessage, area.Pointer));
    }

    [TestMethod]
    public void ANullAreaIsIgnored()
    {
        // WM_SETTINGCHANGE is often broadcast with no area string at all.
        Assert.IsFalse(SystemThemeNotifications.IsAppThemeChange(
            SystemThemeNotifications.SettingChangeMessage,
            IntPtr.Zero));
    }

    private static NativeArea Area(string value) => new(Marshal.StringToHGlobalUni(value));

    private sealed class NativeArea(IntPtr pointer) : IDisposable
    {
        public IntPtr Pointer { get; } = pointer;

        public void Dispose() => Marshal.FreeHGlobal(Pointer);
    }
}
