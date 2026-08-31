using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.Infrastructure.Windows.Tests.Agents;

[TestClass]
public sealed class AgentRunPromptTests
{
    [TestMethod]
    public void TheOpeningPromptAsksForAClockInWithoutNamingASession()
    {
        var prompt = AgentRunPrompt.Create("Create hello.txt.");

        StringAssert.Contains(prompt, "Create hello.txt.");
        StringAssert.Contains(prompt, "filekin_clock_in");
        Assert.IsFalse(
            prompt.Contains("nativeSessionId", StringComparison.OrdinalIgnoreCase),
            "Session identity is enforced out of band by Filekin, never asked for in model prose.");
    }
}
