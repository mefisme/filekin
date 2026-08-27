using Filekin.Core.Commands.App.Go;
using Filekin.Core.Commands.References;

namespace Filekin.Core.Tests.Commands.App.Go;

[TestClass]
public sealed class GoInvocationParserTests
{
    private const string Work = @"D:\Work";

    private readonly GoInvocationParser _parser = new(
        new ReferenceResolver(new NamedLocations()));

    [TestMethod]
    public void UnquotedAbsolutePathMayContainSpaces()
    {
        var result = _parser.Parse(@"/go D:\Client Work\Current Project", Context());

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(@"D:\Client Work\Current Project", result.Invocation!.FolderPath);
    }

    [TestMethod]
    public void UnquotedRelativePathMayContainSpaces()
    {
        var result = _parser.Parse(@"/go Client Work\Current Project", Context());

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(@"D:\Work\Client Work\Current Project", result.Invocation!.FolderPath);
    }

    [TestMethod]
    [DataRow(@"/go ""D:\Client Work""")]
    [DataRow("/go 'D:\\Client Work'")]
    public void FamiliarOuterQuotesRemainAccepted(string input)
    {
        var result = _parser.Parse(input, Context());

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(@"D:\Client Work", result.Invocation!.FolderPath);
    }

    [TestMethod]
    public void NamedLocationReferenceMayIncludeAnUnquotedSpaceBearingSubpath()
    {
        var result = _parser.Parse(@"/go @projects\Client Work", Context());

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(@"D:\Projects\Client Work", result.Invocation!.FolderPath);
    }

    [TestMethod]
    public void OneSelectedFolderCanBeTheTarget()
    {
        var result = _parser.Parse(
            "/go @selection",
            new ReferenceContext(Work, [@"D:\Selected Folder"]));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(@"D:\Selected Folder", result.Invocation!.FolderPath);
    }

    [TestMethod]
    public void SeveralSelectedItemsAreRejected()
    {
        var result = _parser.Parse(
            "/go @selection",
            new ReferenceContext(Work, [@"D:\One", @"D:\Two"]));

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Error, "one folder");
    }

    [TestMethod]
    public void EmptySelectionIsRejected()
    {
        var result = _parser.Parse("/go @selection", Context());

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Error, "no folders");
    }

    [TestMethod]
    public void BareGoShowsUsage()
    {
        var result = _parser.Parse("/go", Context());

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Error, "Usage: /go <folder>");
    }

    [TestMethod]
    public void ALongerCommandNameIsNotMistakenForGo()
    {
        var result = _parser.Parse("/good D:\\Somewhere", Context());

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Error, "Usage");
    }

    private static ReferenceContext Context() => new(Work, []);

    private sealed class NamedLocations : INamedLocationResolver
    {
        public bool TryResolve(string name, out string path)
        {
            if (name.Equals("projects", StringComparison.OrdinalIgnoreCase))
            {
                path = @"D:\Projects";
                return true;
            }

            path = string.Empty;
            return false;
        }
    }
}
