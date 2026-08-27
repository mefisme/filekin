using Filekin.Core.Archives;
using Filekin.Core.Commands.App.Unzip;
using Filekin.Core.Commands.References;

namespace Filekin.Core.Tests.Commands.App.Unzip;

/// <summary>
/// The <c>/unzip</c> grammar, including the one genuinely ambiguous part: telling a trailing
/// destination from another archive. Every shape the owner asked for on 2026-08-27 is pinned here.
/// </summary>
[TestClass]
public sealed class UnzipInvocationParserTests
{
    private const string Work = @"D:\Work";

    private static readonly string[] TwoArchives = [@"D:\Work\one.zip", @"D:\Work\two.zip"];
    private static readonly string[] ArchiveAndFile = [@"D:\Work\one.zip", @"D:\Work\notes.txt"];
    private static readonly string[] NoArchives = [@"D:\Work\notes.txt"];

    private readonly UnzipInvocationParser _parser = new(new ReferenceResolver(new NamedLocations()));

    [TestMethod]
    public void OneArchiveExtractsIntoTheVisibleFolder()
    {
        var result = _parser.Parse("/unzip photos.zip", new ReferenceContext(Work, []));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, result.Invocation!.ArchivePaths.Count);
        Assert.AreEqual(@"D:\Work\photos.zip", result.Invocation.ArchivePaths[0]);
        Assert.AreEqual(Work, result.Invocation.DestinationPath);
    }

    [TestMethod]
    public void TwoArchivesStayTwoArchivesRatherThanArchiveAndDestination()
    {
        var result = _parser.Parse("/unzip one.zip two.zip", new ReferenceContext(Work, []));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(2, result.Invocation!.ArchivePaths.Count);
        Assert.AreEqual(Work, result.Invocation.DestinationPath);
    }

    [TestMethod]
    public void ATrailingFolderThatDoesNotExistYetIsTheDestination()
    {
        var result = _parser.Parse(
            @"/unzip photos.zip D:\github\somefolder", new ReferenceContext(Work, []));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, result.Invocation!.ArchivePaths.Count);
        Assert.AreEqual(@"D:\github\somefolder", result.Invocation.DestinationPath);
    }

    [TestMethod]
    public void ATrailingReferenceIsTheDestination()
    {
        var result = _parser.Parse(
            "/unzip -noroot @selection @thisfolder", new ReferenceContext(Work, TwoArchives));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(2, result.Invocation!.ArchivePaths.Count);
        Assert.AreEqual(Work, result.Invocation.DestinationPath);
        Assert.AreEqual(UnzipLayout.NoRoot, result.Invocation.Layout);
    }

    [TestMethod]
    public void ALocationReferenceWorksAsTheDestination()
    {
        var result = _parser.Parse("/unzip one.zip @tools", new ReferenceContext(Work, []));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(@"D:\Tool Box", result.Invocation!.DestinationPath);
    }

    [TestMethod]
    public void BareUnzipUsesTheSelectedArchives()
    {
        var result = _parser.Parse("/unzip", new ReferenceContext(Work, TwoArchives));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(2, result.Invocation!.ArchivePaths.Count);
        Assert.AreEqual(Work, result.Invocation.DestinationPath);
    }

    [TestMethod]
    public void BareUnzipIgnoresSelectedItemsThatAreNotArchives()
    {
        var result = _parser.Parse("/unzip", new ReferenceContext(Work, ArchiveAndFile));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, result.Invocation!.ArchivePaths.Count);
        Assert.AreEqual(@"D:\Work\one.zip", result.Invocation.ArchivePaths[0]);
    }

    /// <summary>The wording ARCHITECTURE.md already specifies for an empty or wrong selection.</summary>
    [TestMethod]
    public void BareUnzipWithNothingUsableExplainsItself()
    {
        var result = _parser.Parse("/unzip", new ReferenceContext(Work, NoArchives));

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Error, "/unzip needs an archive.");
        StringAssert.Contains(result.Error, "/unzip @selection @thisfolder");
    }

    [TestMethod]
    public void BareUnzipDoesNotGoHuntingForAnArchiveInTheFolder()
    {
        var result = _parser.Parse("/unzip", new ReferenceContext(Work, []));

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Error, "/unzip needs an archive.");
    }

    [TestMethod]
    public void NoRootIsRecognized()
    {
        var result = _parser.Parse("/unzip -noroot photos.zip", new ReferenceContext(Work, []));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(UnzipLayout.NoRoot, result.Invocation!.Layout);
    }

    [TestMethod]
    public void TheDefaultLayoutIsOneNewFolder()
    {
        var result = _parser.Parse("/unzip photos.zip", new ReferenceContext(Work, []));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(UnzipLayout.NewFolder, result.Invocation!.Layout);
    }

    /// <summary>
    /// No switch means "the user did not say", so the Settings default applies rather than a
    /// hardcoded one.
    /// </summary>
    [TestMethod]
    public void WithoutASwitchTheCollisionChoiceIsLeftToSettings()
    {
        var result = _parser.Parse("/unzip photos.zip", new ReferenceContext(Work, []));

        Assert.IsTrue(result.Succeeded);
        Assert.IsNull(result.Invocation!.CollisionPolicy);
        Assert.IsNull(result.Invocation.SkipPreview);
    }

    [TestMethod]
    public void SkipAndOverwriteEachOverrideTheSetting()
    {
        var skip = _parser.Parse("/unzip -skip photos.zip", new ReferenceContext(Work, []));
        var overwrite = _parser.Parse("/unzip -overwrite photos.zip", new ReferenceContext(Work, []));

        Assert.AreEqual(CollisionPolicy.Skip, skip.Invocation!.CollisionPolicy);
        Assert.AreEqual(CollisionPolicy.Overwrite, overwrite.Invocation!.CollisionPolicy);
    }

    [TestMethod]
    public void SkipAndOverwriteTogetherAreRefusedRatherThanRanked()
    {
        var result = _parser.Parse("/unzip -skip -overwrite photos.zip", new ReferenceContext(Work, []));

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Error, "not both");
    }

    [TestMethod]
    public void MinusYSkipsThePreview()
    {
        var result = _parser.Parse("/unzip -y photos.zip", new ReferenceContext(Work, []));

        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(result.Invocation!.SkipPreview);
    }

    [TestMethod]
    public void SwitchesMayFollowTheTargets()
    {
        var result = _parser.Parse("/unzip photos.zip -noroot -y", new ReferenceContext(Work, []));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(UnzipLayout.NoRoot, result.Invocation!.Layout);
        Assert.IsTrue(result.Invocation.SkipPreview);
        Assert.AreEqual(1, result.Invocation.ArchivePaths.Count);
    }

    [TestMethod]
    public void AnUnknownSwitchNamesTheOnesThatExist()
    {
        var result = _parser.Parse("/unzip -force photos.zip", new ReferenceContext(Work, []));

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Error, "-noroot");
    }

    [TestMethod]
    public void ATargetThatIsNotAnArchiveIsRefusedByName()
    {
        var result = _parser.Parse("/unzip notes.txt", new ReferenceContext(Work, []));

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Error, "notes.txt is not an archive.");
    }

    /// <summary>
    /// A format Filekin recognizes but this build cannot open earns a better error than "not an
    /// archive", because the user is not confused — the build is limited.
    /// </summary>
    [TestMethod]
    public void AnUnsupportedArchiveFormatSaysWhatIsSupported()
    {
        var result = _parser.Parse("/unzip bundle.7z", new ReferenceContext(Work, []));

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Error, ".zip");
    }

    [TestMethod]
    public void AMultiItemReferenceCannotBeTheDestination()
    {
        var result = _parser.Parse("/unzip one.zip @selection", new ReferenceContext(Work, ArchiveAndFile));

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Error, "must be one folder");
    }

    [TestMethod]
    public void AQuotedArchiveWithSpacesStaysOneTarget()
    {
        var result = _parser.Parse(@"/unzip ""my photos.zip""", new ReferenceContext(Work, []));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, result.Invocation!.ArchivePaths.Count);
        Assert.AreEqual(@"D:\Work\my photos.zip", result.Invocation.ArchivePaths[0]);
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
