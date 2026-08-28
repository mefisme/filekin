using System.Collections.Generic;
using Filekin.Core.Tidy;

namespace Filekin.App.ViewModels;

/// <summary>
/// One category row in the <c>/tidy</c> plan: the folder that would be used, how many files would go
/// into it, and whether the user wants it.
///
/// The tick is the only editable thing on the row. The owner confirmed on 2026-08-27 that the plan
/// toggles categories, never single files — per-file ticks would turn a "type the command, the mess
/// gets organized" action into a filing session.
/// </summary>
public sealed class TidyGroupViewModel : ObservableObject
{
    private bool _isSelected = true;

    public TidyGroupViewModel(TidyGroup group, IReadOnlyList<string> sampleNames, bool folderExists)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(sampleNames);

        Category = group.Category;
        FolderName = group.Category.FolderName();
        Count = group.Count;
        SampleNames = sampleNames;
        FolderExists = folderExists;
    }

    public TidyCategory Category { get; }

    /// <summary>The literal folder name on disk, which is also the row's label.</summary>
    public string FolderName { get; }

    public int Count { get; }

    /// <summary>A few file names, so the row can be judged without opening anything.</summary>
    public IReadOnlyList<string> SampleNames { get; }

    /// <summary>Whether the folder is already there. Reused rather than created (owner, 2026-08-27).</summary>
    public bool FolderExists { get; }

    public string CountText => Count == 1 ? "1 file" : $"{Count:N0} files";

    /// <summary>Says plainly whether this run creates the folder or adds to one already there.</summary>
    public string DestinationText => FolderExists ? $"into existing {FolderName}" : $"new folder {FolderName}";

    public string SampleText => string.Join(", ", SampleNames);

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
