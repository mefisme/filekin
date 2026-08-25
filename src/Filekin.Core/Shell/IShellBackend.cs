namespace Filekin.Core.Shell;

public interface IShellBackend : IAsyncDisposable
{
    Task<ShellExecutionResult> ExecuteAsync(
        string commandText,
        CancellationToken cancellationToken = default);

    Task<ShellLocation> GetLocationAsync(CancellationToken cancellationToken = default);

    Task<ShellLocation> SetFileSystemLocationAsync(
        string path,
        CancellationToken cancellationToken = default);
}
