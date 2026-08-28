using Filekin.Core.Commands.Completion;
using Filekin.Infrastructure.Windows.References;

namespace Filekin.App.ViewModels;

public sealed partial class ShellViewModel
{
    private static readonly CommandCompletionSuggestion[] AppCommandCompletions =
    [
        new("/copy", "Copy a file or folder to a destination"),
        new("/delete", "Move files or folders to the Recycle Bin (same as /toss)"),
        new("/drives", "Browse assigned drives"),
        new("/ext", "Open an external terminal or program here"),
        new("/go", "Go to a folder; spaces do not need quotes"),
        new("/info", "Inspect a file, folder, or selection"),
        new("/location", "Add, edit, rename, or remove saved Locations"),
        new("/move", "Move a file or folder to a destination"),
        new("/places", "Browse common folders and cloud locations"),
        new("/recycle", "Open the Recycle Bin"),
        new("/rename", "Rename a file or folder"),
        new("/run", "Launch a file or application"),
        new("/settings", "Change Filekin preferences"),
        new("/tidy", "Sort loose files into category folders"),
        new("/toss", "Move files or folders to the Recycle Bin"),
        new("/trash", "Move files or folders to the Recycle Bin (same as /toss)"),
        new("/unzip", "Extract an archive, without the doubled folder"),
        new("/zip", "Compress files or folders into a zip"),
    ];

    private static readonly string[] WindowsReferenceNames =
    [
        "desktop",
        "documents",
        "downloads",
        "home",
        "music",
        "pictures",
        "videos",
    ];

    private IReadOnlyList<CommandCompletionSuggestion> _commandSuggestions = [];
    private int _selectedCommandSuggestionIndex = -1;
    private bool _isCommandSuggestionsOpen;
    private CommandCompletionMatch? _activeCompletion;

    public IReadOnlyList<CommandCompletionSuggestion> CommandSuggestions
    {
        get => _commandSuggestions;
        private set => SetProperty(ref _commandSuggestions, value);
    }

    public int SelectedCommandSuggestionIndex
    {
        get => _selectedCommandSuggestionIndex;
        set => SetProperty(ref _selectedCommandSuggestionIndex, value);
    }

    public bool IsCommandSuggestionsOpen
    {
        get => _isCommandSuggestionsOpen;
        private set => SetProperty(ref _isCommandSuggestionsOpen, value);
    }

    /// <summary>
    /// Handles the first Tab press. A unique match completes immediately; an ambiguous match opens
    /// the compact list and extends only the unambiguous shared prefix.
    /// </summary>
    public bool TryRequestCommandCompletion(
        string input,
        int caretIndex,
        out CommandCompletionEdit? edit)
    {
        ArgumentNullException.ThrowIfNull(input);

        var catalog = BuildCompletionCatalog();
        var match = CommandCompletion.Find(input, caretIndex, catalog);
        if (match is null)
        {
            DismissCommandSuggestions();
            edit = null;
            return false;
        }

        if (match.Suggestions.Count == 1)
        {
            edit = CommandCompletion.Apply(input, match, match.Suggestions[0]);
            DismissCommandSuggestions();
            return true;
        }

        edit = null;
        var commonPrefix = CommandCompletion.CommonPrefix(match.Suggestions);
        if (commonPrefix.Length > match.Prefix.Length)
        {
            edit = CommandCompletion.Apply(
                input,
                match,
                new CommandCompletionSuggestion(commonPrefix, string.Empty));
            match = CommandCompletion.Find(edit.Text, edit.CaretIndex, catalog) ?? match;
        }

        OpenCommandSuggestions(match, preferredText: null);
        return true;
    }

    /// <summary>Accepts the highlighted item with Tab without executing the command line.</summary>
    public CommandCompletionEdit? AcceptSelectedCommandSuggestion(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (_activeCompletion is null ||
            _selectedCommandSuggestionIndex < 0 ||
            _selectedCommandSuggestionIndex >= _commandSuggestions.Count)
        {
            return null;
        }

        var edit = CommandCompletion.Apply(
            input,
            _activeCompletion,
            _commandSuggestions[_selectedCommandSuggestionIndex]);
        DismissCommandSuggestions();
        return edit;
    }

    /// <summary>Accepts a pointer-chosen item from the open list without executing it.</summary>
    public CommandCompletionEdit? AcceptCommandSuggestion(
        string input,
        CommandCompletionSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(suggestion);

        if (_activeCompletion is null || !_commandSuggestions.Contains(suggestion))
        {
            return null;
        }

        var edit = CommandCompletion.Apply(input, _activeCompletion, suggestion);
        DismissCommandSuggestions();
        return edit;
    }

    /// <summary>Refilters an explicitly opened list as the user continues typing.</summary>
    public void RefreshCommandSuggestions(string input, int caretIndex)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!IsCommandSuggestionsOpen)
        {
            return;
        }

        var selectedText = SelectedCommandSuggestionIndex >= 0 &&
                           SelectedCommandSuggestionIndex < CommandSuggestions.Count
            ? CommandSuggestions[SelectedCommandSuggestionIndex].Text
            : null;
        var match = CommandCompletion.Find(input, caretIndex, BuildCompletionCatalog());
        if (match is null)
        {
            DismissCommandSuggestions();
            return;
        }

        OpenCommandSuggestions(match, selectedText);
    }

    public void MoveCommandSuggestionSelection(int offset)
    {
        if (!IsCommandSuggestionsOpen || CommandSuggestions.Count == 0)
        {
            return;
        }

        var current = SelectedCommandSuggestionIndex < 0 ? 0 : SelectedCommandSuggestionIndex;
        SelectedCommandSuggestionIndex = (current + offset + CommandSuggestions.Count) % CommandSuggestions.Count;
    }

    public void DismissCommandSuggestions()
    {
        _activeCompletion = null;
        CommandSuggestions = [];
        SelectedCommandSuggestionIndex = -1;
        IsCommandSuggestionsOpen = false;
    }

    private List<CommandCompletionSuggestion> BuildCompletionCatalog()
    {
        var catalog = new List<CommandCompletionSuggestion>(AppCommandCompletions);
        var referenceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddReference("thisfolder", _currentPath ?? "Current Files folder");
        AddReference("selection", SelectionDescription());

        // Saved Locations precede convenience known-folder aliases, matching resolver precedence.
        foreach (var location in _locationCatalog.Locations)
        {
            AddReference(location.Name, location.Path);
        }

        var knownFolders = new WindowsKnownFolderLocations();
        foreach (var name in WindowsReferenceNames)
        {
            if (knownFolders.TryResolve(name, out var path))
            {
                AddReference(name, path);
            }
        }

        return catalog;

        void AddReference(string name, string description)
        {
            if (referenceNames.Add(name))
            {
                catalog.Add(new CommandCompletionSuggestion("@" + name, description));
            }
        }
    }

    private string SelectionDescription() => _selectionPaths.Count switch
    {
        0 => "Current Files selection is empty",
        1 => _selectionPaths[0],
        var count => $"{count} selected items",
    };

    private void OpenCommandSuggestions(CommandCompletionMatch match, string? preferredText)
    {
        _activeCompletion = match;
        CommandSuggestions = match.Suggestions;
        var preferredIndex = preferredText is null
            ? -1
            : CommandSuggestions
                .Select(static (suggestion, index) => (suggestion, index))
                .Where(candidate => string.Equals(
                    candidate.suggestion.Text,
                    preferredText,
                    StringComparison.OrdinalIgnoreCase))
                .Select(static candidate => candidate.index)
                .DefaultIfEmpty(-1)
                .First();
        SelectedCommandSuggestionIndex = preferredIndex >= 0 ? preferredIndex : 0;
        IsCommandSuggestionsOpen = true;
    }
}
