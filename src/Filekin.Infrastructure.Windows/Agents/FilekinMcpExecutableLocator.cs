namespace Filekin.Infrastructure.Windows.Agents;

/// <summary>
/// Resolves the packaged Filekin MCP companion without starting it or initializing coordination.
/// </summary>
public static class FilekinMcpExecutableLocator
{
    public const string ExecutableFileName = "Filekin.Mcp.exe";

    public static string Resolve() => Resolve(AppContext.BaseDirectory);

    public static string Resolve(string applicationBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationBaseDirectory);
        if (!Path.IsPathFullyQualified(applicationBaseDirectory))
        {
            throw new ArgumentException(
                "The application base directory must be fully qualified.",
                nameof(applicationBaseDirectory));
        }

        var executablePath = Path.GetFullPath(
            Path.Combine(
                applicationBaseDirectory,
                ExecutableFileName));
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                "The packaged Filekin MCP companion is missing. Repair or reinstall Filekin before starting agent coordination.",
                executablePath);
        }

        return executablePath;
    }
}
