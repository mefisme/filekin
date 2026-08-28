using System.Diagnostics;
using Filekin.Core.Discovery;
using Filekin.Infrastructure.Windows.Commands;
using Filekin.Infrastructure.Windows.Inspection.Interop;

namespace Filekin.Infrastructure.Windows.Discovery;

/// <summary>
/// Bounded Windows discovery for <c>/where</c>. Authoritative registrations and shortcuts run first;
/// only common install/app-data roots receive a cancellable directory scan, and reparse points are
/// never followed. This is program-footprint discovery, not a whole-drive file search.
/// </summary>
public sealed class WindowsWhereDiscovery : IWhereDiscovery
{
    private const int MinimumProgressIntervalMilliseconds = 100;

    private static readonly HashSet<string> RelatedFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "addon", "addons", "cache", "cacheddata", "cachedextensions", "caches", "codecache",
        "extension", "extensions", "plugin", "plugins",
    };

    private readonly IWindowsApplicationRegistrationSource _registrations;
    private readonly IWindowsShortcutSource _shortcuts;
    private readonly Func<WindowsWherePathValues> _pathValues;
    private readonly Func<IReadOnlyList<WhereSearchRoot>> _searchRoots;

    public WindowsWhereDiscovery()
        : this(
            new WindowsApplicationRegistrationSource(),
            new WindowsStartMenuShortcutSource(),
            WindowsWherePathValues.Current,
            DefaultSearchRoots)
    {
    }

    internal WindowsWhereDiscovery(
        IWindowsApplicationRegistrationSource registrations,
        IWindowsShortcutSource shortcuts,
        Func<WindowsWherePathValues> pathValues,
        Func<IReadOnlyList<WhereSearchRoot>> searchRoots)
    {
        _registrations = registrations ?? throw new ArgumentNullException(nameof(registrations));
        _shortcuts = shortcuts ?? throw new ArgumentNullException(nameof(shortcuts));
        _pathValues = pathValues ?? throw new ArgumentNullException(nameof(pathValues));
        _searchRoots = searchRoots ?? throw new ArgumentNullException(nameof(searchRoots));
    }

    public Task<WhereDiscoveryOutcome> DiscoverAsync(
        string query,
        IProgress<WhereDiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        return Task.Run(() => Discover(query.Trim(), progress, cancellationToken), cancellationToken);
    }

    private WhereDiscoveryOutcome Discover(
        string query,
        IProgress<WhereDiscoveryProgress>? progress,
        CancellationToken cancellationToken)
    {
        var matcher = new WhereQueryMatcher(query);
        var paths = _pathValues();
        var found = new Dictionary<string, AccumulatedLocation>(StringComparer.OrdinalIgnoreCase);
        var sinceLastPublish = Stopwatch.StartNew();
        var unreadable = 0;

        Publish("Checking installed applications…");
        var registrations = _registrations.GetRegistrations(cancellationToken);
        unreadable += registrations.UnreadableLocations;
        foreach (var app in registrations.Registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var strength = Stronger(matcher.MatchLabel(app.DisplayName), matcher.MatchExecutable(app.ExecutablePath));
            if (strength == WhereMatchStrength.None)
            {
                continue;
            }

            // Only a registration the user's own words named may teach a name. One reached through a
            // learned alias must not widen the search again, or one loose match cascades into a scan
            // of the whole machine.
            if (strength == WhereMatchStrength.Query)
            {
                matcher.LearnFrom(app.ExecutablePath);
                matcher.LearnFrom(app.InstallLocation);
            }

            Add(app.ExecutablePath, WhereLocationKind.Executable, app.Source);
            Add(app.InstallLocation, WhereLocationKind.Installation, app.Source);
        }

        Publish("Checking Start Menu shortcuts…");
        var shortcuts = _shortcuts.GetShortcutPaths(cancellationToken);
        unreadable += shortcuts.UnreadableLocations;
        foreach (var shortcutPath in shortcuts.ShortcutPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileNameWithoutExtension(shortcutPath);
            var strength = matcher.MatchLabel(name);
            if (strength == WhereMatchStrength.None)
            {
                continue;
            }

            Add(shortcutPath, WhereLocationKind.Shortcut, "Start Menu");
            var target = _shortcuts.TryGetTarget(shortcutPath, out var targetUnreadable);
            if (targetUnreadable)
            {
                unreadable++;
            }

            if (target is { Length: > 0 })
            {
                // Only an executable target names the program. A shortcut to a manual would teach
                // "index" from index.html, and that then claims every folder called index.
                var targetIsExecutable = IsExecutable(target, paths.PathExtensions);
                if (strength == WhereMatchStrength.Query && targetIsExecutable)
                {
                    matcher.LearnFrom(target);
                }

                Add(
                    target,
                    targetIsExecutable ? WhereLocationKind.Executable : WhereLocationKind.Installation,
                    "Start Menu target");
            }
        }

        Publish("Checking Windows PATH…");
        foreach (var (directory, source) in paths.ConfiguredDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (matcher.MatchesExecutable(file) && IsExecutable(file, paths.PathExtensions))
                    {
                        Add(file, WhereLocationKind.Executable, source);
                    }
                }
            }
            catch (Exception ex) when (IsUnreadable(ex))
            {
                unreadable++;
            }
        }

        foreach (var root in _searchRoots().Where(static root => root.Kind == WhereSearchRootKind.Installation))
        {
            ScanRoot(root, "Scanning common install folders…");
        }

        foreach (var root in _searchRoots().Where(static root => root.Kind != WhereSearchRootKind.Installation))
        {
            ScanRoot(root, "Scanning user data and config…");
        }

        Publish("Finishing…");
        return new WhereDiscoveryOutcome(Snapshot(), unreadable);

        void ScanRoot(WhereSearchRoot root, string stage)
        {
            Publish(stage);
            if (!Directory.Exists(root.Path))
            {
                return;
            }

            var queue = new Queue<(string Path, int Depth)>();
            queue.Enqueue((root.Path, 0));
            while (queue.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = queue.Dequeue();
                IEnumerable<string> directories;
                try
                {
                    directories = Directory.EnumerateDirectories(current.Path).ToArray();
                }
                catch (Exception ex) when (IsUnreadable(ex))
                {
                    unreadable++;
                    continue;
                }

                foreach (var directory in directories)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (IsReparsePoint(directory))
                    {
                        continue;
                    }

                    var name = Path.GetFileName(directory);
                    // A scanned folder never teaches an alias. Learning from one would let a single
                    // loose match widen the next, and a scan of Program Files then finds everything.
                    if (matcher.MatchesLabel(name))
                    {
                        var kind = root.Kind switch
                        {
                            WhereSearchRootKind.Configuration => WhereLocationKind.Configuration,
                            WhereSearchRootKind.UserData => WhereLocationKind.UserData,
                            _ => WhereLocationKind.Installation,
                        };
                        Add(directory, kind, root.Source);

                        ScanRelatedDirectories(directory, kind, root.Source);

                        if (root.Kind == WhereSearchRootKind.Installation)
                        {
                            ScanMatchedInstallDirectory(directory, Math.Min(2, root.MaximumDepth));
                        }
                    }

                    if (current.Depth < root.MaximumDepth)
                    {
                        queue.Enqueue((directory, current.Depth + 1));
                    }
                }
            }
        }

        void ScanMatchedInstallDirectory(string directory, int maximumDepth)
        {
            var queue = new Queue<(string Path, int Depth)>();
            queue.Enqueue((directory, 0));
            while (queue.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = queue.Dequeue();
                try
                {
                    foreach (var file in Directory.EnumerateFiles(current.Path))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (IsExecutable(file, paths.PathExtensions) && matcher.MatchesExecutable(file))
                        {
                            Add(file, WhereLocationKind.Executable, "Common install folder");
                        }
                    }

                    if (current.Depth >= maximumDepth)
                    {
                        continue;
                    }

                    foreach (var child in Directory.EnumerateDirectories(current.Path))
                    {
                        if (!IsReparsePoint(child))
                        {
                            if (IsRelatedFolder(child))
                            {
                                Add(child, WhereLocationKind.Installation, "Common install folder");
                            }

                            queue.Enqueue((child, current.Depth + 1));
                        }
                    }
                }
                catch (Exception ex) when (IsUnreadable(ex))
                {
                    unreadable++;
                }
            }
        }

        void ScanRelatedDirectories(string directory, WhereLocationKind kind, string source)
        {
            var queue = new Queue<(string Path, int Depth)>();
            queue.Enqueue((directory, 0));
            while (queue.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = queue.Dequeue();
                if (current.Depth >= 2)
                {
                    continue;
                }

                try
                {
                    foreach (var child in Directory.EnumerateDirectories(current.Path))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (IsReparsePoint(child))
                        {
                            continue;
                        }

                        if (IsRelatedFolder(child))
                        {
                            Add(child, kind, $"{source} · related files");
                        }

                        queue.Enqueue((child, current.Depth + 1));
                    }
                }
                catch (Exception ex) when (IsUnreadable(ex))
                {
                    unreadable++;
                }
            }
        }

        void Add(string? candidate, WhereLocationKind kind, string source)
        {
            if (!TryExistingPath(candidate, out var fullPath, out var isFile))
            {
                return;
            }

            if (kind == WhereLocationKind.Executable && !File.Exists(fullPath))
            {
                kind = WhereLocationKind.Installation;
            }

            if (!found.TryGetValue(fullPath, out var location))
            {
                location = new AccumulatedLocation(fullPath, kind, isFile);
                found.Add(fullPath, location);
            }
            else if (KindOrder(kind) < KindOrder(location.Kind))
            {
                location.Kind = kind;
            }

            if (location.Sources.Add(source))
            {
                PublishFind(CurrentStageFor(kind));
            }
        }

        // Progressive rows exist so a long scan looks alive, not so every single match re-sorts and
        // repaints the whole result set. Stage changes always publish; individual finds are paced.
        void PublishFind(string stage)
        {
            if (sinceLastPublish.ElapsedMilliseconds >= MinimumProgressIntervalMilliseconds)
            {
                Publish(stage);
            }
        }

        void Publish(string stage)
        {
            sinceLastPublish.Restart();
            progress?.Report(new WhereDiscoveryProgress(stage, Snapshot(), unreadable));
        }

        IReadOnlyList<WhereLocation> Snapshot() =>
            [.. found.Values
                .OrderBy(static location => KindOrder(location.Kind))
                .ThenBy(static location => location.Path, StringComparer.OrdinalIgnoreCase)
                .Select(location => new WhereLocation(
                    location.Path,
                    location.Kind,
                    string.Join(" · ", location.Sources.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)),
                    location.Kind == WhereLocationKind.Executable
                        ? paths.ScopeOf(Path.GetDirectoryName(location.Path) ?? location.Path)
                        : WherePathScope.None,
                    location.IsFile))];
    }

    private static IReadOnlyList<WhereSearchRoot> DefaultSearchRoots()
    {
        var roots = new List<WhereSearchRoot>();
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), WhereSearchRootKind.Installation, 3, "Program Files");
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), WhereSearchRootKind.Installation, 3, "Program Files (x86)");

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Add(Path.Combine(local, "Programs"), WhereSearchRootKind.Installation, 3, "User programs");
        Add(local, WhereSearchRootKind.UserData, 1, "Local AppData");
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), WhereSearchRootKind.Configuration, 1, "Roaming AppData");
        Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), WhereSearchRootKind.Configuration, 0, "User profile");
        return roots;

        void Add(string path, WhereSearchRootKind kind, int maximumDepth, string source)
        {
            if (!string.IsNullOrWhiteSpace(path) && roots.All(root => !string.Equals(root.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                roots.Add(new WhereSearchRoot(path, kind, maximumDepth, source));
            }
        }
    }

    private static bool TryExistingPath(string? candidate, out string fullPath, out bool isFile)
    {
        fullPath = string.Empty;
        isFile = false;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        try
        {
            var value = Environment.ExpandEnvironmentVariables(candidate.Trim().Trim('"'));
            if (value.Length > 2 && value[^2] == ',' && char.IsDigit(value[^1]))
            {
                value = value[..^2];
            }

            fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
            isFile = File.Exists(fullPath);
            return isFile || Directory.Exists(fullPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (IsUnreadable(ex))
        {
            return true;
        }
    }

    private static bool IsExecutable(string path, IReadOnlySet<string> pathExtensions)
    {
        var extension = Path.GetExtension(path);
        return pathExtensions.Contains(extension) || extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRelatedFolder(string path) =>
        RelatedFolderNames.Contains(WhereQueryMatcher.CompactName(Path.GetFileName(path)));

    private static WhereMatchStrength Stronger(WhereMatchStrength first, WhereMatchStrength second) =>
        first >= second ? first : second;

    private static bool IsUnreadable(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.Security.SecurityException;

    private static int KindOrder(WhereLocationKind kind) => kind switch
    {
        WhereLocationKind.Executable => 0,
        WhereLocationKind.Installation => 1,
        WhereLocationKind.UserData => 2,
        WhereLocationKind.Configuration => 3,
        _ => 4,
    };

    private static string CurrentStageFor(WhereLocationKind kind) => kind switch
    {
        WhereLocationKind.Executable => "Found an executable…",
        WhereLocationKind.Installation => "Found an installation folder…",
        WhereLocationKind.UserData => "Found user data…",
        WhereLocationKind.Configuration => "Found configuration…",
        _ => "Found a Start Menu shortcut…",
    };

    private sealed class AccumulatedLocation(string path, WhereLocationKind kind, bool isFile)
    {
        public string Path { get; } = path;

        public WhereLocationKind Kind { get; set; } = kind;

        public bool IsFile { get; } = isFile;

        public HashSet<string> Sources { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

internal enum WhereSearchRootKind
{
    Installation,
    UserData,
    Configuration,
}

internal sealed record WhereSearchRoot(string Path, WhereSearchRootKind Kind, int MaximumDepth, string Source);
