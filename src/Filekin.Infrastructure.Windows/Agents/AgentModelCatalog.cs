using Filekin.Core.Agents;

namespace Filekin.Infrastructure.Windows.Agents;

/// <summary>
/// One model a tool offers, and how hard it can be asked to think. Effort changes what a turn costs,
/// so it belongs with the model rather than hidden somewhere else.
/// </summary>
public sealed record AgentModelChoice(string Id, string DisplayName, IReadOnlyList<string> Efforts);

/// <summary>
/// What each installed tool offers, as that tool reports or documents it. Filekin never invents a
/// model name: an install that cannot say what it has offers nothing, and its own default is used.
/// </summary>
public sealed class AgentModelCatalog
{
    /// <summary>
    /// Claude Code's documented model aliases that stay within normal subscription usage. Its CLI
    /// also takes a full model name, so this is a shortlist rather than a limit. `best` and `fable`
    /// are deliberately absent because they can use paid usage credits; the explicit 1M aliases are
    /// absent for the same reason. `ultracode` is a separate setting, not an effort level.
    /// </summary>
    private static readonly string[] ClaudeEfforts = ["low", "medium", "high", "xhigh", "max"];

    private static readonly string[] ClaudeModels = ["opus", "sonnet", "haiku", "opusplan"];

    private readonly string _codexExecutable;

    public AgentModelCatalog(string codexExecutable = "codex")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codexExecutable);
        _codexExecutable = codexExecutable;
    }

    public async Task<IReadOnlyList<AgentModelChoice>> ReadAsync(
        AgentProvider provider,
        CancellationToken cancellationToken = default)
    {
        if (provider == AgentProvider.ClaudeCode)
        {
            return ClaudeModels
                .Select(model => new AgentModelChoice(
                    model,
                    model,
                    model == "haiku" ? [] : ClaudeEfforts))
                .ToArray();
        }

        // Codex reports its own list, so a person sees what this install can actually run.
        await using var client = new CodexAppServerClient(_codexExecutable);
        return await client.ReadModelsAsync(cancellationToken).ConfigureAwait(false);
    }
}
