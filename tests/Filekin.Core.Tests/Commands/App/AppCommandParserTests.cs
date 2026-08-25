using Filekin.Core.Commands.App;

namespace Filekin.Core.Tests.Commands.App;

[TestClass]
public sealed class AppCommandParserTests
{
    [TestMethod]
    public void NonSlashInputIsNotAnApplicationCommand()
    {
        Assert.IsFalse(AppCommandParser.TryParse("git status", out _));
    }

    [TestMethod]
    public void BareSlashHasNoCommandName()
    {
        Assert.IsFalse(AppCommandParser.TryParse("/", out _));
        Assert.IsFalse(AppCommandParser.TryParse("/   ", out _));
    }

    [TestMethod]
    public void CommandNameIsLowerCasedAndArgumentsAreSplit()
    {
        Assert.IsTrue(AppCommandParser.TryParse("/Copy a.txt b.txt", out var command));

        Assert.AreEqual("copy", command.Name);
        Assert.AreEqual("a.txt|b.txt", string.Join('|', command.Arguments));
    }

    [TestMethod]
    public void QuotesGroupArgumentsContainingSpaces()
    {
        Assert.IsTrue(AppCommandParser.TryParse("/move \"my file.txt\" 'dest folder'", out var command));

        Assert.AreEqual("move", command.Name);
        Assert.AreEqual("my file.txt|dest folder", string.Join('|', command.Arguments));
    }

    [TestMethod]
    public void EmptyQuotesProduceAnEmptyArgument()
    {
        Assert.IsTrue(AppCommandParser.TryParse("/rename target \"\"", out var command));

        Assert.AreEqual("target|", string.Join('|', command.Arguments));
    }

    [TestMethod]
    public void LeadingWhitespaceAndNoArgumentsAreHandled()
    {
        Assert.IsTrue(AppCommandParser.TryParse("   /history", out var command));

        Assert.AreEqual("history", command.Name);
        Assert.AreEqual(0, command.Arguments.Count);
    }
}
