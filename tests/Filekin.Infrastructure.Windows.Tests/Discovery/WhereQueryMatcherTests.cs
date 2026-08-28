using Filekin.Infrastructure.Windows.Discovery;

namespace Filekin.Infrastructure.Windows.Tests.Discovery;

[TestClass]
public sealed class WhereQueryMatcherTests
{
    [TestMethod]
    public void SimpleExecutableMatchesVersionedLaunchersButNotUnrelatedPrefixes()
    {
        var matcher = new WhereQueryMatcher("python");

        Assert.IsTrue(matcher.MatchesExecutable(@"C:\Tools\python.exe"));
        Assert.IsTrue(matcher.MatchesExecutable(@"C:\Tools\python313.exe"));
        Assert.IsFalse(matcher.MatchesExecutable(@"C:\Tools\pythonw.exe"));
        Assert.IsFalse(matcher.MatchesExecutable(@"C:\Tools\mypython.exe"));
    }

    [TestMethod]
    public void LearnedAuthoritativeNameConnectsFriendlyNameToExecutableAlias()
    {
        var matcher = new WhereQueryMatcher("Visual Studio Code");

        Assert.IsTrue(matcher.MatchesLabel("Microsoft Visual Studio Code"));
        Assert.IsFalse(matcher.MatchesExecutable(@"C:\Apps\Code.exe"));

        matcher.LearnFrom("Microsoft Visual Studio Code");

        Assert.IsTrue(matcher.MatchesExecutable(@"C:\Apps\Code.exe"));
        Assert.IsTrue(matcher.MatchesLabel("Code"));
    }

    [TestMethod]
    public void ALearnedShortNameHasToBeTheWholeNameOfWhatItMatches()
    {
        var matcher = new WhereQueryMatcher("Visual Studio Code");
        matcher.LearnFrom(@"C:\Users\me\AppData\Local\Programs\Microsoft VS Code\Code.exe");
        matcher.LearnFrom(@"C:\Users\me\AppData\Local\Programs\Microsoft VS Code\");

        Assert.IsTrue(matcher.MatchesLabel("Code"));
        Assert.IsTrue(matcher.MatchesLabel(".vscode"));
        Assert.IsTrue(matcher.MatchesLabel("Microsoft VS Code"));

        // Every Electron application keeps a "Code Cache" folder, and none of them is VS Code.
        Assert.IsFalse(matcher.MatchesLabel("Code Cache"));
        Assert.IsFalse(matcher.MatchesLabel("Unicode"));
    }

    [TestMethod]
    public void PublisherArchitectureAndFolderRoleNamesAreNeverLearned()
    {
        var matcher = new WhereQueryMatcher("Visual Studio Code");
        matcher.LearnFrom(@"C:\Program Files\Google\Chrome\Application");
        matcher.LearnFrom(@"C:\Program Files\Git\bin");
        matcher.LearnFrom(@"C:\Program Files\Python313\python-3.13.15-amd64.exe");

        Assert.IsFalse(matcher.MatchesLabel("Application"));
        Assert.IsFalse(matcher.MatchesLabel("bin"));
        Assert.IsFalse(matcher.MatchesLabel("amd64"));
        Assert.IsFalse(matcher.MatchesLabel("windows_amd64"));
    }
}
