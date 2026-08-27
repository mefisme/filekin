using Filekin.Core.Commands.App.Run;
using Filekin.Core.Commands.References;

namespace Filekin.Core.Tests.Commands.App.Run;

[TestClass]
public sealed class RunInvocationParserTests
{
    private static readonly string[] SnapmapTarget = ["snapmap-midi"];
    private static readonly string[] ProjectArguments = ["--project", @"D:\Work"];
    private static readonly string[] LocationTarget = [@"D:\Tool Box\snapmap-midi.exe"];
    private static readonly string[] VerboseArgument = ["--verbose"];
    private static readonly string[] SelectionTargets = [@"D:\one.lnk", @"D:\two.pdf"];
    private static readonly string[] UnknownReferenceTarget = [@"@nowhere\tool.exe"];
    private static readonly string[] QuotedTarget = [@"C:\Program Files\tool.exe"];

    private readonly RunInvocationParser _parser = new(
        new ReferenceResolver(new NamedLocations()));

    [TestMethod]
    public void RelativeTargetAndArgumentsRemainDistinct()
    {
        var result = _parser.Parse(
            "/run snapmap-midi --project @thisfolder",
            Context());

        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEqual(SnapmapTarget, result.Invocation!.Targets.ToArray());
        CollectionAssert.AreEqual(ProjectArguments, result.Invocation.Arguments.ToArray());
    }

    [TestMethod]
    public void LocationReferenceCanAnchorTheTargetPath()
    {
        var result = _parser.Parse(
            @"/run @tools\snapmap-midi.exe --verbose",
            Context());

        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEqual(LocationTarget, result.Invocation!.Targets.ToArray());
        CollectionAssert.AreEqual(VerboseArgument, result.Invocation.Arguments.ToArray());
    }

    [TestMethod]
    public void SelectionExpandsToMultipleTargets()
    {
        var result = _parser.Parse(
            "/run @selection",
            new ReferenceContext(@"D:\Work", [@"D:\one.lnk", @"D:\two.pdf"]));

        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEqual(SelectionTargets, result.Invocation!.Targets.ToArray());
        Assert.AreEqual(0, result.Invocation.Arguments.Count);
    }

    [TestMethod]
    public void ArgumentsWithMultipleTargetsAreRejectedAsAmbiguous()
    {
        var result = _parser.Parse(
            "/run @selection --verbose",
            new ReferenceContext(@"D:\Work", [@"D:\one.exe", @"D:\two.exe"]));

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Error, "only when /run has one target");
    }

    [TestMethod]
    public void EmptySelectionIsRejected()
    {
        var result = _parser.Parse("/run @selection", Context());

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Error, "no items");
    }

    [TestMethod]
    public void BareRunShowsUsage()
    {
        var result = _parser.Parse("/run", Context());

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Error, "Usage");
    }

    [TestMethod]
    public void AnUnknownReferenceIsLeftForTheTargetResolverToJudge()
    {
        var result = _parser.Parse(@"/run @nowhere\tool.exe", Context());

        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEqual(UnknownReferenceTarget, result.Invocation!.Targets.ToArray());
    }

    [TestMethod]
    public void AQuotedTargetWithSpacesStaysOneTarget()
    {
        var result = _parser.Parse(@"/run ""C:\Program Files\tool.exe"" --verbose", Context());

        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEqual(QuotedTarget, result.Invocation!.Targets.ToArray());
        CollectionAssert.AreEqual(VerboseArgument, result.Invocation.Arguments.ToArray());
    }

    private static ReferenceContext Context() => new(@"D:\Work", []);

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
