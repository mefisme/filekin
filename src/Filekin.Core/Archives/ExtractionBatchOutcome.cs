namespace Filekin.Core.Archives;

/// <summary>
/// The complete result of one <c>/unzip</c> invocation. Several archives remain one user-level
/// operation, so they travel through history and Undo as one payload.
/// </summary>
public sealed record ExtractionBatchOutcome
{
    public ExtractionBatchOutcome(IReadOnlyList<ExtractionOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        Outcomes = outcomes;
    }

    /// <summary>Parameterless construction for the JSON round-trip through the journal.</summary>
    public ExtractionBatchOutcome()
        : this([])
    {
    }

    /// <summary>Per-archive results in execution order.</summary>
    public IReadOnlyList<ExtractionOutcome> Outcomes { get; init; }

    public bool WroteAnything => Outcomes.Any(outcome => outcome.WroteAnything);

    public int CreatedFileCount => Outcomes.Sum(outcome => outcome.CreatedFiles.Count);

    public int SkippedCount => Outcomes.Sum(outcome => outcome.SkippedCount);

    public IReadOnlyList<string> Failures => [.. Outcomes.SelectMany(outcome => outcome.Failures)];
}
