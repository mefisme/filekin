using Filekin.Core.Operations;

namespace Filekin.Core.Commands.App;

/// <summary>
/// The parsed identity and authoritative result of one command handled by the common app-command
/// dispatcher. Presentation layers may carry this value without reconstructing filesystem mutations
/// from result text.
/// </summary>
public sealed record AppCommandExecutionDetail
{
    public AppCommandExecutionDetail(
        string commandName,
        AppCommandResult result,
        IReadOnlyList<PathRelocation>? effectiveRelocations = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        ArgumentNullException.ThrowIfNull(result);
        effectiveRelocations ??= result.Relocations;
        var unmatchedRelocations = result.Relocations.ToList();
        if (effectiveRelocations.Any(relocation => !unmatchedRelocations.Remove(relocation)))
        {
            throw new ArgumentException(
                "Effective relocations must belong to the command result.",
                nameof(effectiveRelocations));
        }

        CommandName = commandName;
        Result = result;
        EffectiveRelocations = [.. effectiveRelocations];
    }

    /// <summary>The lower-case command name produced by <see cref="AppCommandParser"/>.</summary>
    public string CommandName { get; }

    public AppCommandResult Result { get; }

    /// <summary>
    /// Successful relocations that still exist after post-command consistency work. This normally
    /// matches <see cref="AppCommandResult.Relocations"/>, but saved-Location compensation can remove
    /// some or all of the original filesystem mutations before history observes the outcome.
    /// </summary>
    public IReadOnlyList<PathRelocation> EffectiveRelocations { get; }

    public AppCommandExecutionDetail WithEffectiveRelocations(
        IReadOnlyList<PathRelocation> effectiveRelocations) =>
        new(CommandName, Result, effectiveRelocations);
}
