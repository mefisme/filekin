using System.Runtime.InteropServices;

namespace Filekin.Infrastructure.Windows.FileSystem.Interop;

/// <summary>
/// Native entry point for Recycle Bin deletion via <c>SHFileOperationW</c>. Field order and the
/// double-null-terminated <c>pFrom</c> buffer follow the official <c>SHFILEOPSTRUCTW</c>
/// documentation (Microsoft Learn): <c>FO_DELETE</c> with <c>FOF_ALLOWUNDO</c> sends fully-qualified
/// paths to the Recycle Bin, and callers must inspect <c>fAnyOperationsAborted</c> in addition to the
/// return value. (<c>IFileOperation</c> is the modern replacement; <c>SHFileOperationW</c> is used
/// here for a dependency-free version-one implementation.)
/// </summary>
internal static partial class ShellFileOperationInterop
{
    internal const uint FO_DELETE = 0x0003;

    // fFlags values from shellapi.h. FOF_NO_UI is documented as the combination
    // FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_NOCONFIRMMKDIR, which runs the operation
    // with no dialogs; FOF_ALLOWUNDO routes the delete to the Recycle Bin.
    internal const ushort FOF_SILENT = 0x0004;
    internal const ushort FOF_NOCONFIRMATION = 0x0010;
    internal const ushort FOF_ALLOWUNDO = 0x0040;
    internal const ushort FOF_NOCONFIRMMKDIR = 0x0200;
    internal const ushort FOF_NOERRORUI = 0x0400;
    internal const ushort FOF_NO_UI = FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_NOCONFIRMMKDIR;

    [StructLayout(LayoutKind.Sequential)]
    internal struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        public IntPtr pFrom;
        public IntPtr pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public IntPtr lpszProgressTitle;
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHFileOperationW")]
    internal static partial int SHFileOperation(ref ShFileOpStruct fileOp);
}
