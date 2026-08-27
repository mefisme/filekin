using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Filekin.Core.Terminal;
using Filekin.Infrastructure.Windows.Terminal.Interop;
using Microsoft.Win32.SafeHandles;

namespace Filekin.Infrastructure.Windows.Terminal;

/// <summary>
/// A hosted terminal session backed by a Windows Pseudoconsole (ConPTY). PowerShell is the
/// root process; interactive tools run inside it and, on exit, return to the shell prompt.
/// This type owns the ConPTY lifecycle only — it surfaces the raw output byte stream and does
/// not interpret or render VT/ANSI sequences.
/// </summary>
public sealed class ConPtyTerminalSession : ITerminalSession
{
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const nuint ProcThreadAttributePseudoConsole = 0x00020016;
    private const uint HandleFlagInherit = 0x00000001;
    private const int StartfUseStdHandles = 0x00000100;
    private const int BufferSize = 4096;
    private const int MaxPendingOutputBytes = 1 << 20;

    private readonly FileStream _input;
    private readonly FileStream _output;
    private readonly Process _rootProcess;
    private readonly int _rootProcessId;
    private readonly Task _outputPump;
    private readonly object _outputEventGate = new();
    private readonly List<TerminalOutputEventArgs> _pendingOutput = [];
    private readonly SemaphoreSlim _inputGate = new(1, 1);
    private int _pendingOutputBytes;

    private EventHandler<TerminalOutputEventArgs>? _outputReceived;

    private IntPtr _pseudoConsole;
    private int _disposed;
    private int _exitRaised;
    private int _hasExited;
    private int _exitCode;

    private ConPtyTerminalSession(
        IntPtr pseudoConsole,
        SafeFileHandle inputWrite,
        SafeFileHandle outputRead,
        Process rootProcess)
    {
        _pseudoConsole = pseudoConsole;

        // CreatePipe produces synchronous handles, as the ConPTY contract requires; the
        // FileStreams must therefore be synchronous. Input and output are still serviced
        // independently (output on its own pump task) to avoid full-buffer deadlocks.
        _input = new FileStream(inputWrite, FileAccess.Write, BufferSize, isAsync: false);
        _output = new FileStream(outputRead, FileAccess.Read, BufferSize, isAsync: false);
        _rootProcess = rootProcess;
        _rootProcessId = rootProcess.Id;

        _rootProcess.EnableRaisingEvents = true;
        _rootProcess.Exited += OnRootProcessExited;

        _outputPump = Task.Run(PumpOutputAsync);

        // Guard against the root exiting between CreateProcess and the event subscription.
        if (_rootProcess.HasExited)
        {
            OnRootProcessExited(this, EventArgs.Empty);
        }
    }

    public event EventHandler<TerminalOutputEventArgs>? OutputReceived
    {
        add
        {
            if (value is null)
            {
                return;
            }

            List<TerminalOutputEventArgs>? pending = null;
            lock (_outputEventGate)
            {
                _outputReceived += value;
                if (_pendingOutput.Count > 0)
                {
                    pending = [.. _pendingOutput];
                    _pendingOutput.Clear();
                    _pendingOutputBytes = 0;
                }
            }

            // A root shell may emit its initial prompt before Start returns. Replay those chunks to
            // the first renderer instead of losing the beginning of the terminal screen.
            if (pending is not null)
            {
                foreach (var chunk in pending)
                {
                    value(this, chunk);
                }
            }
        }

        remove
        {
            lock (_outputEventGate)
            {
                _outputReceived -= value;
            }
        }
    }

    public event EventHandler<TerminalExitEventArgs>? Exited;

    public int RootProcessId => _rootProcessId;

    public bool HasExited => Volatile.Read(ref _hasExited) != 0;

    public int? ExitCode => Volatile.Read(ref _hasExited) != 0 ? _exitCode : null;

    internal static ConPtyTerminalSession Create(string powerShellExecutable, TerminalSessionRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(powerShellExecutable);
        ArgumentNullException.ThrowIfNull(request);

        var size = new Coord(request.InitialSize.Columns, request.InitialSize.Rows);
        var workingDirectory = ResolveWorkingDirectory(request);

        if (!ConPtyInterop.CreatePipe(out var inputRead, out var inputWrite, IntPtr.Zero, 0))
        {
            throw LastError("CreatePipe(input)");
        }

        if (!ConPtyInterop.CreatePipe(out var outputRead, out var outputWrite, IntPtr.Zero, 0))
        {
            _ = ConPtyInterop.CloseHandle(inputRead);
            _ = ConPtyInterop.CloseHandle(inputWrite);
            throw LastError("CreatePipe(output)");
        }

        // The host owns the write end of input and the read end of output; keep them private.
        _ = ConPtyInterop.SetHandleInformation(inputWrite, HandleFlagInherit, 0);
        _ = ConPtyInterop.SetHandleInformation(outputRead, HandleFlagInherit, 0);

        var createResult = ConPtyInterop.CreatePseudoConsole(size, inputRead, outputWrite, 0, out var pseudoConsole);

        // The pseudoconsole duplicated the child ends; the host no longer needs them.
        _ = ConPtyInterop.CloseHandle(inputRead);
        _ = ConPtyInterop.CloseHandle(outputWrite);

        if (createResult < 0)
        {
            _ = ConPtyInterop.CloseHandle(inputWrite);
            _ = ConPtyInterop.CloseHandle(outputRead);
            Marshal.ThrowExceptionForHR(createResult);
        }

        var attributeList = IntPtr.Zero;
        try
        {
            nuint attributeListSize = 0;
            _ = ConPtyInterop.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeListSize);
            attributeList = Marshal.AllocHGlobal((int)attributeListSize);

            if (!ConPtyInterop.InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeListSize))
            {
                throw LastError("InitializeProcThreadAttributeList");
            }

            if (!ConPtyInterop.UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    ProcThreadAttributePseudoConsole,
                    pseudoConsole,
                    (nuint)IntPtr.Size,
                    IntPtr.Zero,
                    IntPtr.Zero))
            {
                throw LastError("UpdateProcThreadAttribute");
            }

            var startupInfo = default(StartupInfoEx);
            startupInfo.StartupInfo.cb = Marshal.SizeOf<StartupInfoEx>();

            // Without STARTF_USESTDHANDLES a GUI/redirected host can have its redirected stdio
            // duplicated into the child even when a pseudoconsole attribute is present. Null
            // standard handles plus this flag force stdio to be established through ConPTY.
            startupInfo.StartupInfo.dwFlags = StartfUseStdHandles;
            startupInfo.lpAttributeList = attributeList;

            var commandLine = (BuildRootCommandLine(powerShellExecutable, request) + '\0').ToCharArray();

            if (!ConPtyInterop.CreateProcess(
                    null,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    ExtendedStartupInfoPresent | CreateUnicodeEnvironment,
                    IntPtr.Zero,
                    workingDirectory,
                    ref startupInfo,
                    out var processInformation))
            {
                throw LastError("CreateProcessW");
            }

            try
            {
                var process = Process.GetProcessById((int)processInformation.dwProcessId);
                return new ConPtyTerminalSession(
                    pseudoConsole,
                    new SafeFileHandle(inputWrite, ownsHandle: true),
                    new SafeFileHandle(outputRead, ownsHandle: true),
                    process);
            }
            finally
            {
                _ = ConPtyInterop.CloseHandle(processInformation.hThread);
                _ = ConPtyInterop.CloseHandle(processInformation.hProcess);
            }
        }
        catch
        {
            ConPtyInterop.ClosePseudoConsole(pseudoConsole);
            _ = ConPtyInterop.CloseHandle(inputWrite);
            _ = ConPtyInterop.CloseHandle(outputRead);
            throw;
        }
        finally
        {
            if (attributeList != IntPtr.Zero)
            {
                ConPtyInterop.DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }
        }
    }

    /// <summary>
    /// Writes to the pseudoconsole input pipe. A terminal surface sends one keystroke per call
    /// without awaiting the previous one, so the writes are serialized here: concurrent writes to a
    /// <see cref="FileStream"/> are undefined and would interleave or drop typed input.
    /// </summary>
    public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _inputGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await _input.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            await _input.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _inputGate.Release();
        }
    }

    public ValueTask WriteAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
    }

    public void Resize(TerminalSize size)
    {
        ThrowIfDisposed();
        var result = ConPtyInterop.ResizePseudoConsole(_pseudoConsole, new Coord(size.Columns, size.Rows));
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        return _rootProcess.WaitForExitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _rootProcess.Exited -= OnRootProcessExited;

        try
        {
            await _input.DisposeAsync().ConfigureAwait(false);
        }
        catch (IOException)
        {
            // The child end of the pipe may already be gone; that is expected during teardown.
        }

        if (_pseudoConsole != IntPtr.Zero)
        {
            // Terminates attached clients (the root shell and its children). A final output
            // frame may still be emitted, so the pump keeps draining below until the pipe breaks.
            ConPtyInterop.ClosePseudoConsole(_pseudoConsole);
            _pseudoConsole = IntPtr.Zero;
        }

        try
        {
            await _outputPump.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // The boundary is already closed by ClosePseudoConsole; do not block teardown.
        }

        await _output.DisposeAsync().ConfigureAwait(false);

        try
        {
            if (!_rootProcess.HasExited)
            {
                _rootProcess.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the check and the kill request.
        }
        catch (Win32Exception)
        {
            // The process is already terminating; nothing further to do.
        }

        _rootProcess.Dispose();
        _inputGate.Dispose();

        lock (_outputEventGate)
        {
            _pendingOutput.Clear();
            _pendingOutputBytes = 0;
        }
    }

    private static string BuildRootCommandLine(string powerShellExecutable, TerminalSessionRequest request)
    {
        var location = request.Launch.InitialLocation.PowerShellPath;
        var escapedLocation = location.Replace("'", "''", StringComparison.Ordinal);

        var startup = new StringBuilder();
        // Filekin may have been launched before an installer updated the user's PATH. Preserve any
        // process-specific entries, then add the current configured machine/user values so each new
        // hosted shell sees tools that an ordinary newly opened PowerShell can resolve.
        startup.Append("$env:PATH = @($env:PATH, ")
            .Append("[Environment]::GetEnvironmentVariable('Path', 'Machine'), ")
            .Append("[Environment]::GetEnvironmentVariable('Path', 'User')) -join ';'; ");
        startup.Append("Set-Location -LiteralPath '").Append(escapedLocation).Append('\'');

        if (!string.IsNullOrWhiteSpace(request.Launch.CommandText))
        {
            // v1 known-interactive-tool invocations are simple tokens (claude, codex, python,
            // ssh, pwsh). Commands containing embedded double quotes are out of scope here and
            // are recorded as a follow-up in HANDOFF.md.
            startup.Append("; ").Append(request.Launch.CommandText);
        }

        var profileFlag = request.LoadProfile ? string.Empty : " -NoProfile";

        // -NoExit keeps the shell interactive after the one-shot startup command returns, so an
        // interactive tool that exits drops back to the PowerShell prompt (Filekin invariant).
        return $"\"{powerShellExecutable}\"{profileFlag} -NoLogo -NoExit -Command \"{startup}\"";
    }

    private static string ResolveWorkingDirectory(TerminalSessionRequest request)
    {
        // CreateProcessW needs a filesystem working directory. For a filesystem launch that is
        // the requested location. For a non-filesystem provider delegation there is no
        // filesystem path, so the process starts in the user profile and the -Command Set-Location
        // moves it to the provider path (for example HKLM:\).
        var location = request.Launch.InitialLocation;
        return location.IsFileSystem
            ? location.FileSystemPath!
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static Win32Exception LastError(string api)
    {
        return new Win32Exception(Marshal.GetLastPInvokeError(), $"{api} failed.");
    }

    private void OnRootProcessExited(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _exitRaised, 1) != 0)
        {
            return;
        }

        int code;
        try
        {
            code = _rootProcess.ExitCode;
        }
        catch (InvalidOperationException)
        {
            code = -1;
        }

        _exitCode = code;
        Volatile.Write(ref _hasExited, 1);
        Exited?.Invoke(this, new TerminalExitEventArgs(code));
    }

    private async Task PumpOutputAsync()
    {
        var buffer = new byte[BufferSize];
        try
        {
            while (true)
            {
                var count = await _output.ReadAsync(buffer).ConfigureAwait(false);
                if (count == 0)
                {
                    break;
                }

                var chunk = new byte[count];
                Array.Copy(buffer, chunk, count);
                var eventArgs = new TerminalOutputEventArgs(chunk);
                EventHandler<TerminalOutputEventArgs>? handler;
                lock (_outputEventGate)
                {
                    handler = _outputReceived;
                    if (handler is null)
                    {
                        // Only the startup frame needs replaying. A session nobody ever renders must
                        // not accumulate its whole output in memory, so drop the oldest chunks.
                        _pendingOutput.Add(eventArgs);
                        _pendingOutputBytes += count;
                        while (_pendingOutputBytes > MaxPendingOutputBytes && _pendingOutput.Count > 1)
                        {
                            _pendingOutputBytes -= _pendingOutput[0].Data.Length;
                            _pendingOutput.RemoveAt(0);
                        }
                    }
                }

                handler?.Invoke(this, eventArgs);
            }
        }
        catch (IOException)
        {
            // The pipe broke because the session ended; this is the normal end of the pump.
        }
        catch (ObjectDisposedException)
        {
            // Disposal closed the output stream while a read was in flight.
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
}
