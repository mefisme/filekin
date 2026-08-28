using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Filekin.Infrastructure.Windows.Commands;

/// <summary>
/// Writes one value in the current user's Windows environment, in place of
/// <see cref="Environment.SetEnvironmentVariable(string,string,EnvironmentVariableTarget)"/>.
///
/// Two measured problems make the framework method unusable for PATH (DECISIONS.md, 2026-08-28).
///
/// It rewrites the value as <c>REG_SZ</c> whatever it was before. A PATH holding
/// <c>%USERPROFILE%\bin</c> is normally <c>REG_EXPAND_SZ</c>, and once flattened those entries stop
/// expanding — the text survives, the meaning does not. Preserving the existing kind is the whole
/// reason this type exists.
///
/// It also announces the change with <c>SendMessageTimeout</c> without <c>SMTO_ABORTIFHUNG</c>, so
/// every top-level window that is not pumping messages costs the full timeout. Measured on a desktop
/// with 13 such windows: 9 ms for the registry write, then 15–20 seconds inside the announcement.
/// The same broadcast with <c>SMTO_ABORTIFHUNG</c> took 0.7 s.
/// </summary>
internal static partial class WindowsUserEnvironmentWriter
{
    private const string EnvironmentKey = "Environment";

    private static readonly IntPtr HwndBroadcast = 0xFFFF;
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint BroadcastTimeoutMilliseconds = 200;

    /// <summary>
    /// Stores <paramref name="value"/> and tells Windows about it. The stored value kind is the one
    /// already on the value; a variable reference in a brand new value implies <c>REG_EXPAND_SZ</c>.
    /// </summary>
    internal static void Write(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        using (var key = Registry.CurrentUser.OpenSubKey(EnvironmentKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(EnvironmentKey))
        {
            if (value is null)
            {
                key.DeleteValue(name, throwOnMissingValue: false);
            }
            else
            {
                key.SetValue(name, value, KindFor(key, name, value));
            }
        }

        Announce();
    }

    private static RegistryValueKind KindFor(RegistryKey key, string name, string value)
    {
        try
        {
            var existing = key.GetValueKind(name);
            return existing is RegistryValueKind.String or RegistryValueKind.ExpandString
                ? existing
                : DefaultKindFor(value);
        }
        catch (IOException)
        {
            // No such value yet, which is what GetValueKind reports by throwing.
            return DefaultKindFor(value);
        }
    }

    private static RegistryValueKind DefaultKindFor(string value) =>
        value.Contains('%', StringComparison.Ordinal)
            ? RegistryValueKind.ExpandString
            : RegistryValueKind.String;

    /// <summary>
    /// Broadcasts the change the way Windows documents. Most programs ignore it — a running terminal
    /// keeps the environment it started with, which is why a new one is needed to see the change —
    /// but Explorer listens, and programs launched from Explorer afterwards inherit the new value.
    /// Hung windows are skipped rather than waited on.
    /// </summary>
    private static void Announce()
    {
        try
        {
            _ = SendMessageTimeoutW(
                HwndBroadcast,
                WmSettingChange,
                IntPtr.Zero,
                EnvironmentKey,
                SmtoAbortIfHung,
                BroadcastTimeoutMilliseconds,
                out _);
        }
        catch (EntryPointNotFoundException)
        {
            // The value is already stored; a missing announcement only delays when others notice.
        }
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr SendMessageTimeoutW(
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        string lParam,
        uint flags,
        uint timeoutMilliseconds,
        out UIntPtr result);
}
