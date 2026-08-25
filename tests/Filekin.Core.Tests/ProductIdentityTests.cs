using Filekin.Core;

namespace Filekin.Core.Tests;

[TestClass]
public sealed class ProductIdentityTests
{
    [TestMethod]
    public void NameMatchesConfirmedProductName()
    {
        Assert.AreEqual("Filekin", ProductIdentity.Name);
    }

    [TestMethod]
    public void CategoryDescriptionMatchesConfirmedCopy()
    {
        Assert.AreEqual(
            "Filekin — a keyboard-first Windows file manager + terminal.",
            ProductIdentity.CategoryDescription);
    }
}
