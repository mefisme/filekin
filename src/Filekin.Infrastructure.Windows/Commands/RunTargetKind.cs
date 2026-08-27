namespace Filekin.Infrastructure.Windows.Commands;

/// <summary>How Filekin should activate a resolved <c>/run</c> target.</summary>
public enum RunTargetKind
{
    External,
    Terminal,
    Directory,
}
