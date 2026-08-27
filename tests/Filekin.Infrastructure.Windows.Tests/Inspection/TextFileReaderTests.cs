using System.Text;
using Filekin.Infrastructure.Windows.Inspection;

namespace Filekin.Infrastructure.Windows.Tests.Inspection;

[TestClass]
public sealed class TextFileReaderTests
{
    private string _root = null!;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), $"Filekin-Text-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [TestMethod]
    public void AByteOrderMarkNamesTheEncoding()
    {
        Assert.AreEqual("UTF-8 with BOM", Sniff("bom.txt", new UTF8Encoding(true), "hello")!.EncodingName);
        Assert.AreEqual("UTF-16 LE", Sniff("utf16.txt", Encoding.Unicode, "hello")!.EncodingName);
        Assert.AreEqual("UTF-16 BE", Sniff("utf16be.txt", Encoding.BigEndianUnicode, "hello")!.EncodingName);
    }

    [TestMethod]
    public void PlainAsciiIsReportedAsUtf8()
    {
        Assert.AreEqual("UTF-8", Sniff("plain.txt", new UTF8Encoding(false), "hello")!.EncodingName);
    }

    [TestMethod]
    public void ValidMultiByteUtf8IsRecognizedWithoutABom()
    {
        Assert.AreEqual("UTF-8", Sniff("accents.txt", new UTF8Encoding(false), "café — naïve")!.EncodingName);
    }

    [TestMethod]
    public void AFileWithInvalidUtf8IsReportedAsEightBitRatherThanGuessedAtACodePage()
    {
        var path = Path.Combine(_root, "ansi.txt");
        File.WriteAllBytes(path, [(byte)'a', 0xE9, (byte)'b']);

        Assert.AreEqual("8-bit text", TextFileReader.Sniff(path)!.EncodingName);
    }

    [TestMethod]
    public void ANulByteMeansBinary()
    {
        var path = Path.Combine(_root, "blob.bin");
        File.WriteAllBytes(path, [0x41, 0x00, 0x42]);

        Assert.IsNull(TextFileReader.Sniff(path));
    }

    [TestMethod]
    public async Task ATrailingNewlineDoesNotAddAnEmptyLine()
    {
        var path = Path.Combine(_root, "three.txt");
        await File.WriteAllTextAsync(path, "one\ntwo\nthree\n");

        Assert.AreEqual(3, await TextFileReader.CountLinesAsync(path, TextFileReader.Sniff(path)!));
    }

    [TestMethod]
    public async Task AFinalLineWithoutANewlineStillCounts()
    {
        var path = Path.Combine(_root, "three-open.txt");
        await File.WriteAllTextAsync(path, "one\ntwo\nthree");

        Assert.AreEqual(3, await TextFileReader.CountLinesAsync(path, TextFileReader.Sniff(path)!));
    }

    [TestMethod]
    public async Task AnEmptyFileHasNoLines()
    {
        var path = Path.Combine(_root, "empty.txt");
        await File.WriteAllTextAsync(path, string.Empty);

        Assert.AreEqual(0, await TextFileReader.CountLinesAsync(path, new TextFileProbe("UTF-8", 0)));
    }

    [TestMethod]
    public async Task WindowsLineEndingsCountOncePerLine()
    {
        var path = Path.Combine(_root, "crlf.txt");
        await File.WriteAllTextAsync(path, "one\r\ntwo\r\n");

        Assert.AreEqual(2, await TextFileReader.CountLinesAsync(path, TextFileReader.Sniff(path)!));
    }

    private TextFileProbe? Sniff(string name, Encoding encoding, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content, encoding);
        return TextFileReader.Sniff(path);
    }
}
