using Filekin.Core.Commands.App.Info;
using Filekin.Core.Commands.References;

namespace Filekin.Core.Tests.Commands.App.Info;

[TestClass]
public sealed class InfoInvocationParserTests
{
    private static readonly string[] SelectedItems = [@"D:\Work\one.txt", @"D:\Work\two.txt"];
    private static readonly string[] CurrentFolder = [@"D:\Work"];
    private static readonly string[] ToolsFolder = [@"D:\Tool Box"];
    private static readonly string[] LocalFile = [@"D:\Work\notes.txt"];

    private readonly InfoInvocationParser _parser = new(
        new ReferenceResolver(new NamedLocations()));

    [TestMethod]
    public void BareInfoDescribesTheSelection()
    {
        var result = _parser.Parse("/info", new ReferenceContext(@"D:\Work", SelectedItems));

        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEqual(SelectedItems, result.Invocation!.Targets.ToArray());
    }

    [TestMethod]
    public void BareInfoFallsBackToTheVisibleFolder()
    {
        var result = _parser.Parse("/info", new ReferenceContext(@"D:\Work", []));

        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEqual(CurrentFolder, result.Invocation!.Targets.ToArray());
    }

    [TestMethod]
    public void BareInfoWithNoFolderAndNoSelectionExplainsItself()
    {
        var result = _parser.Parse("/info", new ReferenceContext(null, []));

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Error, "Select something");
    }

    [TestMethod]
    public void ALocationReferenceResolvesToItsPath()
    {
        var result = _parser.Parse("/info @tools", new ReferenceContext(@"D:\Work", []));

        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEqual(ToolsFolder, result.Invocation!.Targets.ToArray());
    }

    [TestMethod]
    public void ARelativeTargetResolvesAgainstTheVisibleFolder()
    {
        var result = _parser.Parse("/info notes.txt", new ReferenceContext(@"D:\Work", []));

        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEqual(LocalFile, result.Invocation!.Targets.ToArray());
    }

    [TestMethod]
    public void AnExplicitSelectionKeepsEveryItem()
    {
        var result = _parser.Parse("/info @selection", new ReferenceContext(@"D:\Work", SelectedItems));

        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEqual(SelectedItems, result.Invocation!.Targets.ToArray());
    }

    [TestMethod]
    public void AQuotedTargetWithSpacesStaysOneTarget()
    {
        var result = _parser.Parse(@"/info ""my notes.txt""", new ReferenceContext(@"D:\Work", []));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, result.Invocation!.Targets.Count);
        Assert.AreEqual(@"D:\Work\my notes.txt", result.Invocation.Targets[0]);
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
