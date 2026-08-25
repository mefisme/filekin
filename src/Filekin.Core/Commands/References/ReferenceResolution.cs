namespace Filekin.Core.Commands.References;

/// <summary>
/// The result of resolving one <c>@name</c> token. A recognized reference resolves to one or more
/// absolute paths (<c>@selection</c> may yield several, or none when nothing is selected); an
/// unrecognized name is not a workspace reference and is left untouched so real shell syntax — array
/// <c>@()</c>, hashtable <c>@{}</c>, here-string <c>@"..."@</c>, and splatting of an unknown variable —
/// passes through (FEATURES.md — "Shell-Compatible Workspace References").
/// </summary>
public sealed record ReferenceResolution
{
    private ReferenceResolution(bool isKnownReference, IReadOnlyList<string> paths)
    {
        IsKnownReference = isKnownReference;
        Paths = paths;
    }

    public bool IsKnownReference { get; }

    public IReadOnlyList<string> Paths { get; }

    public static ReferenceResolution Unknown { get; } = new(false, []);

    public static ReferenceResolution Known(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new ReferenceResolution(true, paths);
    }
}
