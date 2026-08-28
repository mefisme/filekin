using Filekin.Core.Commands.App.Tidy;
using Filekin.Core.Commands.References;

namespace Filekin.Core.Tests.Tidy;

[TestClass]
public sealed class TidyInvocationParserTests
{
    private const string Current = @"D:\Work";

    [TestMethod]
    public void BareTidyTargetsTheVisibleFolder()
    {
        var result = Parse("/tidy");

        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.AreEqual(Current, result.Invocation!.FolderPath);
        Assert.IsNull(result.Invocation.SkipPreview);
    }

    [TestMethod]
    public void AnUnquotedPathWithSpacesIsOneTarget()
    {
        var result = Parse(@"/tidy C:\Program Files");

        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.AreEqual(@"C:\Program Files", result.Invocation!.FolderPath);
    }

    [TestMethod]
    public void ARelativeTargetResolvesAgainstTheVisibleFolder()
    {
        var result = Parse("/tidy Inbox");

        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.AreEqual(Path.Combine(Current, "Inbox"), result.Invocation!.FolderPath);
    }

    [TestMethod]
    public void MatchingOuterQuotesAreAccepted()
    {
        var result = Parse(@"/tidy ""C:\Program Files""");

        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.AreEqual(@"C:\Program Files", result.Invocation!.FolderPath);
    }

    [TestMethod]
    [DataRow("/tidy -y")]
    [DataRow("/tidy -yes")]
    public void TheSkipSwitchIsRecognizedOnItsOwn(string input)
    {
        var result = Parse(input);

        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.IsTrue(result.Invocation!.SkipPreview);
        Assert.AreEqual(Current, result.Invocation.FolderPath);
    }

    [TestMethod]
    public void TheSkipSwitchMayPrecedeAFolderWithSpaces()
    {
        var result = Parse(@"/tidy -y C:\Program Files");

        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.IsTrue(result.Invocation!.SkipPreview);
        Assert.AreEqual(@"C:\Program Files", result.Invocation.FolderPath);
    }

    [TestMethod]
    public void AFolderStartingWithAHyphenIsStillATarget()
    {
        var result = Parse("/tidy -weird-folder");

        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.IsNull(result.Invocation!.SkipPreview);
        Assert.AreEqual(Path.Combine(Current, "-weird-folder"), result.Invocation.FolderPath);
    }

    [TestMethod]
    public void AReferenceResolvingToSeveralItemsIsRefused()
    {
        var result = Parse("/tidy @selection", new FakeResolver(["a", "b"]));

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Error!, "one folder");
    }

    [TestMethod]
    public void AReferenceResolvingToOneFolderIsUsed()
    {
        var result = Parse("/tidy @downloads", new FakeResolver([@"C:\Users\me\Downloads"]));

        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.AreEqual(@"C:\Users\me\Downloads", result.Invocation!.FolderPath);
    }

    [TestMethod]
    public void AnotherCommandIsNotParsedAsTidy()
    {
        var result = Parse("/tidyup");

        Assert.IsFalse(result.Succeeded);
    }

    private static TidyInvocationParseResult Parse(string input, IReferenceResolver? references = null) =>
        new TidyInvocationParser(references ?? new FakeResolver(null)).Parse(
            input,
            new ReferenceContext(Current, []));

    /// <summary>Only <see cref="ResolveToken"/> is exercised; the parser never calls the other two.</summary>
    private sealed class FakeResolver(IReadOnlyList<string>? paths) : IReferenceResolver
    {
        public string ResolveLine(string input, ReferenceContext context) => throw new NotSupportedException();

        public ReferenceResolution ResolveReference(string name, ReferenceContext context) =>
            throw new NotSupportedException();

        public ReferenceResolution ResolveToken(string token, ReferenceContext context) =>
            paths is null || !token.StartsWith('@')
                ? ReferenceResolution.Unknown
                : ReferenceResolution.Known(paths);
    }
}
