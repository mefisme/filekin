using Filekin.Core.Inspection;
using Filekin.Infrastructure.Windows.Inspection;

namespace Filekin.Infrastructure.Windows.Tests.Inspection;

[TestClass]
public sealed class WindowsFileInspectorTests
{
    private static readonly string[] AlwaysUsefulFileFields =
        ["Type", "Size", "Path", "Created", "Modified", "Encoding"];

    private string _root = null!;
    private readonly WindowsFileInspector _inspector = new();

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), $"Filekin-Info-{Guid.NewGuid():N}");
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
    public void AFileReportsTheAlwaysUsefulFieldsInOrder()
    {
        var file = Path.Combine(_root, "notes.txt");
        File.WriteAllText(file, "one\r\ntwo\r\n");

        var result = _inspector.Inspect(file);

        Assert.AreEqual(InspectionKind.File, result.Kind);
        Assert.AreEqual("notes.txt", result.Heading);
        Assert.AreEqual(file, result.SinglePath);
        Assert.IsFalse(result.NeedsAggregate);
        CollectionAssert.AreEqual(
            AlwaysUsefulFileFields,
            result.Details.Select(static detail => detail.Label).ToArray());
        Assert.AreEqual(file, result.Details.Single(static detail => detail.Label == "Path").Value);
    }

    [TestMethod]
    public void ATextFileOffersALineCountAndNamesItsEncoding()
    {
        var file = Path.Combine(_root, "readme.md");
        File.WriteAllText(file, "# title\n");

        var result = _inspector.Inspect(file);

        Assert.IsTrue(result.CanCountLines);
        Assert.AreEqual("UTF-8", result.Details.Single(static detail => detail.Label == "Encoding").Value);
    }

    [TestMethod]
    public void ABinaryFileOffersNoLineCountAndNoEncoding()
    {
        var file = Path.Combine(_root, "blob.bin");
        File.WriteAllBytes(file, [0x01, 0x00, 0x02, 0x00]);

        var result = _inspector.Inspect(file);

        Assert.IsFalse(result.CanCountLines);
        Assert.IsFalse(result.Details.Any(static detail => detail.Label == "Encoding"));
    }

    [TestMethod]
    public void AFolderAsksForAggregatesAndDoesNotInventASize()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "My Project")).FullName;

        var result = _inspector.Inspect(folder);

        Assert.AreEqual(InspectionKind.Folder, result.Kind);
        Assert.AreEqual("My Project", result.Heading);
        Assert.IsTrue(result.NeedsAggregate);

        // Size, Files, and Folders come from the scan; the inspector must not guess them.
        Assert.IsFalse(result.Details.Any(static detail => detail.Label is "Size" or "Files" or "Folders"));
    }

    [TestMethod]
    public void AMultipleSelectionIsSummarizedRatherThanListed()
    {
        var first = Path.Combine(_root, "a.txt");
        var second = Path.Combine(_root, "b.txt");
        File.WriteAllText(first, "a");
        File.WriteAllText(second, "b");

        var result = _inspector.InspectSelection([first, second]);

        Assert.AreEqual(InspectionKind.Selection, result.Kind);
        Assert.AreEqual("2 selected items", result.Heading);
        Assert.IsNull(result.SinglePath, "A multi-item selection has no single target to act on.");
        Assert.IsTrue(result.NeedsAggregate);
        Assert.AreEqual(_root, result.Details.Single(static detail => detail.Label == "Location").Value);
    }

    [TestMethod]
    public void ASelectionSpanningFoldersSaysHowManyFolders()
    {
        var nested = Directory.CreateDirectory(Path.Combine(_root, "nested")).FullName;
        var first = Path.Combine(_root, "a.txt");
        var second = Path.Combine(nested, "b.txt");
        File.WriteAllText(first, "a");
        File.WriteAllText(second, "b");

        var result = _inspector.InspectSelection([first, second]);

        Assert.AreEqual("2 folders", result.Details.Single(static detail => detail.Label == "Location").Value);
    }

    [TestMethod]
    public void ASingleItemSelectionIsDescribedAsThatItem()
    {
        var file = Path.Combine(_root, "only.txt");
        File.WriteAllText(file, "x");

        var result = _inspector.InspectSelection([file]);

        Assert.AreEqual(InspectionKind.File, result.Kind);
        Assert.AreEqual(file, result.SinglePath);
    }

    [TestMethod]
    public void AMissingTargetReportsItselfInsteadOfThrowing()
    {
        var result = _inspector.Inspect(Path.Combine(_root, "gone.txt"));

        Assert.IsNotNull(result.Error);
        Assert.AreEqual(0, result.Details.Count);
    }

    [TestMethod]
    public void AnExecutableReportsItsArchitecture()
    {
        var commandPrompt = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "cmd.exe");

        var result = _inspector.Inspect(commandPrompt);

        var architecture = result.Details.SingleOrDefault(static detail => detail.Label == "Architecture");
        Assert.IsNotNull(architecture, "A PE image should report the machine its header names.");
        Assert.AreEqual(
            Environment.Is64BitOperatingSystem ? "x64" : "x86",
            architecture.Value);
    }

    [TestMethod]
    public void AnExecutableIsLabelledCompanyRatherThanPublisher()
    {
        var commandPrompt = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "cmd.exe");

        var result = _inspector.Inspect(commandPrompt);

        // The name inside a file is a claim, not a verified signer. Calling it "Publisher" would
        // imply Filekin checked a signature (DECISIONS.md, 2026-08-27).
        Assert.IsFalse(result.Details.Any(static detail => detail.Label == "Publisher"));
        Assert.IsTrue(result.Details.Any(static detail => detail.Label == "Company"));
    }
}
