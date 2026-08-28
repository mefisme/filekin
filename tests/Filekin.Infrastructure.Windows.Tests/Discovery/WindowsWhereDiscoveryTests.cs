using Filekin.Core.Discovery;
using Filekin.Infrastructure.Windows.Discovery;

namespace Filekin.Infrastructure.Windows.Tests.Discovery;

[TestClass]
public sealed class WindowsWhereDiscoveryTests
{
    [TestMethod]
    public async Task RegisteredAndPathMatchesAreDeduplicatedWithUserPathStatus()
    {
        using var fixture = new DiscoveryFixture();
        var executable = fixture.File(@"Tools\python.exe");
        var install = Path.GetDirectoryName(executable)!;
        var discovery = fixture.Create(
            registrations:
            [
                new WindowsApplicationRegistration(
                    "Python 3.13",
                    executable,
                    install + Path.DirectorySeparatorChar,
                    "Installed app · User"),
            ],
            userPath: install);

        var result = await discovery.DiscoverAsync("python");

        var executableResult = result.Locations.Single(location => location.Path == executable);
        Assert.AreEqual(WhereLocationKind.Executable, executableResult.Kind);
        Assert.IsTrue(executableResult.PathScope.HasFlag(WherePathScope.User));
        StringAssert.Contains(executableResult.Sources, "Installed app");
        StringAssert.Contains(executableResult.Sources, "PATH");
        Assert.AreEqual(2, result.Locations.Count);
    }

    [TestMethod]
    public async Task FriendlyApplicationNameLearnsAliasForInstallAndAppDataFolders()
    {
        using var fixture = new DiscoveryFixture();
        var executable = fixture.File(@"Program Files\Microsoft VS Code\Code.exe");
        var installation = Path.GetDirectoryName(executable)!;
        var data = fixture.Directory(@"AppData\Code");
        var cache = fixture.Directory(@"AppData\Code\Cache");
        var configuration = fixture.Directory(@"Profile\.vscode");
        var extensions = fixture.Directory(@"Profile\.vscode\extensions");
        var discovery = fixture.Create(
            registrations:
            [
                new WindowsApplicationRegistration(
                    "Microsoft Visual Studio Code",
                    executable,
                    installation,
                    "Installed app · Machine"),
            ],
            roots:
            [
                new WhereSearchRoot(fixture.Directory("Program Files"), WhereSearchRootKind.Installation, 3, "Program Files"),
                new WhereSearchRoot(fixture.Directory("AppData"), WhereSearchRootKind.UserData, 1, "Local AppData"),
                new WhereSearchRoot(fixture.Directory("Profile"), WhereSearchRootKind.Configuration, 0, "User profile"),
            ]);

        var result = await discovery.DiscoverAsync("Visual Studio Code");

        CollectionAssert.IsSubsetOf(
            new[] { executable, installation, data, cache, configuration, extensions },
            result.Locations.Select(location => location.Path).ToArray());
        Assert.AreEqual(WhereLocationKind.UserData, result.Locations.Single(location => location.Path == data).Kind);
        Assert.IsTrue(result.Locations.Single(location => location.Path == executable).IsFile);
        Assert.IsFalse(result.Locations.Single(location => location.Path == cache).IsFile);
    }

    [TestMethod]
    public async Task MatchingStartMenuShortcutAddsTheShortcutAndItsTarget()
    {
        using var fixture = new DiscoveryFixture();
        var executable = fixture.File(@"Apps\Codex\codex.exe");
        var shortcut = fixture.File(@"Start Menu\Codex.lnk");
        var discovery = fixture.Create(
            shortcuts: new Dictionary<string, string?> { [shortcut] = executable });

        var result = await discovery.DiscoverAsync("codex");

        Assert.AreEqual(WhereLocationKind.Executable, result.Locations.Single(location => location.Path == executable).Kind);
        Assert.AreEqual(WhereLocationKind.Shortcut, result.Locations.Single(location => location.Path == shortcut).Kind);
    }

    [TestMethod]
    public async Task ARegistrationReachedThroughALearnedNameDoesNotWidenTheSearchAgain()
    {
        using var fixture = new DiscoveryFixture();
        var executable = fixture.File(@"Programs\Microsoft VS Code\Code.exe");
        var installation = Path.GetDirectoryName(executable)!;
        var unrelated = fixture.File(@"Nvidia\nvcontainer.exe");
        var unrelatedFolder = fixture.Directory(@"Roots\NVIDIA Corporation");
        var discovery = fixture.Create(
            registrations:
            [
                new WindowsApplicationRegistration(
                    "Microsoft Visual Studio Code (User)",
                    executable,
                    installation,
                    "Installed app · User"),
                new WindowsApplicationRegistration(
                    "NVIDIA User Container",
                    unrelated,
                    Path.GetDirectoryName(unrelated)!,
                    "Installed app · Machine"),
            ],
            roots: [new WhereSearchRoot(fixture.Directory("Roots"), WhereSearchRootKind.Installation, 2, "Program Files")]);

        var result = await discovery.DiscoverAsync("Visual Studio Code");
        var paths = result.Locations.Select(location => location.Path).ToArray();

        // "(User)" in a friendly name must never become the alias that selects NVIDIA User Container,
        // whose own name would then teach "nvidia" and pull in the rest of the machine.
        CollectionAssert.Contains(paths, executable);
        CollectionAssert.DoesNotContain(paths, unrelated);
        CollectionAssert.DoesNotContain(paths, unrelatedFolder);
    }

    [TestMethod]
    public async Task ShortcutToADocumentIsListedWithoutTeachingItsFileName()
    {
        using var fixture = new DiscoveryFixture();
        var manual = fixture.File(@"Python313\Doc\html\index.html");
        var unrelatedIndex = fixture.Directory(@"Roots\Go\src\index");
        var shortcut = fixture.File(@"Start Menu\Python 3.13 Manuals.lnk");
        var discovery = fixture.Create(
            shortcuts: new Dictionary<string, string?> { [shortcut] = manual },
            roots: [new WhereSearchRoot(fixture.Directory("Roots"), WhereSearchRootKind.Installation, 3, "Program Files")]);

        var result = await discovery.DiscoverAsync("python");
        var paths = result.Locations.Select(location => location.Path).ToArray();

        CollectionAssert.Contains(paths, manual);
        CollectionAssert.DoesNotContain(paths, unrelatedIndex);
    }

    [TestMethod]
    public async Task CancellationStopsABoundedScan()
    {
        using var fixture = new DiscoveryFixture();
        var discovery = fixture.Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => discovery.DiscoverAsync("python", cancellationToken: cancellation.Token));
    }

    [TestMethod]
    public async Task UnreadableRegistrationsShortcutRootsAndTargetsAreReported()
    {
        using var fixture = new DiscoveryFixture();
        var shortcut = fixture.File(@"Start Menu\Python.lnk");
        var discovery = fixture.Create(
            shortcuts: new Dictionary<string, string?> { [shortcut] = null },
            unreadableRegistrations: 2,
            unreadableShortcutRoots: 1,
            unreadableShortcuts: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { shortcut });

        var result = await discovery.DiscoverAsync("python");

        Assert.AreEqual(4, result.UnreadableLocations);
    }

    private sealed class DiscoveryFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "Filekin.Where.Tests", Guid.NewGuid().ToString("N"));

        public DiscoveryFixture() => System.IO.Directory.CreateDirectory(_root);

        public string Directory(string relative)
        {
            var path = Path.Combine(_root, relative);
            System.IO.Directory.CreateDirectory(path);
            return path;
        }

        public string File(string relative)
        {
            var path = Path.Combine(_root, relative);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, string.Empty);
            return path;
        }

        public WindowsWhereDiscovery Create(
            IReadOnlyList<WindowsApplicationRegistration>? registrations = null,
            IReadOnlyDictionary<string, string?>? shortcuts = null,
            string? userPath = null,
            IReadOnlyList<WhereSearchRoot>? roots = null,
            int unreadableRegistrations = 0,
            int unreadableShortcutRoots = 0,
            IReadOnlySet<string>? unreadableShortcuts = null)
        {
            ObjectDisposedException.ThrowIf(!System.IO.Directory.Exists(_root), this);
            return new WindowsWhereDiscovery(
                new FakeRegistrations(registrations ?? [], unreadableRegistrations),
                new FakeShortcuts(
                    shortcuts ?? new Dictionary<string, string?>(),
                    unreadableShortcutRoots,
                    unreadableShortcuts ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase)),
                () => new WindowsWherePathValues(
                    null,
                    userPath,
                    null,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".cmd" }),
                () => roots ?? []);
        }

        public void Dispose() => System.IO.Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeRegistrations(
        IReadOnlyList<WindowsApplicationRegistration> registrations,
        int unreadableLocations)
        : IWindowsApplicationRegistrationSource
    {
        public WindowsApplicationRegistrationOutcome GetRegistrations(CancellationToken cancellationToken) =>
            new(registrations, unreadableLocations);
    }

    private sealed class FakeShortcuts(
        IReadOnlyDictionary<string, string?> shortcuts,
        int unreadableLocations,
        IReadOnlySet<string> unreadableShortcuts) : IWindowsShortcutSource
    {
        public WindowsShortcutEnumerationOutcome GetShortcutPaths(CancellationToken cancellationToken) =>
            new([.. shortcuts.Keys], unreadableLocations);

        public string? TryGetTarget(string shortcutPath, out bool unreadable)
        {
            unreadable = unreadableShortcuts.Contains(shortcutPath);
            return shortcuts.GetValueOrDefault(shortcutPath);
        }
    }
}
