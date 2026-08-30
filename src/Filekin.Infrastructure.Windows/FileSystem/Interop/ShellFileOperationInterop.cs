using System.Runtime.InteropServices;

namespace Filekin.Infrastructure.Windows.FileSystem.Interop;

/// <summary>
/// Native entry point for Recycle Bin deletion via <c>SHFileOperationW</c>. Field order and the
/// double-null-terminated <c>pFrom</c> buffer follow the official <c>SHFILEOPSTRUCTW</c> contract.
/// Filekin brackets the operation with shell-identity snapshots because this API does not return the
/// newly created Recycle Bin entry.
/// </summary>
internal static partial class ShellFileOperationInterop
{
    internal const uint FoDelete = 0x0003;
    internal const ushort FofSilent = 0x0004;
    internal const ushort FofNoConfirmation = 0x0010;
    internal const ushort FofAllowUndo = 0x0040;
    internal const ushort FofNoConfirmMakeDirectory = 0x0200;
    internal const ushort FofNoErrorUi = 0x0400;
    internal const ushort FofNoUi =
        FofSilent | FofNoConfirmation | FofNoErrorUi | FofNoConfirmMakeDirectory;

    [StructLayout(LayoutKind.Sequential)]
    internal struct ShFileOpStruct
    {
        public IntPtr Window;
        public uint Function;
        public IntPtr From;
        public IntPtr To;
        public ushort Flags;
        public int AnyOperationsAborted;
        public IntPtr NameMappings;
        public IntPtr ProgressTitle;
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHFileOperationW")]
    internal static partial int SHFileOperation(ref ShFileOpStruct fileOperation);
}
