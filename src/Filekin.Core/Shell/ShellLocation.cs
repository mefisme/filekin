namespace Filekin.Core.Shell;

public sealed record ShellLocation
{
    public ShellLocation(string powerShellPath, string providerName, string? fileSystemPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(powerShellPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        PowerShellPath = powerShellPath;
        ProviderName = providerName;
        FileSystemPath = fileSystemPath;
    }

    public string PowerShellPath { get; }

    public string ProviderName { get; }

    public string? FileSystemPath { get; }

    public bool IsFileSystem => FileSystemPath is not null;
}
