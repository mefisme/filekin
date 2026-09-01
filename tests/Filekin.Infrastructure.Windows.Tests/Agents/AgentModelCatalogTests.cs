using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.Infrastructure.Windows.Tests.Agents;

[TestClass]
public sealed class AgentModelCatalogTests
{
    private static readonly string[] ClaudeEfforts = ["low", "medium", "high", "xhigh", "max"];

    private static readonly string[] ClaudeModels = ["opus", "sonnet", "haiku", "opusplan"];

    [TestMethod]
    public async Task ClaudeOffersOnlyDocumentedSubscriptionSafeAliases()
    {
        var models = await new AgentModelCatalog().ReadAsync(AgentProvider.ClaudeCode);

        CollectionAssert.AreEqual(ClaudeModels, models.Select(model => model.Id).ToArray());
        Assert.IsFalse(models.Any(model => model.Id == "fable"));
        Assert.IsFalse(models.Any(model => model.Id == "best"));
        Assert.IsFalse(models.Any(model => model.Id.Contains("1m", StringComparison.OrdinalIgnoreCase)));
        Assert.IsEmpty(models.Single(model => model.Id == "haiku").Efforts);
        Assert.IsTrue(models
            .Where(model => model.Id != "haiku")
            .All(model => model.Efforts.SequenceEqual(ClaudeEfforts)));
    }
}
