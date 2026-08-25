using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Filekin.Core.Shell;
using PowerShellInstance = System.Management.Automation.PowerShell;

namespace Filekin.Infrastructure.Windows.Shell;

public sealed class PowerShellRunspaceBackend : IShellBackend
{
    private const string FileSystemProviderName = "FileSystem";

    private readonly object _activePowerShellGate = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly Runspace _runspace;
    private PowerShellInstance? _activePowerShell;
    private int _disposed;

    private PowerShellRunspaceBackend(Runspace runspace)
    {
        _runspace = runspace;
    }

    public static async Task<PowerShellRunspaceBackend> CreateAsync(
        string initialFileSystemPath,
        CancellationToken cancellationToken = default)
    {
        ValidateFileSystemPath(initialFileSystemPath);

        var initialState = InitialSessionState.CreateDefault2();
        var runspace = RunspaceFactory.CreateRunspace(initialState);

        try
        {
            await Task.Run(runspace.Open, cancellationToken).ConfigureAwait(false);

            var backend = new PowerShellRunspaceBackend(runspace);
            await backend
                .SetFileSystemLocationAsync(initialFileSystemPath, cancellationToken)
                .ConfigureAwait(false);
            return backend;
        }
        catch
        {
            runspace.Dispose();
            throw;
        }
    }

    public async Task<ShellExecutionResult> ExecuteAsync(
        string commandText,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandText);

        return await RunSerializedAsync(
            async () =>
            {
                var previousLocation = await GetLocationCoreAsync(cancellationToken).ConfigureAwait(false);
                EnsureFileSystemLocation(previousLocation);

                using var powerShell = CreatePowerShell();
                powerShell.AddScript(commandText, useLocalScope: false);

                PSDataCollection<PSObject>? output = null;
                var resultingLocation = previousLocation;
                ShellTerminalLaunchRequest? terminalLaunchRequest = null;

                try
                {
                    output = await InvokeAsync(powerShell, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    var observedLocation = await GetLocationCoreAsync(CancellationToken.None).ConfigureAwait(false);
                    if (observedLocation.IsFileSystem)
                    {
                        resultingLocation = observedLocation;
                    }
                    else
                    {
                        terminalLaunchRequest = new ShellTerminalLaunchRequest(observedLocation);
                        resultingLocation = await SetFileSystemLocationCoreAsync(
                                previousLocation.FileSystemPath!,
                                CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }

                var errors = powerShell.Streams.Error
                    .Select(static error => error.ToString())
                    .ToArray();

                return new ShellExecutionResult(
                    Array.AsReadOnly(output!.Select(static item => item?.ToString() ?? string.Empty).ToArray()),
                    Array.AsReadOnly(errors),
                    resultingLocation,
                    terminalLaunchRequest);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public Task<ShellLocation> GetLocationAsync(CancellationToken cancellationToken = default)
    {
        return RunSerializedAsync(
            () => GetLocationCoreAsync(cancellationToken),
            cancellationToken);
    }

    public Task<ShellLocation> SetFileSystemLocationAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ValidateFileSystemPath(path);

        return RunSerializedAsync(
            () => SetFileSystemLocationCoreAsync(path, cancellationToken),
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        PowerShellInstance? activePowerShell;
        lock (_activePowerShellGate)
        {
            activePowerShell = _activePowerShell;
        }

        await Task.Run(() => TryStop(activePowerShell)).ConfigureAwait(false);

        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(_runspace.Dispose).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
            _operationGate.Dispose();
        }
    }

    private static void EnsureFileSystemLocation(ShellLocation location)
    {
        if (!location.IsFileSystem)
        {
            throw new InvalidOperationException(
                $"The Files shell backend is not at a filesystem location: '{location.PowerShellPath}'.");
        }
    }

    private static void ValidateFileSystemPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("A fully qualified filesystem path is required.", nameof(path));
        }
    }

    private static void TryStop(PowerShellInstance? powerShell)
    {
        if (powerShell is null)
        {
            return;
        }

        try
        {
            powerShell.Stop();
        }
        catch (ObjectDisposedException)
        {
            // Disposal won the race with cancellation or backend shutdown.
        }
        catch (InvalidOperationException)
        {
            // The invocation completed before the stop request reached it.
        }
    }

    private PowerShellInstance CreatePowerShell()
    {
        var powerShell = PowerShellInstance.Create();
        powerShell.Runspace = _runspace;
        return powerShell;
    }

    private Task<ShellLocation> GetLocationCoreAsync(CancellationToken cancellationToken)
    {
        return Task.Run(
            () => CreateShellLocation(_runspace.SessionStateProxy.Path.CurrentLocation),
            cancellationToken);
    }

    private static ShellLocation CreateShellLocation(PathInfo pathInfo)
    {
        var isFileSystem = pathInfo.Provider.Name.Equals(
            FileSystemProviderName,
            StringComparison.OrdinalIgnoreCase);

        return new ShellLocation(
            pathInfo.Path,
            pathInfo.Provider.Name,
            isFileSystem ? pathInfo.ProviderPath : null);
    }

    private async Task<ShellLocation> SetFileSystemLocationCoreAsync(
        string path,
        CancellationToken cancellationToken)
    {
        using var powerShell = CreatePowerShell();
        powerShell
            .AddCommand("Set-Location")
            .AddParameter("LiteralPath", path);

        await InvokeAsync(powerShell, cancellationToken).ConfigureAwait(false);
        if (powerShell.HadErrors)
        {
            throw CreateInvocationException($"PowerShell could not enter filesystem location '{path}'.", powerShell);
        }

        var location = await GetLocationCoreAsync(cancellationToken).ConfigureAwait(false);
        EnsureFileSystemLocation(location);
        return location;
    }

    private async Task<PSDataCollection<PSObject>> InvokeAsync(
        PowerShellInstance powerShell,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_activePowerShellGate)
        {
            _activePowerShell = powerShell;
        }

        try
        {
            var invocation = powerShell.InvokeAsync();
            using var cancellationRegistration = cancellationToken.Register(
                static state => QueueStop((PowerShellInstance?)state),
                powerShell);

            try
            {
                return await invocation.ConfigureAwait(false);
            }
            catch (PipelineStoppedException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }
        finally
        {
            lock (_activePowerShellGate)
            {
                if (ReferenceEquals(_activePowerShell, powerShell))
                {
                    _activePowerShell = null;
                }
            }
        }
    }

    private static void QueueStop(PowerShellInstance? powerShell)
    {
        ThreadPool.QueueUserWorkItem(
            static state => TryStop(state),
            powerShell,
            preferLocal: false);
    }

    private async Task<T> RunSerializedAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ThrowIfDisposed();
            return await operation().ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private static InvalidOperationException CreateInvocationException(
        string message,
        PowerShellInstance powerShell)
    {
        var details = string.Join(
            Environment.NewLine,
            powerShell.Streams.Error.Select(static error => error.ToString()));

        return string.IsNullOrWhiteSpace(details)
            ? new InvalidOperationException(message)
            : new InvalidOperationException($"{message}{Environment.NewLine}{details}");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
}
