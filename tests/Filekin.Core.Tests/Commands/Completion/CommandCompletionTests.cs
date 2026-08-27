using Filekin.Core.Commands.Completion;

namespace Filekin.Core.Tests.Commands.Completion;

[TestClass]
public sealed class CommandCompletionTests
{
    private static readonly string[] SlashCommands = ["/recycle", "/rename", "/settings"];

    private static readonly CommandCompletionSuggestion[] Catalog =
    [
        new("/recycle", "Open the Recycle Bin"),
        new("/rename", "Rename a file or folder"),
        new("/settings", "Change Filekin preferences"),
        new("@projects", @"D:\Projects"),
        new("@selection", "Selected files and folders"),
        new("@thisfolder", "Current Files folder"),
    ];

    [TestMethod]
    public void BareSlashDiscoversOnlyApplicationCommands()
    {
        var match = CommandCompletion.Find("/", 1, Catalog);

        Assert.IsNotNull(match);
        CollectionAssert.AreEqual(SlashCommands, match.Suggestions.Select(static suggestion => suggestion.Text).ToArray());
    }

    [TestMethod]
    public void PartialReferenceCanCompleteInsideAnOrdinaryShellLine()
    {
        const string input = "Get-ChildItem @pro";

        var match = CommandCompletion.Find(input, input.Length, Catalog);

        Assert.IsNotNull(match);
        Assert.AreEqual("@pro", match.Prefix);
        Assert.AreEqual("@projects", match.Suggestions.Single().Text);
    }

    [TestMethod]
    public void UnknownReferenceDoesNotClaimPowerShellSyntax()
    {
        const string input = "Write-Output @arguments";

        var match = CommandCompletion.Find(input, input.Length, Catalog);

        Assert.IsNull(match);
    }

    [TestMethod]
    public void EmbeddedAtSignDoesNotStartAReferenceToken()
    {
        const string input = "Write-Output name@pro";

        var match = CommandCompletion.Find(input, input.Length, Catalog);

        Assert.IsNull(match);
    }

    [TestMethod]
    public void SlashOutsideTheLeadingCommandTokenIsNotClaimed()
    {
        const string input = "Write-Output /re";

        var match = CommandCompletion.Find(input, input.Length, Catalog);

        Assert.IsNull(match);
    }

    [TestMethod]
    public void ReferenceSubpathIsNotClaimedAfterTheName()
    {
        const string input = "Get-ChildItem @projects\\src";

        var match = CommandCompletion.Find(input, input.Length, Catalog);

        Assert.IsNull(match);
    }

    [TestMethod]
    public void MatchingIsCaseInsensitiveAndReplacesTheWholeToken()
    {
        const string input = "  /RE rest";
        var match = CommandCompletion.Find(input, 5, Catalog);

        Assert.IsNotNull(match);
        var edit = CommandCompletion.Apply(input, match, match.Suggestions[0]);

        Assert.AreEqual("  /recycle rest", edit.Text);
        Assert.AreEqual(10, edit.CaretIndex);
    }

    [TestMethod]
    public void CommonPrefixStopsWhereAmbiguousCommandsDiverge()
    {
        var suggestions = Catalog.Where(static suggestion => suggestion.Text.StartsWith("/re", StringComparison.Ordinal)).ToArray();

        var prefix = CommandCompletion.CommonPrefix(suggestions);

        Assert.AreEqual("/re", prefix);
    }
}
