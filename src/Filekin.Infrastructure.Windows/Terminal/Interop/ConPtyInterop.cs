using System.Runtime.InteropServices;

namespace Filekin.Infrastructure.Windows.Terminal.Interop;

/// <summary>
/// Native entry points for the Windows Pseudoconsole (ConPTY) lifecycle. The call order and
/// teardown rules follow the official guidance in "Creating a Pseudoconsole session"
/// (Microsoft Learn): synchronous pipes, a pseudoconsole handle passed to CreateProcess via
/// STARTUPINFOEX, and independent draining of the output channel through ClosePseudoConsole.
/// </summary>
internal static partial class ConPtyInterop
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreatePipe(
        out IntPtr hReadPipe,
        out IntPtr hWritePipe,
        IntPtr lpPipeAttributes,
        uint nSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);

    [LibraryImport("kernel32.dll")]
    internal static partial int CreatePseudoConsole(
        Coord size,
        IntPtr hInput,
        IntPtr hOutput,
        uint dwFlags,
        out IntPtr phPC);

    [LibraryImport("kernel32.dll")]
    internal static partial int ResizePseudoConsole(IntPtr hPC, Coord size);

    [LibraryImport("kernel32.dll")]
    internal static partial void ClosePseudoConsole(IntPtr hPC);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList,
        int dwAttributeCount,
        int dwFlags,
        ref nuint lpSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        nuint attribute,
        IntPtr lpValue,
        nuint cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [LibraryImport("kernel32.dll")]
    internal static partial void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreateProcess(
        string? lpApplicationName,
        char[] lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfoEx lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(IntPtr hObject);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct Coord(short x, short y)
{
    public readonly short X = x;
    public readonly short Y = y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct StartupInfo
{
    public int cb;
    public IntPtr lpReserved;
    public IntPtr lpDesktop;
    public IntPtr lpTitle;
    public int dwX;
    public int dwY;
    public int dwXSize;
    public int dwYSize;
    public int dwXCountChars;
    public int dwYCountChars;
    public int dwFillAttribute;
    public int dwFlags;
    public short wShowWindow;
    public short cbReserved2;
    public IntPtr lpReserved2;
    public IntPtr hStdInput;
    public IntPtr hStdOutput;
    public IntPtr hStdError;
}

[StructLayout(LayoutKind.Sequential)]
internal struct StartupInfoEx
{
    public StartupInfo StartupInfo;
    public IntPtr lpAttributeList;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ProcessInformation
{
    public IntPtr hProcess;
    public IntPtr hThread;
    public uint dwProcessId;
    public uint dwThreadId;
}
