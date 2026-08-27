using Filekin.Core.Commands.References;

namespace Filekin.Core.Tests.Commands.References;

[TestClass]
public sealed class NamedLocationResolverTests
{
    [TestMethod]
    public void UserLocationsAreReplacedAsOneCaseInsensitiveSnapshot()
    {
        var resolver = new UserNamedLocationResolver();
        resolver.Replace([new NamedLocation("Projects", @"D:\Work\Projects")]);

        Assert.IsTrue(resolver.TryResolve("projects", out var firstPath));
        Assert.AreEqual(@"D:\Work\Projects", firstPath);

        resolver.Replace([new NamedLocation("Archive", @"E:\Archive")]);

        Assert.IsFalse(resolver.TryResolve("projects", out _));
        Assert.IsTrue(resolver.TryResolve("ARCHIVE", out var secondPath));
        Assert.AreEqual(@"E:\Archive", secondPath);
    }

    [TestMethod]
    public void FirstCompositeResolverWins()
    {
        var user = new UserNamedLocationResolver();
        user.Replace([new NamedLocation("downloads", @"D:\Sorted Downloads")]);
        var fallback = new UserNamedLocationResolver();
        fallback.Replace([new NamedLocation("downloads", @"C:\Users\Me\Downloads")]);
        var resolver = new CompositeNamedLocationResolver(user, fallback);

        Assert.IsTrue(resolver.TryResolve("downloads", out var path));
        Assert.AreEqual(@"D:\Sorted Downloads", path);
    }

    [TestMethod]
    public void CompositeFallsThroughToLaterResolvers()
    {
        var empty = new UserNamedLocationResolver();
        var fallback = new UserNamedLocationResolver();
        fallback.Replace([new NamedLocation("home", @"C:\Users\Me")]);
        var resolver = new CompositeNamedLocationResolver(empty, fallback);

        Assert.IsTrue(resolver.TryResolve("home", out var path));
        Assert.AreEqual(@"C:\Users\Me", path);
    }
}
