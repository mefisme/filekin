using Filekin.Core.Commands.App.Zip;
using Filekin.Core.Commands.References;

namespace Filekin.Core.Tests.Commands.App.Zip;

/// <summary>
/// The <c>/zip</c> grammar: items and an optional name, and nothing else. Its trailing-argument rule
/// is the inverse of <c>/unzip</c>'s — an argument ending in <c>.zip</c> is the archive being
/// written, everything else is a source.
/// </summary>
[TestClass]
public sealed class ZipInvocationParserTests
{
    private const string Work = @"D:\Work";

    private static readonly string[] Selected = [@"D:\Work\a.txt", @"D:\Work\b.txt"];

    private readonly ZipInvocationParser _parser = new(new ReferenceResolver(new NamedLocations()));

    [TestMethod]
    public void BareZipCompressesTheSelection()
    {
        var result = _parser.Parse("/zip", new ReferenceContext(Work, Selected));

        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEqual(Selected, result.Invocation!.SourcePaths.ToArray());
    }

    [TestMethod]
    public void BareZipWithNothingSelectedExplainsItself()
    {
        var result = _parser.Parse("/zip", new ReferenceContext(Work, []));

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Error, "/zip needs something to compress.");
    }

    /// <summary>One source names the archive after itself, so <c>/zip photos</c> writes <c>photos.zip</c>.</summary>
    [TestMethod]
    public void OneSourceNamesTheArchiveAfterItself()
    {
        var result = _parser.Parse("/zip photos", new ReferenceContext(Work, []));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(@"D:\Work\photos.zip", result.Invocation!.OutputPath);
    }

    /// <summary>Several sources name it after the folder they are in, because no one source labels the rest.</summary>
    [TestMethod]
    public void SeveralSourcesNameTheArchiveAfterTheFolder()
    {
        var result = _parser.Parse("/zip a.txt b.txt", new ReferenceContext(Work, []));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(@"D:\Work\Work.zip", result.Invocation!.OutputPath);
    }

    [TestMethod]
    public void ATrailingZipNameIsTheArchiveToWrite()
    {
        var result = _parser.Parse(
            @"/zip photos notes.txt D:\backup\stuff.zip", new ReferenceContext(Work, []));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(@"D:\backup\stuff.zip", result.Invocation!.OutputPath);
        Assert.AreEqual(2, result.Invocation.SourcePaths.Count);
    }

    [TestMethod]
    public void TheArchiveMayLandInALocation()
    {
        var result = _parser.Parse(@"/zip photos @tools\out.zip", new ReferenceContext(Work, []));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(@"D:\Tool Box\out.zip", result.Invocation!.OutputPath);
    }

    [TestMethod]
    public void ASelectionReferenceExpandsToEveryItem()
    {
        var result = _parser.Parse("/zip @selection out.zip", new ReferenceContext(Work, Selected));

        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEqual(Selected, result.Invocation!.SourcePaths.ToArray());
        Assert.AreEqual(@"D:\Work\out.zip", result.Invocation.OutputPath);
    }

    /// <summary>
    /// <c>/zip</c> has no switches on purpose. The root and overwrite choices belong to the preview,
    /// so a switch is a mistake worth naming rather than something to quietly ignore.
    /// </summary>
    [TestMethod]
    public void ASwitchIsRefusedAndSaysWhatToDoInstead()
    {
        var result = _parser.Parse("/zip -noroot photos", new ReferenceContext(Work, []));

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Error, "not switches");
        StringAssert.Contains(result.Error, "-noroot");
    }

    [TestMethod]
    public void AQuotedSourceWithSpacesStaysOneSource()
    {
        var result = _parser.Parse(@"/zip ""my photos""", new ReferenceContext(Work, []));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, result.Invocation!.SourcePaths.Count);
        Assert.AreEqual(@"D:\Work\my photos", result.Invocation.SourcePaths[0]);
    }

    [TestMethod]
    public void WithoutAFilesystemFolderTheCommandExplainsItself()
    {
        var result = _parser.Parse("/zip photos", new ReferenceContext(null, []));

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Error, "filesystem folder");
    }

    private sealed class NamedLocations : INamedLocationResolver
    {
        public bool TryResolve(string name, out string path)
        {
            if (name.Equals("tools", StringComparison.OrdinalIgnoreCase))
            {
                path = @"D:\Tool Box";
                return true;
            }

            path = string.Empty;
            return false;
        }
    }
}
