using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.Mcp;

/// <summary>
/// The companion's second mode: Claude Code's status-line command for one Filekin agent project. It
/// receives the documented status JSON on stdin, keeps the five-hour and seven-day windows, and stores
/// them as that project's Claude usage observation. It prints nothing, reads no transcript, changes no
/// coordination state, and stores no raw input.
/// </summary>
internal static class ClaudeStatusLineMode
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static async Task<int> RunAsync(
        ClaudeStatusLineRequest request,
        TextReader input,
        TextWriter diagnostics)
    {
        // Same rule as the coordination companion: a status line launched by a session that has
        // outlived its project must not open the live state database read-write. The check writes
        // nothing and creates nothing, so a stale session cannot migrate or touch it on the way to
        // being told it has no project.
        if (!await SqliteAgentProjectStore
                .ProjectExistsAsync(request.StateDatabasePath, request.ProjectId)
                .ConfigureAwait(false))
        {
            await diagnostics.WriteLineAsync(
                    "Filekin has no such agent project in its coordination state, so this Claude usage observation was discarded.")
                .ConfigureAwait(false);
            return 1;
        }

        using var timeout = new CancellationTokenSource(Timeout);
        using var store = new SqliteAgentProjectStore(request.StateDatabasePath);
        var ingestor = new ClaudeStatusLineUsageIngestor(store, request);
        ClaudeStatusLineIngestion outcome;
        try
        {
            outcome = await ingestor.IngestAsync(input, timeout.Token).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            await diagnostics.WriteLineAsync(
                    "This Filekin agent project no longer exists, so its Claude usage observation was discarded.")
                .ConfigureAwait(false);
            return 1;
        }

        switch (outcome)
        {
            case ClaudeStatusLineIngestion.Recorded:
            case ClaudeStatusLineIngestion.NoUsageReported:
            case ClaudeStatusLineIngestion.Superseded:
                return 0;
            case ClaudeStatusLineIngestion.ForeignProject:
                await diagnostics.WriteLineAsync(
                        "The status-line payload described another checkout, so Filekin refused to record it.")
                    .ConfigureAwait(false);
                return 1;
            default:
                await diagnostics.WriteLineAsync(
                        "The status-line payload was not readable Claude Code status JSON.")
                    .ConfigureAwait(false);
                return 1;
        }
    }
}
