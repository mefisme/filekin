namespace Filekin.Core.Inspection;

/// <summary>What a single <c>/info</c> invocation is describing.</summary>
public enum InspectionKind
{
    /// <summary>One file.</summary>
    File,

    /// <summary>One folder, whose size and counts are scanned recursively.</summary>
    Folder,

    /// <summary>Several selected items, summarized as one aggregate.</summary>
    Selection,
}

/// <summary>One label/value row on the Info sheet, in display order.</summary>
public sealed record InspectionDetail(string Label, string Value);

/// <summary>
/// The immediately available part of an Info sheet: everything that can be read from file metadata
/// without walking a tree or reading a whole file. Recursive size, hashes, and line counts arrive
/// separately, because <c>/info</c> opens at once and never blocks on expensive work
/// (UX-DESIGN.md — Files · Info).
/// </summary>
public sealed record InspectionResult
{
    public InspectionResult(
        InspectionKind kind,
        string heading,
        string? singlePath,
        IReadOnlyList<InspectionDetail> details,
        bool needsAggregate,
        bool canCountLines = false,
        string? error = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(heading);
        ArgumentNullException.ThrowIfNull(details);

        Kind = kind;
        Heading = heading;
        SinglePath = singlePath;
        Details = details;
        NeedsAggregate = needsAggregate;
        CanCountLines = canCountLines;
        Error = error;
    }

    public InspectionKind Kind { get; }

    /// <summary>The item name, or a count such as <c>37 selected items</c>.</summary>
    public string Heading { get; }

    /// <summary>
    /// The one path being inspected, or <c>null</c> for a multi-item selection. This gates the
    /// single-target actions: Copy, SHA-256, line count, and Windows Properties.
    /// </summary>
    public string? SinglePath { get; }

    /// <summary>Rows that are ready immediately, already ordered for display.</summary>
    public IReadOnlyList<InspectionDetail> Details { get; }

    /// <summary>Whether recursive Size/Files/Folders rows belong on this sheet.</summary>
    public bool NeedsAggregate { get; }

    /// <summary>Whether the target looks like text, so a line count is worth offering.</summary>
    public bool CanCountLines { get; }

    /// <summary>Set when the target could not be inspected at all; no rows are meaningful then.</summary>
    public string? Error { get; }

    public static InspectionResult Failure(string heading, string error) =>
        new(InspectionKind.File, heading, singlePath: null, details: [], needsAggregate: false, error: error);
}
