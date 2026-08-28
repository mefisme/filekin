using System.Text;

namespace Filekin.Infrastructure.Windows.Discovery;

/// <summary>Why a candidate name matched: the user's own query, or a name learned from a match.</summary>
internal enum WhereMatchStrength
{
    None,
    Alias,
    Query,
}

/// <summary>
/// Conservative name matching plus a small set of aliases learned from authoritative matches.
///
/// Learning exists for one reason: a friendly name such as "Visual Studio Code" must still find
/// <c>Code.exe</c> and <c>.vscode</c>. Three rules keep that from becoming "everything matches".
///
/// A name is learned only from the executable and installation folder a match names, never from the
/// display name, because "Microsoft Visual Studio Code (User)" otherwise teaches <c>user</c> and that
/// selects "NVIDIA User Container". Only a <see cref="WhereMatchStrength.Query"/> match may teach at
/// all, so a result found through an alias can never widen the search a second time. And a short
/// learned word must be a name in its entirety, while only a long joined name may match inside
/// another name, because <c>code</c> otherwise selects both Unicode and every "Code Cache" folder.
/// </summary>
internal sealed class WhereQueryMatcher
{
    /// <summary>
    /// Words that name a publisher, a category, an architecture, or a folder role rather than one
    /// program. Learning one of these turns an alias into a net that catches most of the machine.
    /// </summary>
    private static readonly HashSet<string> GenericWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "amd64", "and", "app", "application", "arm64", "bin", "cache", "client", "common",
        "corporation", "data", "desktop", "doc", "docs", "edition", "files", "for", "framework",
        "help", "inc", "installer", "library", "llc", "log", "logs", "microsoft", "of", "platform",
        "plugin", "program", "programs", "runtime", "sdk", "server", "service", "services", "setup",
        "shared", "software", "studio", "temp", "the", "tool", "tools", "update", "updater", "user",
        "users", "version", "visual", "win32", "win64", "windows", "with", "x64", "x86",
    };

    /// <summary>The shortest learned word allowed to identify a program by an exact name match.</summary>
    private const int MinimumSegmentAliasLength = 4;

    /// <summary>The shortest joined name allowed to match anywhere inside another name.</summary>
    private const int MinimumSubstringAliasLength = 6;

    /// <summary>A machine with thousands of registrations must not accumulate aliases without end.</summary>
    private const int MaximumAliases = 24;

    private readonly string[] _queryWords;
    private readonly string _compactQuery;
    private readonly HashSet<string> _segmentAliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _substringAliases = new(StringComparer.OrdinalIgnoreCase);

    public WhereQueryMatcher(string query)
    {
        _queryWords = Words(query);
        _compactQuery = Compact(query);
    }

    public bool MatchesLabel(string? label) => MatchLabel(label) != WhereMatchStrength.None;

    public bool MatchesExecutable(string? path) => MatchExecutable(path) != WhereMatchStrength.None;

    public WhereMatchStrength MatchLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return WhereMatchStrength.None;
        }

        // The typed query is trusted: the user chose it, so it may match anywhere inside a name.
        var compact = Compact(label);
        if (compact.Contains(_compactQuery, StringComparison.OrdinalIgnoreCase) ||
            (_queryWords.Length > 1 && _queryWords.All(word => compact.Contains(word, StringComparison.OrdinalIgnoreCase))))
        {
            return WhereMatchStrength.Query;
        }

        if (_substringAliases.Any(alias => compact.Contains(alias, StringComparison.OrdinalIgnoreCase)))
        {
            return WhereMatchStrength.Alias;
        }

        // A short learned word has to be the whole name. Accepting it as one word among several
        // makes the alias "code" claim every Electron app's "Code Cache" folder.
        return _segmentAliases.Contains(compact) ? WhereMatchStrength.Alias : WhereMatchStrength.None;
    }

    public WhereMatchStrength MatchExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return WhereMatchStrength.None;
        }

        var stem = Compact(Path.GetFileNameWithoutExtension(path));
        if (IsExecutableMatch(stem, _compactQuery))
        {
            return WhereMatchStrength.Query;
        }

        return _segmentAliases.Concat(_substringAliases).Any(alias => IsExecutableMatch(stem, alias))
            ? WhereMatchStrength.Alias
            : WhereMatchStrength.None;
    }

    /// <summary>
    /// Records the real names behind one authoritative match: an executable's own name, or the leaf
    /// folder a program was installed into. Call this only for a <see cref="WhereMatchStrength.Query"/>
    /// match, and only with a path, never with a display name.
    /// </summary>
    public void LearnFrom(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            _segmentAliases.Count + _substringAliases.Count >= MaximumAliases)
        {
            return;
        }

        var name = Path.GetFileNameWithoutExtension(value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var specific = Words(name)
            .Where(static word => !GenericWords.Contains(word) && !word.All(char.IsDigit))
            .ToArray();
        if (specific.Length == 0)
        {
            // A folder called "Application" or "bin" says nothing about which program owns it.
            return;
        }

        foreach (var word in specific.Where(static word => word.Length >= MinimumSegmentAliasLength))
        {
            _segmentAliases.Add(word);
        }

        // "Microsoft VS Code" teaches both "microsoftvscode" and "vscode"; the second is what finds
        // the .vscode folder, and it stays specific enough to be safe as a substring.
        foreach (var joined in new[] { Compact(name), string.Concat(specific) })
        {
            if (joined.Length >= MinimumSubstringAliasLength)
            {
                _substringAliases.Add(joined);
            }
        }
    }

    private static bool IsExecutableMatch(string stem, string query) =>
        stem.Equals(query, StringComparison.OrdinalIgnoreCase) ||
        stem.StartsWith(query, StringComparison.OrdinalIgnoreCase) &&
        stem[query.Length..].All(static character => char.IsDigit(character));

    private static string[] Words(string value) =>
        value.Split(
                value.Where(static character => !char.IsLetterOrDigit(character)).Distinct().ToArray(),
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static word => word.ToLowerInvariant())
            .ToArray();

    private static string Compact(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    internal static string CompactName(string value) => Compact(value);
}
