using System.Text.Json;
using Filekin.Core.Agents;

namespace Filekin.Infrastructure.Windows.Agents;

/// <summary>What one status-line payload did to the stored quota observation.</summary>
public enum ClaudeStatusLineIngestion
{
    /// <summary>The payload was not valid status-line JSON, or was larger than a status line can be.</summary>
    Malformed,

    /// <summary>The payload described another checkout, so it was refused without being stored.</summary>
    ForeignProject,

    /// <summary>Claude reported no rate-limit windows yet, so usage honestly stays unknown.</summary>
    NoUsageReported,

    /// <summary>A newer observation was already stored, so this older one was discarded.</summary>
    Superseded,

    /// <summary>The observation replaced this project's stored Claude usage.</summary>
    Recorded,
}

/// <summary>
/// Turns the documented Claude Code status-line JSON into this project's stored Claude quota
/// observation. It reads only the non-secret window fields, keeps the payload in memory for the length
/// of one parse, and stores nothing but the parsed windows.
/// </summary>
public sealed class ClaudeStatusLineUsageIngestor
{
    /// <summary>A status-line payload is a few kilobytes; anything larger is refused unread.</summary>
    public const int MaximumInputLength = 256 * 1024;

    private readonly IAgentUsageObservationStore _observations;
    private readonly string _projectFolderPath;
    private readonly Guid _projectId;
    private readonly TimeProvider _timeProvider;

    public ClaudeStatusLineUsageIngestor(
        IAgentUsageObservationStore observations,
        ClaudeStatusLineRequest request,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(request);
        if (request.ProjectId == Guid.Empty)
        {
            throw new ArgumentException("The agent project id cannot be empty.", nameof(request));
        }

        _observations = observations;
        _projectId = request.ProjectId;
        _projectFolderPath = Path.GetFullPath(request.ProjectFolderPath);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ClaudeStatusLineIngestion> IngestAsync(
        TextReader input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var buffer = new char[MaximumInputLength + 1];
        try
        {
            var length = 0;
            while (length < buffer.Length)
            {
                var read = await input.ReadBlockAsync(buffer.AsMemory(length), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                length += read;
            }

            if (length > MaximumInputLength)
            {
                return ClaudeStatusLineIngestion.Malformed;
            }

            return await IngestAsync(buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Array.Clear(buffer);
        }
    }

    private async Task<ClaudeStatusLineIngestion> IngestAsync(
        ReadOnlyMemory<char> payload,
        CancellationToken cancellationToken)
    {
        AgentUsageSnapshot snapshot;
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return ClaudeStatusLineIngestion.Malformed;
            }

            if (!DescribesThisProject(document.RootElement))
            {
                return ClaudeStatusLineIngestion.ForeignProject;
            }

            snapshot = ClaudeCliProtocol.ParseStatusLineUsage(
                document.RootElement,
                _timeProvider.GetUtcNow());
        }
        catch (JsonException)
        {
            return ClaudeStatusLineIngestion.Malformed;
        }

        if (!snapshot.IsKnown)
        {
            return ClaudeStatusLineIngestion.NoUsageReported;
        }

        return await _observations
            .RecordUsageObservationAsync(
                reportingProjectId: _projectId,
                snapshot,
                cancellationToken)
            .ConfigureAwait(false)
            ? ClaudeStatusLineIngestion.Recorded
            : ClaudeStatusLineIngestion.Superseded;
    }

    /// <summary>
    /// Filekin passes this status line inline to one background session in one checkout, so a payload
    /// from anywhere else is refused rather than attributed to this project.
    /// </summary>
    private bool DescribesThisProject(JsonElement root)
    {
        var reported = ReadWorkspacePath(root);
        return reported is not null && IsProjectFolderOrBeneath(reported);
    }

    private bool IsProjectFolderOrBeneath(string reportedPath)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(reportedPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (ClaudeBackgroundLaunchPlan.PathsEqual(_projectFolderPath, fullPath))
        {
            return true;
        }

        var root = Path.TrimEndingDirectorySeparator(_projectFolderPath) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadWorkspacePath(JsonElement root)
    {
        if (root.TryGetProperty("workspace", out var workspace) &&
            workspace.ValueKind == JsonValueKind.Object &&
            workspace.TryGetProperty("project_dir", out var projectDirectory) &&
            projectDirectory.ValueKind == JsonValueKind.String)
        {
            return projectDirectory.GetString();
        }

        return root.TryGetProperty("cwd", out var currentDirectory) &&
               currentDirectory.ValueKind == JsonValueKind.String
            ? currentDirectory.GetString()
            : null;
    }
}
