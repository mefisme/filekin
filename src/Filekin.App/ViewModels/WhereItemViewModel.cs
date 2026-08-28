using Filekin.Core.Discovery;

namespace Filekin.App.ViewModels;

/// <summary>Presentation state for one explicit-action row in <c>Files · Where</c>.</summary>
public sealed class WhereItemViewModel : ObservableObject
{
    private WhereLocation _location;
    private bool _startsSection;

    public WhereItemViewModel(WhereLocation location, bool startsSection)
    {
        _location = location ?? throw new ArgumentNullException(nameof(location));
        _startsSection = startsSection;
    }

    public WhereLocation Location => _location;

    public string Path => Location.Path;

    public string Sources => Location.Sources;

    public bool StartsSection => _startsSection;

    public string SectionTitle => Location.Kind switch
    {
        WhereLocationKind.Executable => "EXECUTABLES",
        WhereLocationKind.Installation => "INSTALLATION",
        WhereLocationKind.UserData => "USER DATA",
        WhereLocationKind.Configuration => "CONFIGURATION",
        _ => "SHORTCUTS",
    };

    public string KindText => Location.Kind switch
    {
        WhereLocationKind.Executable => "Executable",
        WhereLocationKind.Installation => "Install folder",
        WhereLocationKind.UserData => "User data",
        WhereLocationKind.Configuration => "Configuration",
        _ => "Start Menu",
    };

    public string PathStatus
    {
        get
        {
            if (Location.Kind != WhereLocationKind.Executable)
            {
                return Sources;
            }

            var configured = Location.PathScope & (WherePathScope.User | WherePathScope.Machine);
            return configured switch
            {
                WherePathScope.User | WherePathScope.Machine => "On PATH · User + Machine",
                WherePathScope.User => "On PATH · User",
                WherePathScope.Machine => "On PATH · Machine",
                _ when Location.PathScope.HasFlag(WherePathScope.Process) => "On PATH · This Filekin session",
                _ => "Not on PATH",
            };
        }
    }

    public string DetailText => Location.Kind == WhereLocationKind.Executable
        ? $"{PathStatus} · {Sources}"
        : Sources;

    public bool IsDirectory => !Location.IsFile;

    public bool CanAddToUserPath => Location.Kind == WhereLocationKind.Executable &&
        !Location.PathScope.HasFlag(WherePathScope.User) &&
        !Location.PathScope.HasFlag(WherePathScope.Machine);

    public string AutomationName => $"{KindText}, {Path}, {PathStatus}";

    internal void Update(WhereLocation location, bool startsSection)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (_location == location && _startsSection == startsSection)
        {
            return;
        }

        _location = location;
        _startsSection = startsSection;
        OnPropertyChanged(string.Empty);
    }
}
