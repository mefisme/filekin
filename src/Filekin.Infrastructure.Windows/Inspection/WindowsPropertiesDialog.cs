using System.Runtime.InteropServices;

namespace Filekin.Infrastructure.Windows.Inspection;

/// <summary>
/// Opens the native Windows Properties dialog for one item. This is the deliberate escape hatch:
/// permissions, ACLs, signatures, compatibility, and ownership stay with Windows instead of being
/// rebuilt inside Filekin (DECISIONS.md, 2026-08-24). It is the one place Filekin shows a system
/// dialog, and only because the user asked for it by name.
///
/// <c>SHObjectProperties</c> rather than <c>ShellExecuteEx</c> with the <c>properties</c> verb: the
/// verb resolves a path through ordinary file-system parsing, which the user profile folder's own
/// properties handler refuses — it fails with <c>ERROR_CANCELLED</c> after showing the shell's own
/// "Unspecified error" box. Measured on 2026-08-27: the verb worked for files, ordinary folders,
/// <c>C:\Users</c>, and <c>C:\</c>, and failed only for <c>C:\Users\&lt;user&gt;</c>;
/// <c>SHObjectProperties</c> worked for all five (DECISIONS.md, 2026-08-27).
/// </summary>
public static partial class WindowsPropertiesDialog
{
    /// <summary>SHOP_FILEPATH — the object is named by a fully qualified filesystem path.</summary>
    private const uint ShopFilePath = 0x00000002;

    /// <summary>
    /// Shows the properties dialog for <paramref name="path"/>. The dialog is owned by Windows and
    /// runs modeless; Filekin does not wait for it.
    /// </summary>
    /// <param name="path">The file or folder to describe.</param>
    /// <param name="ownerWindow">
    /// The window the dialog belongs to, so it cannot be lost behind Filekin. <see cref="IntPtr.Zero"/>
    /// still works and leaves the dialog unowned.
    /// </param>
    /// <exception cref="InvalidOperationException">The shell refused to show the dialog.</exception>
    public static void Show(string path, IntPtr ownerWindow = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!SHObjectProperties(ownerWindow, ShopFilePath, path, null))
        {
            throw new InvalidOperationException($"Windows would not show properties for '{path}'.");
        }
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHObjectProperties", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SHObjectProperties(
        IntPtr window,
        uint objectType,
        string objectName,
        string? propertyPage);
}
