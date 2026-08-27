using System.Security;
using Microsoft.Win32;

namespace Filekin.Infrastructure.Windows.Theming;

/// <summary>
/// Reads the Windows "app mode" preference that <c>theme: system</c> follows. This is the same value
/// the Settings app writes when the user picks Light or Dark for apps, and the value that changes
/// under an automatic light/dark schedule.
/// </summary>
public static class WindowsAppTheme
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    /// <summary>
    /// Whether Windows is currently asking apps to render light. When the value cannot be read at
    /// all, this reports dark: Filekin's own default theme is dark, so an unreadable system
    /// preference lands on the same appearance as no preference at all.
    /// </summary>
    public static bool PrefersLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue(AppsUseLightThemeValue) is int value && value != 0;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
