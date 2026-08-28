using Filekin.Core.Archives;
using Filekin.Core.Commands.App.Zip;
using Filekin.Core.Commands.References;

namespace Filekin.Core.Tests.Commands.App.Zip;

/// <summary>
/// The <c>/zip</c> grammar: items, an optional name, and the switches it shares with <c>/unzip</c>.
/// Its trailing-argument rule is the inverse of <c>/unzip</c>'s — an argument ending in <c>.zip</c>
/// is the archive being written, everything else is a source.
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
    /// <c>-noroot</c> is the one <c>/unzip</c> switch <c>/zip</c> does not take. It is refused by
    /// name rather than as a generic unknown, because anyone typing it has extraction in mind and
    /// needs to hear why it does not apply.
    /// </summary>
    [TestMethod]
    public void NoRootIsRefusedByNameBecauseCompressionHasNoSuchChoice()
    {
        var result = _parser.Parse("/zip -noroot photos", new ReferenceContext(Work, []));

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Error, "-noroot");
        StringAssert.Contains(result.Error, "extraction");
    }

    [TestMethod]
    public void AnUnknownSwitchNamesTheOnesThatWork()
    {
        var result = _parser.Parse("/zip -bogus photos", new ReferenceContext(Work, []));

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Error, "-skip, -overwrite, or -y");
    }

    [TestMethod]
    public void NoSwitchLeavesBothChoicesToTheSettings()
    {
        var result = _parser.Parse("/zip photos", new ReferenceContext(Work, []));

        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.IsNull(result.Invocation!.CollisionPolicy);
        Assert.IsNull(result.Invocation.SkipPreview);
    }

    [TestMethod]
    public void OverwriteAndSkipAreCarriedAsAnExplicitChoice()
    {
        var overwrite = _parser.Parse("/zip -overwrite photos", new ReferenceContext(Work, []));
        Assert.IsTrue(overwrite.Succeeded, overwrite.Error);
        Assert.AreEqual(CollisionPolicy.Overwrite, overwrite.Invocation!.CollisionPolicy);

        var skip = _parser.Parse("/zip -skip photos", new ReferenceContext(Work, []));
        Assert.IsTrue(skip.Succeeded, skip.Error);
        Assert.AreEqual(CollisionPolicy.Skip, skip.Invocation!.CollisionPolicy);
    }

    [TestMethod]
    [DataRow("/zip -y photos")]
    [DataRow("/zip -yes photos")]
    public void TheSkipPreviewSwitchIsCarried(string input)
    {
        var result = _parser.Parse(input, new ReferenceContext(Work, []));

        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.IsTrue(result.Invocation!.SkipPreview);
    }

    [TestMethod]
    public void ContradictoryCollisionSwitchesAreRefused()
    {
        var result = _parser.Parse("/zip -skip -overwrite photos", new ReferenceContext(Work, []));

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Error, "not both");
    }

    [TestMethod]
    public void SwitchesDoNotDisturbTheTrailingNameRule()
    {
        var result = _parser.Parse(@"/zip -y -overwrite photos notes.txt out.zip", new ReferenceContext(Work, []));

        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.AreEqual(@"D:\Work\out.zip", result.Invocation!.OutputPath);
        Assert.AreEqual(2, result.Invocation.SourcePaths.Count);
        Assert.IsTrue(result.Invocation.SkipPreview);
        Assert.AreEqual(CollisionPolicy.Overwrite, result.Invocation.CollisionPolicy);
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
