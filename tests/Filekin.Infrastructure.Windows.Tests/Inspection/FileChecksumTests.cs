using System.Text;
using Filekin.Infrastructure.Windows.Inspection;

namespace Filekin.Infrastructure.Windows.Tests.Inspection;

[TestClass]
public sealed class FileChecksumTests
{
    [TestMethod]
    public async Task Sha256MatchesTheKnownDigest()
    {
        var path = Path.Combine(Path.GetTempPath(), $"Filekin-Hash-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "abc", new UTF8Encoding(false));

        try
        {
            Assert.AreEqual(
                "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                await FileChecksum.ComputeSha256Async(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task CancellationStopsHashing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"Filekin-Hash-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(path, new byte[32 * 1024 * 1024]);

        try
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            await Assert.ThrowsExactlyAsync<TaskCanceledException>(
                () => FileChecksum.ComputeSha256Async(path, cancellation.Token));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
