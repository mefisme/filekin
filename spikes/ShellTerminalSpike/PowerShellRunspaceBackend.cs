using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace Filekin.ShellTerminalSpike;

internal sealed class PowerShellRunspaceBackend : IDisposable
{
    private const string MetadataProperty = "__FilekinSpikeMetadata";
    private readonly object _activeGate = new();
    private readonly Runspace _runspace;
    private PowerShell? _activePipeline;

    public PowerShellRunspaceBackend(string initialFilesystemLocation)
    {
        var initialState = InitialSessionState.CreateDefault2();
        _runspace = RunspaceFactory.CreateRunspace(initialState);
        _runspace.Open();
        SetFilesystemLocation(initialFilesystemLocation);
    }

    public RunspaceCommandResult Execute(string script)
    {
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = _runspace;

        // A nullable LASTEXITCODE lets the result distinguish native execution from cmdlet-only input.
        powerShell.AddScript("$global:LASTEXITCODE = $null", useLocalScope: false);
        powerShell.AddStatement();
        powerShell.AddScript(script, useLocalScope: false);
        powerShell.AddStatement();
        powerShell.AddScript(MetadataScript, useLocalScope: false);

        lock (_activeGate)
        {
            _activePipeline = powerShell;
        }

        Collection<PSObject> output;
        var stopped = false;
        try
        {
            output = powerShell.Invoke();
        }
        catch (PipelineStoppedException)
        {
            output = [];
            stopped = true;
        }
        finally
        {
            lock (_activeGate)
            {
                if (ReferenceEquals(_activePipeline, powerShell))
                {
                    _activePipeline = null;
                }
            }
        }

        var standardOutput = new List<string>();
        LocationSnapshot? location = null;
        int? nativeExitCode = null;
        var pipelineSucceeded = !powerShell.HadErrors;

        foreach (var item in output)
        {
            if (item.Properties[MetadataProperty]?.Value is true)
            {
                var provider = item.Properties["ProviderName"]?.Value?.ToString() ?? string.Empty;
                var currentPath = item.Properties["CurrentPath"]?.Value?.ToString() ?? string.Empty;
                var providerPath = item.Properties["ProviderPath"]?.Value?.ToString();
                location = new LocationSnapshot(currentPath, provider, providerPath);

                if (int.TryParse(item.Properties["NativeExitCode"]?.Value?.ToString(), out var parsedExitCode))
                {
                    nativeExitCode = parsedExitCode;
                }

                if (bool.TryParse(item.Properties["PipelineSucceeded"]?.Value?.ToString(), out var parsedSucceeded))
                {
                    pipelineSucceeded = parsedSucceeded && !powerShell.HadErrors;
                }

                continue;
            }

            standardOutput.Add(item?.ToString() ?? string.Empty);
        }

        var standardError = powerShell.Streams.Error.Select(error => error.ToString()).ToList();
        return new RunspaceCommandResult(
            standardOutput,
            standardError,
            nativeExitCode,
            pipelineSucceeded,
            stopped,
            location ?? GetLocation());
    }

    public void SetFilesystemLocation(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(fullPath);
        }

        using var powerShell = PowerShell.Create();
        powerShell.Runspace = _runspace;
        powerShell.AddCommand("Set-Location").AddParameter("LiteralPath", fullPath);
        powerShell.Invoke();
        if (powerShell.HadErrors)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, powerShell.Streams.Error));
        }

        var location = GetLocation();
        if (!location.IsFilesystem)
        {
            throw new InvalidOperationException($"Set-Location resolved to non-filesystem provider '{location.ProviderName}'.");
        }
    }

    public LocationSnapshot GetLocation()
    {
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = _runspace;
        powerShell.AddScript(MetadataScript);
        var output = powerShell.Invoke();
        var item = output.Last();
        return new LocationSnapshot(
            item.Properties["CurrentPath"]?.Value?.ToString() ?? string.Empty,
            item.Properties["ProviderName"]?.Value?.ToString() ?? string.Empty,
            item.Properties["ProviderPath"]?.Value?.ToString());
    }

    public void StopActivePipeline()
    {
        PowerShell? active;
        lock (_activeGate)
        {
            active = _activePipeline;
        }

        active?.Stop();
    }

    public void Dispose()
    {
        StopActivePipeline();
        _runspace.Dispose();
    }

    private const string MetadataScript = """
        $__spikeSucceeded = $?
        $__spikeLocation = Get-Location
        $__spikeProviderPath = $null
        try {
            $__spikeProviderPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($__spikeLocation.Path)
        } catch {
            $__spikeProviderPath = $__spikeLocation.Path
        }
        [pscustomobject]@{
            __FilekinSpikeMetadata = $true
            CurrentPath = $__spikeLocation.Path
            ProviderName = $__spikeLocation.Provider.Name
            ProviderPath = $__spikeProviderPath
            NativeExitCode = $global:LASTEXITCODE
            PipelineSucceeded = $__spikeSucceeded
        }
        """;
}

internal sealed record LocationSnapshot(string CurrentPath, string ProviderName, string? ProviderPath)
{
    public bool IsFilesystem => ProviderName.Equals("FileSystem", StringComparison.OrdinalIgnoreCase);
}

internal sealed record RunspaceCommandResult(
    IReadOnlyList<string> StandardOutput,
    IReadOnlyList<string> StandardError,
    int? NativeExitCode,
    bool PipelineSucceeded,
    bool Stopped,
    LocationSnapshot Location);
