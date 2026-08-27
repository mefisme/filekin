using System.Runtime.InteropServices;

namespace Filekin.Infrastructure.Windows.Inspection.Interop;

/// <summary>
/// Reads what a Windows <c>.lnk</c> shortcut points at. Filekin only ever <em>reveals</em> a
/// shortcut; editing one stays with the native Windows Properties dialog (DECISIONS.md, 2026-08-27),
/// so nothing here has a setter path and <c>IShellLink::Resolve</c> is never called — Resolve can
/// show UI and search the network for a missing target.
/// </summary>
internal static class ShellLinkInterop
{
    /// <summary>SLGP_RAWPATH — the stored path, without environment expansion or resolution.</summary>
    private const uint SlgpRawPath = 0x4;

    private const int MaxPath = 260;
    private const int MaxArguments = 1024;

    /// <summary>Reads a shortcut, or returns <c>null</c> when it cannot be read as one.</summary>
    internal static ShellLinkDetails? TryRead(string path)
    {
        object? instance = null;
        try
        {
            instance = new ShellLinkCoClass();
            var link = (IShellLinkW)instance;
            ((IPersistFile)instance).Load(path, 0);

            return new ShellLinkDetails(
                ReadBuffer(MaxPath, buffer => link.GetPath(buffer, buffer.Length, IntPtr.Zero, SlgpRawPath)),
                ReadBuffer(MaxArguments, buffer => link.GetArguments(buffer, buffer.Length)),
                ReadBuffer(MaxPath, buffer => link.GetWorkingDirectory(buffer, buffer.Length)));
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
        finally
        {
            if (instance is not null && Marshal.IsComObject(instance))
            {
                _ = Marshal.ReleaseComObject(instance);
            }
        }
    }

    private static string? ReadBuffer(int size, Action<char[]> read)
    {
        var buffer = new char[size];
        read(buffer);
        var end = Array.IndexOf(buffer, '\0');
        var value = new string(buffer, 0, end < 0 ? buffer.Length : end);
        return value.Length == 0 ? null : value;
    }
}

/// <summary>What a shortcut points at. Any field may be absent.</summary>
internal sealed record ShellLinkDetails(string? Target, string? Arguments, string? WorkingDirectory);

[ComImport]
[Guid("00021401-0000-0000-C000-000000000046")]
internal sealed class ShellLinkCoClass
{
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal interface IShellLinkW
{
    void GetPath([Out][MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[] file, int maxPath, IntPtr findData, uint flags);

    void GetIDList(out IntPtr idList);

    void SetIDList(IntPtr idList);

    void GetDescription([Out][MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[] name, int maxName);

    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

    void GetWorkingDirectory([Out][MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[] directory, int maxPath);

    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);

    void GetArguments([Out][MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[] arguments, int maxArguments);

    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);

    void GetHotkey(out short hotkey);

    void SetHotkey(short hotkey);

    void GetShowCmd(out int showCommand);

    void SetShowCmd(int showCommand);

    void GetIconLocation([Out][MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[] iconPath, int maxPath, out int iconIndex);

    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);

    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);

    void Resolve(IntPtr window, uint flags);

    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("0000010b-0000-0000-C000-000000000046")]
internal interface IPersistFile
{
    void GetClassID(out Guid classId);

    [PreserveSig]
    int IsDirty();

    void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);

    void Save([MarshalAs(UnmanagedType.LPWStr)] string? fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);

    void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);

    void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
}
