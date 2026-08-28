using Filekin.Core.Commands.App.Where;

namespace Filekin.Core.Tests.Commands.App.Where;

[TestClass]
public sealed class WhereInvocationParserTests
{
    [TestMethod]
    public void OneToolNameIsAccepted()
    {
        var result = WhereInvocationParser.Parse("/where python");

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("python", result.Invocation!.Query);
    }

    [TestMethod]
    public void QuotedApplicationNameStaysOneQuery()
    {
        var result = WhereInvocationParser.Parse("/where \"Visual Studio Code\"");

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("Visual Studio Code", result.Invocation!.Query);
    }

    [TestMethod]
    public void MissingQueryExplainsWhatToEnter()
    {
        var result = WhereInvocationParser.Parse("/where");

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Error, "/where python");
    }

    [TestMethod]
    public void UnquotedMultiwordQueryExplainsQuoting()
    {
        var result = WhereInvocationParser.Parse("/where Visual Studio Code");

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Error, "quotes");
    }

    [TestMethod]
    public void SelectionReferenceIsRejectedRatherThanExpandedIntoSeveralQueries()
    {
        var result = WhereInvocationParser.Parse("/where @selection");

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Error, "program or tool name");
    }
}
