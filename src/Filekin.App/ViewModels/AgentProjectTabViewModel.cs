using System.IO;
using Filekin.Core.Agents;

namespace Filekin.App.ViewModels;

/// <summary>
/// One persistent Agents control-center tab for one exact project folder. The tab keeps the
/// presentation state that has not been saved yet while another project is selected; provider and
/// coordination state remain owned by the app-wide runtime and store.
/// </summary>
public sealed class AgentProjectTabViewModel : ObservableObject
{
    private bool _isSelected;

    public AgentProjectTabViewModel(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        FolderPath = Path.GetFullPath(folderPath);
        var folderName = Path.GetFileName(FolderPath.TrimEnd(Path.DirectorySeparatorChar));
        Title = $"Agents · {(folderName.Length == 0 ? FolderPath : folderName)}";
    }

    public string FolderPath { get; }

    public string Title { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    internal AgentProjectState? Project { get; set; }

    internal string ObjectiveDraft { get; set; } = string.Empty;

    internal bool IsObjectiveDraftDirty { get; set; }

    internal string AgentChoice { get; set; } = string.Empty;

    /// <summary>
    /// Presentation state only. Each open project tab remembers whether its supporting activity log
    /// is open while this Filekin window lives; a newly opened tab starts with the log collapsed.
    /// </summary>
    internal bool IsActivityLogExpanded { get; set; }

    internal List<AgentEventViewModel> Events { get; } = [];

    /// <summary>
    /// The coordination facts this tab has already written into its account. A message or a handoff
    /// stays in the project for good, so without this every refresh would report it again.
    /// </summary>
    internal List<string> NotedCoordinationIds { get; } = [];

    internal string LastNotedStatus { get; set; } = string.Empty;

    internal string LastNotedReport { get; set; } = string.Empty;
}
