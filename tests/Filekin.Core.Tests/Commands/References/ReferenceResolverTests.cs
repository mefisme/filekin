using Filekin.Core.Commands.References;

namespace Filekin.Core.Tests.Commands.References;

[TestClass]
public sealed class ReferenceResolverTests
{
    [TestMethod]
    public void ThisFolderResolvesToTheCurrentFolderQuoted()
    {
        var resolver = CreateResolver();

        var line = resolver.ResolveLine("Get-Content @thisfolder", Ctx(@"D:\Work"));

        Assert.AreEqual(@"Get-Content 'D:\Work'", line);
    }

    [TestMethod]
    public void AReferenceSubpathIsCombinedOntoTheResolvedPath()
    {
        var resolver = CreateResolver();

        var line = resolver.ResolveLine(@"/run @thisfolder\tools\app.exe", Ctx(@"D:\Work"));

        Assert.AreEqual(@"/run 'D:\Work\tools\app.exe'", line);
    }

    [TestMethod]
    public void SelectionExpandsToEveryQuotedSelectedItem()
    {
        var resolver = CreateResolver();

        var line = resolver.ResolveLine("@selection", Ctx(@"D:\Work", @"D:\a.txt", @"D:\b c.txt"));

        Assert.AreEqual(@"'D:\a.txt' 'D:\b c.txt'", line);
    }

    [TestMethod]
    public void KnownReferenceWinsEvenWhereItWouldBePowerShellSplatting()
    {
        var resolver = CreateResolver();

        // '@selection' would otherwise splat a $selection variable; in the command bar the workspace
        // reference wins (DECISIONS.md, 2026-08-25).
        var line = resolver.ResolveLine("Do-Thing @selection", Ctx(@"D:\Work", @"D:\a.txt"));

        Assert.AreEqual(@"Do-Thing 'D:\a.txt'", line);
    }

    [TestMethod]
    public void UnknownReferenceIsLeftUntouched()
    {
        var resolver = CreateResolver();

        var line = resolver.ResolveLine("echo @notareference", Ctx(@"D:\Work"));

        Assert.AreEqual("echo @notareference", line);
    }

    [TestMethod]
    [DataRow("$x = @(1,2)")]
    [DataRow("$h = @{ a = 1 }")]
    [DataRow("Invoke-Thing @args")]
    [DataRow("mail someone@example.com now")]
    public void NativePowerShellAtSyntaxPassesThrough(string input)
    {
        var resolver = CreateResolver();

        Assert.AreEqual(input, resolver.ResolveLine(input, Ctx(@"D:\Work")));
    }

    [TestMethod]
    public void EmbeddedSingleQuotesAreDoubledForPowerShell()
    {
        var resolver = CreateResolver();

        var line = resolver.ResolveLine("@selection", Ctx(@"D:\Work", @"D:\o'brien.txt"));

        Assert.AreEqual(@"'D:\o''brien.txt'", line);
    }

    [TestMethod]
    public void ThisFolderIsLeftUntouchedOnANonFilesystemLocation()
    {
        var resolver = CreateResolver();

        var line = resolver.ResolveLine("@thisfolder", new ReferenceContext(currentFolderPath: null, []));

        Assert.AreEqual("@thisfolder", line);
    }

    [TestMethod]
    public void NamedLocationsAreResolvedThroughThePort()
    {
        var resolver = CreateResolver(new Dictionary<string, string> { ["downloads"] = @"D:\Users\me\Downloads" });

        var line = resolver.ResolveLine(@"/unzip pack.zip @downloads\out", Ctx(@"D:\Work"));

        Assert.AreEqual(@"/unzip pack.zip 'D:\Users\me\Downloads\out'", line);
    }

    [TestMethod]
    public void ResolveReferenceReportsKnownAndUnknownNames()
    {
        var resolver = CreateResolver();

        Assert.IsTrue(resolver.ResolveReference("ThisFolder", Ctx(@"D:\Work")).IsKnownReference);
        Assert.IsFalse(resolver.ResolveReference("nope", Ctx(@"D:\Work")).IsKnownReference);
    }

    private static ReferenceResolver CreateResolver(Dictionary<string, string>? namedLocations = null)
    {
        INamedLocationResolver locations = namedLocations is null
            ? EmptyNamedLocationResolver.Instance
            : new FakeNamedLocations(namedLocations);
        return new ReferenceResolver(locations);
    }

    private static ReferenceContext Ctx(string folder, params string[] selection) => new(folder, selection);

    private sealed class FakeNamedLocations : INamedLocationResolver
    {
        private readonly Dictionary<string, string> _locations;

        public FakeNamedLocations(Dictionary<string, string> locations)
        {
            _locations = new Dictionary<string, string>(locations, StringComparer.OrdinalIgnoreCase);
        }

        public bool TryResolve(string name, out string path)
        {
            if (_locations.TryGetValue(name, out var resolved))
            {
                path = resolved;
                return true;
            }

            path = string.Empty;
            return false;
        }
    }
}
