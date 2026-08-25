using Filekin.Infrastructure.Windows.Shell;

namespace Filekin.Infrastructure.Windows.Tests.Shell;

[TestClass]
public sealed class PowerShellRunspaceBackendTests
{
    [TestMethod]
    public async Task ExecuteAsyncPreservesRunspaceState()
    {
        await using var backend = await PowerShellRunspaceBackend.CreateAsync(Path.GetTempPath());

        await backend.ExecuteAsync("$filekinTestValue = 'hello'");
        await backend.ExecuteAsync("function Get-FilekinTestValue { $filekinTestValue }");
        var result = await backend.ExecuteAsync("Get-FilekinTestValue");

        CollectionAssert.Contains(result.Output.ToList(), "hello");
    }

    [TestMethod]
    public async Task SetFileSystemLocationAsyncDoesNotChangeProcessLocation()
    {
        var processLocation = Environment.CurrentDirectory;
        var testLocation = CreateTestDirectory();

        try
        {
            await using var backend = await PowerShellRunspaceBackend.CreateAsync(Path.GetTempPath());

            var location = await backend.SetFileSystemLocationAsync(testLocation);

            Assert.IsTrue(location.IsFileSystem);
            Assert.AreEqual(NormalizePath(testLocation), NormalizePath(location.FileSystemPath!));
            Assert.AreEqual(processLocation, Environment.CurrentDirectory);
        }
        finally
        {
            Directory.Delete(testLocation, recursive: true);
        }
    }

    [TestMethod]
    public async Task ExecuteAsyncReturnsFileSystemNavigation()
    {
        var testLocation = CreateTestDirectory();

        try
        {
            await using var backend = await PowerShellRunspaceBackend.CreateAsync(Path.GetTempPath());
            var escapedPath = testLocation.Replace("'", "''", StringComparison.Ordinal);

            var result = await backend.ExecuteAsync($"Set-Location -LiteralPath '{escapedPath}'");

            Assert.IsNull(result.TerminalLaunchRequest);
            Assert.AreEqual(
                NormalizePath(testLocation),
                NormalizePath(result.CurrentLocation.FileSystemPath!));
        }
        finally
        {
            Directory.Delete(testLocation, recursive: true);
        }
    }

    [TestMethod]
    public async Task ExecuteAsyncDelegatesNonFileSystemProviderAndRestoresFilesLocation()
    {
        var testLocation = CreateTestDirectory();

        try
        {
            await using var backend = await PowerShellRunspaceBackend.CreateAsync(testLocation);
            await backend.ExecuteAsync("$filekinTestValue = 'retained'");

            var result = await backend.ExecuteAsync("Set-Location HKCU:\\");

            Assert.IsNotNull(result.TerminalLaunchRequest);
            Assert.AreEqual("Registry", result.TerminalLaunchRequest.InitialLocation.ProviderName);
            Assert.IsFalse(result.TerminalLaunchRequest.InitialLocation.IsFileSystem);
            StringAssert.StartsWith(
                result.TerminalLaunchRequest.InitialLocation.PowerShellPath,
                "HKCU:\\",
                StringComparison.OrdinalIgnoreCase);
            Assert.AreEqual(
                NormalizePath(testLocation),
                NormalizePath(result.CurrentLocation.FileSystemPath!));

            var retainedState = await backend.ExecuteAsync("Write-Output $filekinTestValue");
            CollectionAssert.Contains(retainedState.Output.ToList(), "retained");
        }
        finally
        {
            Directory.Delete(testLocation, recursive: true);
        }
    }

    [TestMethod]
    public async Task ExecuteAsyncCancellationRestoresFilesLocationAndLeavesBackendUsable()
    {
        var testLocation = CreateTestDirectory();

        try
        {
            await using var backend = await PowerShellRunspaceBackend.CreateAsync(testLocation);
            using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            await Assert.ThrowsExactlyAsync<OperationCanceledException>(
                () => backend.ExecuteAsync(
                    "Set-Location HKCU:\\; Start-Sleep -Seconds 5",
                    cancellationSource.Token));

            var currentLocation = await backend.GetLocationAsync();
            Assert.AreEqual(
                NormalizePath(testLocation),
                NormalizePath(currentLocation.FileSystemPath!));

            var result = await backend.ExecuteAsync("Write-Output 'ready'");
            CollectionAssert.Contains(result.Output.ToList(), "ready");
        }
        finally
        {
            Directory.Delete(testLocation, recursive: true);
        }
    }

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "Filekin.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string NormalizePath(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
