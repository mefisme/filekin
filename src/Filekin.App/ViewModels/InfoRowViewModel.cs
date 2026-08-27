namespace Filekin.App.ViewModels;

/// <summary>What the action on an Info row does, when it has one.</summary>
public enum InfoRowAction
{
    None,
    CopyValue,
    CalculateChecksum,
    CountLines,
}

/// <summary>
/// One label/value row on the Info sheet.
///
/// The row is mutable on purpose. A recursive scan fills Size, Files, and Folders in while it runs,
/// and an on-demand action replaces its own value; rebuilding the collection instead would throw
/// away the row the keyboard is on — the defect the Places and Drives views already had to fix.
/// </summary>
public sealed class InfoRowViewModel : ObservableObject
{
    private string _value;
    private string? _actionLabel;
    private bool _isBusy;

    public InfoRowViewModel(string label, string value, InfoRowAction action = InfoRowAction.None, string? actionLabel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(value);

        Label = label;
        _value = value;
        Action = action;
        _actionLabel = actionLabel;
    }

    public string Label { get; }

    public InfoRowAction Action { get; }

    public string Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value))
            {
                OnPropertyChanged(nameof(AutomationName));
            }
        }
    }

    /// <summary>The action button caption, or <c>null</c> when this row has no action left to offer.</summary>
    public string? ActionLabel
    {
        get => _actionLabel;
        set
        {
            if (SetProperty(ref _actionLabel, value))
            {
                OnPropertyChanged(nameof(HasAction));
            }
        }
    }

    public bool HasAction => !string.IsNullOrEmpty(_actionLabel);

    /// <summary>True while this row's own on-demand work is running, so the action cannot re-enter.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsActionEnabled));
            }
        }
    }

    public bool IsActionEnabled => !_isBusy;

    /// <summary>Screen readers get the whole fact, not two disconnected cells.</summary>
    public string AutomationName => $"{Label}: {Value}";
}
