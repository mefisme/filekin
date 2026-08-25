using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Filekin.ShellTerminalSpike;

internal sealed class ConPtySession : IAsyncDisposable
{
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint ProcThreadAttributePseudoConsole = 0x00020016;
    private const uint HandleFlagInherit = 0x00000001;
    private const int StartfUseStdHandles = 0x00000100;

    private readonly object _outputGate = new();
    private readonly StringBuilder _capturedOutput = new();
    private readonly FileStream _input;
    private readonly FileStream _output;
    private readonly Task _outputPump;
    private readonly Process _rootProcess;
    private IntPtr _pseudoConsole;
    private bool _disposed;

    private ConPtySession(
        IntPtr pseudoConsole,
        SafeFileHandle inputWrite,
        SafeFileHandle outputRead,
        Process rootProcess,
        bool mirrorOutput)
    {
        _pseudoConsole = pseudoConsole;
        // CreatePipe produces synchronous handles, as required by the ConPTY API contract.
        // ReadAsync/WriteAsync still keep blocking pipe work off the caller through FileStream's fallback.
        _input = new FileStream(inputWrite, FileAccess.Write, 4096, isAsync: false);
        _output = new FileStream(outputRead, FileAccess.Read, 4096, isAsync: false);
        _rootProcess = rootProcess;
        _outputPump = Task.Run(() => PumpOutputAsync(mirrorOutput));
    }

    public int RootProcessId => _rootProcess.Id;

    public static ConPtySession StartPowerShell(
        string powerShellExecutable,
        string initialDirectory,
        short columns = 80,
        short rows = 24,
        bool mirrorOutput = false)
    {
        if (!NativeMethods.CreatePipe(out var inputReadRaw, out var inputWriteRaw, IntPtr.Zero, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe(input) failed.");
        }

        if (!NativeMethods.CreatePipe(out var outputReadRaw, out var outputWriteRaw, IntPtr.Zero, 0))
        {
            NativeMethods.CloseHandle(inputReadRaw);
            NativeMethods.CloseHandle(inputWriteRaw);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe(output) failed.");
        }

        // The host owns these two ends. Explicitly prevent accidental inheritance.
        NativeMethods.SetHandleInformation(inputWriteRaw, HandleFlagInherit, 0);
        NativeMethods.SetHandleInformation(outputReadRaw, HandleFlagInherit, 0);

        var createResult = NativeMethods.CreatePseudoConsole(
            new Coord(columns, rows),
            inputReadRaw,
            outputWriteRaw,
            0,
            out var pseudoConsole);

        NativeMethods.CloseHandle(inputReadRaw);
        NativeMethods.CloseHandle(outputWriteRaw);

        if (createResult < 0)
        {
            NativeMethods.CloseHandle(inputWriteRaw);
            NativeMethods.CloseHandle(outputReadRaw);
            Marshal.ThrowExceptionForHR(createResult);
        }

        IntPtr attributeList = IntPtr.Zero;
        var size = IntPtr.Zero;
        NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        attributeList = Marshal.AllocHGlobal(size);

        try
        {
            if (!NativeMethods.InitializeProcThreadAttributeList(attributeList, 1, 0, ref size))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "InitializeProcThreadAttributeList failed.");
            }

            if (!NativeMethods.UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    (IntPtr)ProcThreadAttributePseudoConsole,
                    pseudoConsole,
                    (IntPtr)IntPtr.Size,
                    IntPtr.Zero,
                    IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "UpdateProcThreadAttribute failed.");
            }

            var startupInfo = new StartupInfoEx();
            startupInfo.StartupInfo.cb = Marshal.SizeOf<StartupInfoEx>();
            // Without STARTF_USESTDHANDLES, Windows can duplicate redirected parent stdio
            // into a console child even when it is also given a pseudoconsole attribute.
            // Null std handles plus this flag force stdio to be established through ConPTY.
            startupInfo.StartupInfo.dwFlags = StartfUseStdHandles;
            startupInfo.lpAttributeList = attributeList;

            var escapedDirectory = initialDirectory.Replace("'", "''", StringComparison.Ordinal);
            var commandLine = new StringBuilder(
                $"\"{powerShellExecutable}\" -NoLogo -NoProfile -NoExit -Command \"Set-PSReadLineOption -HistorySaveStyle SaveNothing -ErrorAction SilentlyContinue; Set-Location -LiteralPath '{escapedDirectory}'; Write-Output '__CONPTY_READY__'\"");

            if (!NativeMethods.CreateProcess(
                    null,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    ExtendedStartupInfoPresent | CreateUnicodeEnvironment,
                    IntPtr.Zero,
                    initialDirectory,
                    ref startupInfo,
                    out var processInfo))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessW failed.");
            }

            try
            {
                var process = Process.GetProcessById((int)processInfo.dwProcessId);
                return new ConPtySession(
                    pseudoConsole,
                    new SafeFileHandle(inputWriteRaw, ownsHandle: true),
                    new SafeFileHandle(outputReadRaw, ownsHandle: true),
                    process,
                    mirrorOutput);
            }
            finally
            {
                NativeMethods.CloseHandle(processInfo.hThread);
                NativeMethods.CloseHandle(processInfo.hProcess);
            }
        }
        catch
        {
            NativeMethods.ClosePseudoConsole(pseudoConsole);
            NativeMethods.CloseHandle(inputWriteRaw);
            NativeMethods.CloseHandle(outputReadRaw);
            throw;
        }
        finally
        {
            if (attributeList != IntPtr.Zero)
            {
                NativeMethods.DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }
        }
    }

    public async Task WriteAsync(string text, CancellationToken cancellationToken = default)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await _input.WriteAsync(bytes, cancellationToken);
        await _input.FlushAsync(cancellationToken);
    }

    public void Resize(short columns, short rows)
    {
        var result = NativeMethods.ResizePseudoConsole(_pseudoConsole, new Coord(columns, rows));
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    public async Task<bool> WaitForTextAsync(string expected, TimeSpan timeout)
    {
        var stopAt = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < stopAt)
        {
            lock (_outputGate)
            {
                if (_capturedOutput.ToString().Contains(expected, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            await Task.Delay(40);
        }

        return false;
    }

    public async Task<bool> WaitForRootExitAsync(TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await _rootProcess.WaitForExitAsync(cancellation.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public string GetCapturedOutput()
    {
        lock (_outputGate)
        {
            return _capturedOutput.ToString();
        }
    }

    private async Task PumpOutputAsync(bool mirrorOutput)
    {
        var buffer = new byte[4096];
        try
        {
            while (true)
            {
                var count = await _output.ReadAsync(buffer);
                if (count == 0)
                {
                    break;
                }

                var text = Encoding.UTF8.GetString(buffer, 0, count);
                lock (_outputGate)
                {
                    _capturedOutput.Append(text);
                }

                if (mirrorOutput)
                {
                    Console.Write(text);
                }
            }
        }
        catch (Exception) when (_disposed)
        {
            // Expected when disposal closes a pipe while the drain task is completing.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _input.DisposeAsync();

        if (_pseudoConsole != IntPtr.Zero)
        {
            NativeMethods.ClosePseudoConsole(_pseudoConsole);
            _pseudoConsole = IntPtr.Zero;
        }

        try
        {
            await _outputPump.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            // The process boundary is already closed by ClosePseudoConsole; do not block teardown indefinitely.
        }

        await _output.DisposeAsync();
        _rootProcess.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Coord(short x, short y)
    {
        public readonly short X = x;
        public readonly short Y = y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreatePipe(out IntPtr readPipe, out IntPtr writePipe, IntPtr pipeAttributes, uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

        [DllImport("kernel32.dll")]
        internal static extern int CreatePseudoConsole(Coord size, IntPtr input, IntPtr output, uint flags, out IntPtr pseudoConsole);

        [DllImport("kernel32.dll")]
        internal static extern int ResizePseudoConsole(IntPtr pseudoConsole, Coord size);

        [DllImport("kernel32.dll")]
        internal static extern void ClosePseudoConsole(IntPtr pseudoConsole);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool InitializeProcThreadAttributeList(IntPtr attributeList, int attributeCount, int flags, ref IntPtr size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UpdateProcThreadAttribute(
            IntPtr attributeList,
            uint flags,
            IntPtr attribute,
            IntPtr value,
            IntPtr size,
            IntPtr previousValue,
            IntPtr returnSize);

        [DllImport("kernel32.dll")]
        internal static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

        [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateProcess(
            string? applicationName,
            StringBuilder commandLine,
            IntPtr processAttributes,
            IntPtr threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
            uint creationFlags,
            IntPtr environment,
            string currentDirectory,
            ref StartupInfoEx startupInfo,
            out ProcessInformation processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);
    }
}
