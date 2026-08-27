using Filekin.Core.Archives;
using Filekin.Core.Commands.References;

namespace Filekin.Core.Commands.App.Unzip;

/// <summary>
/// Parses <c>/unzip [-noroot] [-skip] [-overwrite] [-y] &lt;archive...&gt; [destination]</c> before the
/// ordinary shell-quoting reference pass, for the same reason <c>/run</c> and <c>/info</c> do: a
/// multi-item <c>@selection</c> must stay several targets rather than collapsing into one quoted
/// string.
///
/// Telling the last argument apart from another archive is the only genuinely ambiguous part of the
/// grammar, so the rule is deterministic and stated rather than guessed (UX-DESIGN.md — "References
/// do not guess; commands validate"). With two or more positional arguments, the last one is the
/// destination unless it names an archive. That makes all four shapes the owner asked for work:
/// <c>/unzip a.zip</c>, <c>/unzip a.zip b.zip</c>, <c>/unzip -noroot @selection D:\new\folder</c>,
/// and <c>/unzip -noroot @selection @thisfolder</c> — including a destination that does not exist yet.
/// </summary>
public sealed class UnzipInvocationParser
{
    private readonly IReferenceResolver _references;

    public UnzipInvocationParser(IReferenceResolver references)
    {
        ArgumentNullException.ThrowIfNull(references);
        _references = references;
    }

    public UnzipInvocationParseResult Parse(string input, ReferenceContext context)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);

        if (!AppCommandParser.TryParse(input, out var command) ||
            !command.Name.Equals("unzip", StringComparison.OrdinalIgnoreCase))
        {
            return UnzipInvocationParseResult.Fail(MissingArchiveError);
        }

        var layout = UnzipLayout.NewFolder;
        CollisionPolicy? collisions = null;
        bool? skipPreview = null;
        var sawSkip = false;
        var sawOverwrite = false;
        var positional = new List<string>();

        foreach (var argument in command.Arguments)
        {
            if (!IsSwitch(argument))
            {
                positional.Add(argument);
                continue;
            }

            switch (argument.TrimStart('-').ToLowerInvariant())
            {
                case "noroot":
                    layout = UnzipLayout.NoRoot;
                    break;
                case "skip":
                    collisions = CollisionPolicy.Skip;
                    sawSkip = true;
                    break;
                case "overwrite":
                    collisions = CollisionPolicy.Overwrite;
                    sawOverwrite = true;
                    break;
                case "y":
                case "yes":
                    skipPreview = true;
                    break;
                default:
                    return UnzipInvocationParseResult.Fail(
                        $"{argument} is not an /unzip switch. Use -noroot, -skip, -overwrite, or -y.");
            }
        }

        if (sawSkip && sawOverwrite)
        {
            return UnzipInvocationParseResult.Fail("Use -skip or -overwrite, not both.");
        }

        return positional.Count == 0
            ? ParseImplicitTargets(context, layout, collisions, skipPreview)
            : ParseExplicitTargets(positional, context, layout, collisions, skipPreview);
    }

    /// <summary>
    /// Bare <c>/unzip</c> extracts what is selected. It deliberately does not go looking for an
    /// archive in the current folder: <c>/unzip</c> is type-restricted (ARCHITECTURE.md — Topic on
    /// reference arity), so an empty or non-archive selection is an error rather than a guess.
    /// </summary>
    private static UnzipInvocationParseResult ParseImplicitTargets(
        ReferenceContext context,
        UnzipLayout layout,
        CollisionPolicy? collisions,
        bool? skipPreview)
    {
        if (context.CurrentFolderPath is not { Length: > 0 } folder)
        {
            return UnzipInvocationParseResult.Fail("Open a filesystem folder, then run /unzip.");
        }

        var archives = context.Selection.Where(ArchiveFormats.LooksLikeArchive).ToList();
        return archives.Count == 0
            ? UnzipInvocationParseResult.Fail(MissingArchiveError)
            : Build(archives, folder, layout, collisions, skipPreview);
    }

    private UnzipInvocationParseResult ParseExplicitTargets(
        List<string> positional,
        ReferenceContext context,
        UnzipLayout layout,
        CollisionPolicy? collisions,
        bool? skipPreview)
    {
        var destination = context.CurrentFolderPath;
        var archiveTokens = positional;

        if (positional.Count >= 2 && !ResolvesToArchive(positional[^1], context))
        {
            var resolved = Expand(positional[^1], context);
            if (resolved.Count != 1)
            {
                return UnzipInvocationParseResult.Fail(
                    "The destination must be one folder. Try /unzip @selection @thisfolder.");
            }

            destination = resolved[0];
            archiveTokens = positional[..^1];
        }

        if (destination is not { Length: > 0 })
        {
            return UnzipInvocationParseResult.Fail("Open a filesystem folder, or name a destination folder.");
        }

        var archives = new List<string>();
        foreach (var token in archiveTokens)
        {
            archives.AddRange(Expand(token, context));
        }

        if (archives.Count == 0)
        {
            return UnzipInvocationParseResult.Fail(MissingArchiveError);
        }

        // An expanded @selection may hold ordinary files beside the archives; keep only what /unzip
        // can act on, and refuse only when nothing is left.
        var usable = archives.Where(ArchiveFormats.LooksLikeArchive).ToList();
        if (usable.Count == 0)
        {
            return UnzipInvocationParseResult.Fail(
                archives.Count == 1
                    ? $"{Path.GetFileName(archives[0])} is not an archive."
                    : MissingArchiveError);
        }

        return Build(usable, destination, layout, collisions, skipPreview);
    }

    private static UnzipInvocationParseResult Build(
        IReadOnlyList<string> archives,
        string destination,
        UnzipLayout layout,
        CollisionPolicy? collisions,
        bool? skipPreview)
    {
        foreach (var archive in archives)
        {
            if (!ArchiveFormats.IsSupported(archive))
            {
                return UnzipInvocationParseResult.Fail(
                    $"{Path.GetFileName(archive)} is not a format this version can open. /unzip reads {ArchiveFormats.SupportedDescription}.");
            }
        }

        return UnzipInvocationParseResult.Success(
            new UnzipInvocation(archives, destination, layout, collisions, skipPreview));
    }

    /// <summary>
    /// A token is a switch when it starts with <c>-</c> and is not a path. <c>-</c> alone, or
    /// anything containing a separator, is treated as a target so an oddly named file still works.
    /// </summary>
    private static bool IsSwitch(string argument) =>
        argument.Length > 1 &&
        argument[0] == '-' &&
        !argument.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
        !argument.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal);

    private bool ResolvesToArchive(string token, ReferenceContext context)
    {
        if (ArchiveFormats.LooksLikeArchive(token))
        {
            return true;
        }

        var resolved = Expand(token, context);
        return resolved.Count > 0 && resolved.All(ArchiveFormats.LooksLikeArchive);
    }

    private IReadOnlyList<string> Expand(string token, ReferenceContext context)
    {
        var resolution = _references.ResolveToken(token, context);
        if (resolution.IsKnownReference)
        {
            return resolution.Paths;
        }

        // A literal target is relative to the visible folder, the same rule /run and /info use.
        try
        {
            return context.CurrentFolderPath is { Length: > 0 } folder
                ? [Path.GetFullPath(token, folder)]
                : [Path.GetFullPath(token)];
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return [token];
        }
    }

    /// <summary>The wording ARCHITECTURE.md already specifies for this case.</summary>
    private const string MissingArchiveError = "/unzip needs an archive. Try: /unzip @selection @thisfolder";
}
