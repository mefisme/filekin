using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Filekin.App.Controls;
using Filekin.App.ViewModels;
using Filekin.Core.Agents;
using Filekin.Core.Commands.Completion;
using Filekin.Core.FileSystem;
using Filekin.Infrastructure.Windows.Windowing;
using Microsoft.Win32;

namespace Filekin.App.Views;

[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The window disposes its view model in the Closed event; a WPF Window is not IDisposable.")]
public partial class MainWindow : Window
{
    private const string MaximizeGlyph = "\uE922";
    private const string RestoreGlyph = "\uE923";

    private readonly ShellViewModel _viewModel = new();

    // One insertion produces several WM_DEVICECHANGE broadcasts and the new volume is not queryable
    // the instant the first one arrives, so the message handler restarts this instead of enumerating.
    private readonly DispatcherTimer _volumeSettleTimer;
    private bool _isLoaded;
    private bool _isRefreshingWorkspace;
    private bool _isRestoringWorkspaceState;
    private bool _isApplyingCommandCompletion;
    private bool _focusWhereWhenReady;

    // /where streams its results, and later ones sort ahead of earlier ones. Focus stays on the row
    // object it was given, so without pinning it drifts down the list as the scan fills in. It stays
    // pinned to the first result until the user moves the cursor themselves.
    private bool _whereFocusPinned;
    private bool _isFocusingWhereRow;
    private bool _allowWindowClose;
    private Func<Task>? _pendingTerminalConfirmation;
    private NavItem? _contextLocation;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.WhereItems.CollectionChanged += OnWhereItemsChanged;
        WhereList.ItemContainerGenerator.StatusChanged += OnWhereContainersGenerated;
        FitToWorkArea();
        _volumeSettleTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(600),
        };
        _volumeSettleTimer.Tick += OnVolumeSettled;
        SourceInitialized += OnSourceInitialized;
        StateChanged += OnStateChanged;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    // Never open taller or wider than the screen's work area, or the bottom of the sidebar
    // (the /places /drives /recycle surfaces and the Settings/About footer) would fall off-screen
    // until the window is maximized.
    private void FitToWorkArea()
    {
        var work = SystemParameters.WorkArea;
        if (Height > work.Height)
        {
            Height = work.Height;
        }

        if (Width > work.Width)
        {
            Width = work.Width;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        _isLoaded = true;
        _ = FilesList.Focus();
    }

    private async void OnWindowActivated(object? sender, EventArgs e)
    {
        if (!_viewModel.IsFilesWorkspaceSelected)
        {
            return;
        }

        await RefreshWorkspaceAfterReturnAsync();
    }

    private async Task RefreshWorkspaceAfterReturnAsync()
    {
        if (!_isLoaded || _isRefreshingWorkspace || _viewModel.IsBusy)
        {
            return;
        }

        _isRefreshingWorkspace = true;
        try
        {
            var selectedFilePaths = FilesList.SelectedItems
                .OfType<FileRowViewModel>()
                .Select(static item => item.FullPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var focusedFilePath = FocusedListItem<FileRowViewModel>(FilesList)?.FullPath;
            var filesHadFocus = FilesList.IsKeyboardFocusWithin;
            var filesOffset = VerticalOffset(FilesList);

            var selectedRecycledItems = SelectedRecycledItems()
                .Select(static item => item.Item)
                .ToHashSet();
            var focusedRecycledItem = FocusedListItem<RecycledItemViewModel>(RecycleBinList)?.Item;
            var recycleBinHadFocus = RecycleBinList.IsKeyboardFocusWithin;
            var recycleBinOffset = VerticalOffset(RecycleBinList);

            // Places and Drives rebind wholesale when their content changes, so the row the keyboard
            // was on is captured the same way the other two surfaces capture theirs.
            var focusedPlacePath = FocusedListItem<PlaceItemViewModel>(PlacesList)?.Path;
            var placesHadFocus = PlacesList.IsKeyboardFocusWithin;
            var placesOffset = VerticalOffset(PlacesList);
            var focusedDriveRoot = FocusedListItem<DriveItemViewModel>(DrivesList)?.Root;
            var drivesHadFocus = DrivesList.IsKeyboardFocusWithin;
            var drivesOffset = VerticalOffset(DrivesList);

            var refresh = await _viewModel.RefreshWorkspaceAsync();
            if (refresh.FilesChanged)
            {
                RestoreFilesState(selectedFilePaths, focusedFilePath, filesHadFocus, filesOffset);
            }

            if (!refresh.VisibleRichViewChanged)
            {
                return;
            }

            if (_viewModel.IsRecycleBinOpen)
            {
                RestoreRecycleBinState(
                    selectedRecycledItems,
                    focusedRecycledItem,
                    recycleBinHadFocus,
                    recycleBinOffset);
            }
            else if (_viewModel.IsPlacesOpen)
            {
                RestoreViewportAndFocus<PlaceItemViewModel>(
                    PlacesList,
                    placesOffset,
                    placesHadFocus,
                    item => string.Equals(item.Path, focusedPlacePath, StringComparison.OrdinalIgnoreCase));
            }
            else if (_viewModel.IsDrivesOpen)
            {
                RestoreViewportAndFocus<DriveItemViewModel>(
                    DrivesList,
                    drivesOffset,
                    drivesHadFocus,
                    item => string.Equals(item.Root, focusedDriveRoot, StringComparison.OrdinalIgnoreCase));
            }
        }
        finally
        {
            _isRefreshingWorkspace = false;
        }
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        _volumeSettleTimer.Stop();
        _viewModel.WhereItems.CollectionChanged -= OnWhereItemsChanged;
        WhereList.ItemContainerGenerator.StatusChanged -= OnWhereContainersGenerated;
        await _viewModel.DisposeAsync();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowWindowClose || _viewModel.TerminalTabs.Count == 0)
        {
            return;
        }

        e.Cancel = true;
        var count = _viewModel.TerminalTabs.Count;
        ShowTerminalConfirmation(
            count == 1
                ? "Close Filekin and end the live terminal session?"
                : $"Close Filekin and end {count} live terminal sessions?",
            () =>
            {
                _allowWindowClose = true;
                Close();
                return Task.CompletedTask;
            });
    }

    private async void OnCommandPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Tab:
                if (_viewModel.IsCommandSuggestionsOpen &&
                    Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    e.Handled = true;
                    _viewModel.MoveCommandSuggestionSelection(-1);
                    CommandSuggestionList.ScrollIntoView(CommandSuggestionList.SelectedItem);
                    break;
                }

                if (_viewModel.IsCommandSuggestionsOpen)
                {
                    var accepted = _viewModel.AcceptSelectedCommandSuggestion(CommandBox.Text);
                    if (accepted is not null)
                    {
                        e.Handled = true;
                        ApplyCommandCompletion(accepted);
                    }

                    break;
                }

                if (_viewModel.TryRequestCommandCompletion(CommandBox.Text, CommandBox.CaretIndex, out var edit))
                {
                    e.Handled = true;
                    ApplyCommandCompletion(edit);
                    if (_viewModel.IsCommandSuggestionsOpen)
                    {
                        CommandSuggestionList.ScrollIntoView(CommandSuggestionList.SelectedItem);
                    }
                }

                break;
            case Key.Enter:
                e.Handled = true;
                _viewModel.DismissCommandSuggestions();
                SetOutputExpanded(false);
                await _viewModel.ExecuteCommandAsync();
                if (!_viewModel.IsFilesWorkspaceSelected)
                {
                    FocusSelectedTerminal();
                }
                else if (!_viewModel.IsFilesContentVisible)
                {
                    // A command that opened a rich view puts focus in it; ordinary commands leave
                    // the caret in the command bar.
                    RestoreWorkspaceFocus();
                }

                break;
            case Key.Up:
                e.Handled = true;
                if (_viewModel.IsCommandSuggestionsOpen)
                {
                    _viewModel.MoveCommandSuggestionSelection(-1);
                    CommandSuggestionList.ScrollIntoView(CommandSuggestionList.SelectedItem);
                }
                else
                {
                    _viewModel.RecallPreviousCommand();
                    CommandBox.CaretIndex = CommandBox.Text.Length;
                }

                break;
            case Key.Down:
                e.Handled = true;
                if (_viewModel.IsCommandSuggestionsOpen)
                {
                    _viewModel.MoveCommandSuggestionSelection(1);
                    CommandSuggestionList.ScrollIntoView(CommandSuggestionList.SelectedItem);
                }
                else
                {
                    _viewModel.RecallNextCommand();
                    CommandBox.CaretIndex = CommandBox.Text.Length;
                }

                break;
            case Key.Escape:
                e.Handled = true;
                if (_viewModel.IsBusy)
                {
                    _viewModel.CancelActiveCommand();
                    break;
                }

                if (_viewModel.IsCommandSuggestionsOpen)
                {
                    _viewModel.DismissCommandSuggestions();
                    break;
                }

                SetOutputExpanded(false);
                RestoreWorkspaceFocus();
                break;
        }
    }

    private void OnCommandTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isApplyingCommandCompletion || !_viewModel.IsCommandSuggestionsOpen)
        {
            return;
        }

        // TextChanged may run before WPF has placed the caret after the typed character. Refresh at
        // input priority so filtering sees the final text and caret position for this keystroke.
        _ = Dispatcher.BeginInvoke(
            () => _viewModel.RefreshCommandSuggestions(CommandBox.Text, CommandBox.CaretIndex),
            DispatcherPriority.Input);
    }

    private void OnCommandLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        _viewModel.DismissCommandSuggestions();

    private void OnCommandSuggestionMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(CommandSuggestionList, source) is not ListBoxItem item ||
            item.DataContext is not CommandCompletionSuggestion suggestion)
        {
            return;
        }

        var edit = _viewModel.AcceptCommandSuggestion(CommandBox.Text, suggestion);
        if (edit is null)
        {
            return;
        }

        e.Handled = true;
        ApplyCommandCompletion(edit);
        _ = CommandBox.Focus();
    }

    private void ApplyCommandCompletion(CommandCompletionEdit? edit)
    {
        if (edit is null)
        {
            return;
        }

        _isApplyingCommandCompletion = true;
        try
        {
            _viewModel.CommandInput = edit.Text;
            CommandBox.Text = edit.Text;
            CommandBox.CaretIndex = edit.CaretIndex;
        }
        finally
        {
            _isApplyingCommandCompletion = false;
        }
    }

    private void OnOpenExternalTerminal(object sender, RoutedEventArgs e) =>
        _viewModel.OpenExternalTerminal();

    private async void OnPathSegmentClick(object sender, RoutedEventArgs e)
    {
        // A rich view is a lens over preserved Files state. Its visible breadcrumb must not navigate
        // the hidden hierarchy and then reveal that surprise only when the view closes.
        if (_viewModel.IsFilesContentVisible && sender is Button { Tag: string path })
        {
            await _viewModel.NavigateToAsync(path);
        }
    }

    private void OnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse<FileSortColumn>(tag, out var column))
        {
            _viewModel.SortBy(column);
        }
    }

    private void OnFilesSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isRestoringWorkspaceState)
        {
            _viewModel.SetSelection(FilesList.SelectedItems.OfType<FileRowViewModel>().ToList());
        }
    }

    private async void OnFilesActivate(object sender, MouseButtonEventArgs e)
    {
        if (FindRow(e.OriginalSource) is { } row)
        {
            await _viewModel.ActivateAsync(row);
        }
    }

    private async void OnFilesPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when FilesList.SelectedItem is FileRowViewModel row:
                e.Handled = true;
                await _viewModel.ActivateAsync(row);
                break;
            case Key.Back:
                e.Handled = true;
                await _viewModel.NavigateUpAsync();
                break;
        }
    }

    private static FileRowViewModel? FindRow(object source)
    {
        var node = source as DependencyObject;
        while (node is not null and not ListBoxItem)
        {
            node = VisualTreeHelper.GetParent(node);
        }

        return (node as ListBoxItem)?.DataContext as FileRowViewModel;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WindowProcedure);
        }
    }

    private IntPtr WindowProcedure(
        IntPtr windowHandle,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == MaximizedWindowBounds.GetMinMaxInfoMessage)
        {
            handled = MaximizedWindowBounds.TryApply(windowHandle, lParam);
        }
        else if (message == VolumeChangeNotifications.DeviceChangeMessage &&
                 _viewModel.IsDrivesOpen &&
                 VolumeChangeNotifications.IsVolumeChange(wParam, lParam))
        {
            // A USB stick, memory card, or inserted disc changes the assigned drives while the view
            // is already on screen. Never enumerate drives inside a window procedure — coalesce the
            // burst and re-enumerate once it has settled.
            _volumeSettleTimer.Stop();
            _volumeSettleTimer.Start();
        }
        else if (SystemThemeNotifications.IsAppThemeChange(message, lParam))
        {
            // Windows flipped its light/dark app mode. Only a "Follow system" preference reacts;
            // re-resolving is cheap and the view model ignores it for an explicit choice.
            _viewModel.ReapplySystemTheme();
        }

        return IntPtr.Zero;
    }

    private async void OnVolumeSettled(object? sender, EventArgs e)
    {
        _volumeSettleTimer.Stop();
        if (_viewModel.IsDrivesOpen)
        {
            await RefreshWorkspaceAfterReturnAsync();
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        bool maximized = WindowState == WindowState.Maximized;

        // A borderless window overflows the work area by the resize border when
        // maximized; inset the content and drop the 1px frame to compensate.
        RootBorder.Margin = maximized ? new Thickness(7) : new Thickness(0);
        RootBorder.BorderThickness = maximized ? new Thickness(0) : new Thickness(1);
        MaxRestoreGlyph.Text = maximized ? RestoreGlyph : MaximizeGlyph;
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaxRestore(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private void OnToggleOutput(object sender, RoutedEventArgs e)
    {
        bool open = OutputPanel.Visibility != Visibility.Visible;
        SetOutputExpanded(open);
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TerminalConfirmationOverlay.Visibility == Visibility.Visible)
        {
            switch (e.Key)
            {
                case Key.Y:
                    e.Handled = true;
                    await ConfirmTerminalActionAsync();
                    break;
                case Key.N:
                case Key.Escape:
                    e.Handled = true;
                    CancelTerminalConfirmation();
                    break;
            }

            return;
        }

        // Ctrl+Tab / Ctrl+Shift+Tab is the single key Filekin claims from a focused terminal. It is
        // handled here, ahead of the terminal branch below, and marked handled so neither the hosted
        // shell nor WPF's own control-tab navigation sees it. Every other key still reaches the shell.
        if (e.Key == Key.Tab
            && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)
            && !_viewModel.IsConfirming
            && _viewModel.TerminalTabs.Count > 0)
        {
            e.Handled = true;
            _viewModel.SelectAdjacentWorkspace(forward: !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            if (_viewModel.IsFilesWorkspaceSelected)
            {
                await RefreshWorkspaceAfterReturnAsync();
            }

            FocusCurrentWorkspace();
            return;
        }

        // Ctrl+Shift+T and Ctrl+Shift+W share the same reserved namespace as Ctrl+Shift+V paste, and
        // the hosted shell cannot tell Ctrl+Shift+letter from plain Ctrl+letter anyway, so claiming
        // them costs the terminal nothing.
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && !_viewModel.IsConfirming)
        {
            if (e.Key == Key.T)
            {
                e.Handled = true;
                _viewModel.OpenPowerShellTab();
                FocusSelectedTerminal();
                return;
            }

            if (e.Key == Key.W && _viewModel.SelectedTerminal is { } selected)
            {
                e.Handled = true;
                RequestCloseTerminal(selected);
                return;
            }
        }

        // Terminal input belongs to the hosted shell. Files-only confirmation and Escape behavior
        // must not intercept Ctrl+C, Escape, Y/N, or any other ordinary terminal key.
        if (!_viewModel.IsFilesWorkspaceSelected)
        {
            return;
        }

        // A pending in-app confirm answers to Y/N (or Esc) from anywhere, ahead of other key handling.
        if (_viewModel.IsConfirming)
        {
            switch (e.Key)
            {
                case Key.Y:
                    e.Handled = true;
                    await _viewModel.ConfirmYesAsync();
                    return;
                case Key.N:
                case Key.Escape:
                    e.Handled = true;
                    _viewModel.CancelConfirmation();
                    return;
                default:
                    return;
            }
        }

        // Space is the command bar, on every Files surface and not only the file list
        // (UX-DESIGN.md — Space-to-Command). It is claimed here, ahead of every view, so no focused
        // button, row, or rich view can answer it first. Text entry keeps Space, or no field in
        // Settings could hold one, and Ctrl+Space still toggles a file selection.
        if (e.Key == Key.Space
            && Keyboard.Modifiers == ModifierKeys.None
            && !IsTextEntryFocused())
        {
            e.Handled = true;
            _ = CommandBox.Focus();
            return;
        }

        if (e.Key != Key.Escape)
        {
            return;
        }

        // Escape in the command bar returns to the current workspace surface. Let the TextBox-level
        // handler do that before applying the workspace-level rich-view dismissal behavior.
        if (CommandBox.IsKeyboardFocusWithin)
        {
            return;
        }

        if (OutputPanel.Visibility == Visibility.Visible)
        {
            SetOutputExpanded(false);
            RestoreWorkspaceFocus();
            e.Handled = true;
        }
        else if (_viewModel.IsRecycleBinOpen)
        {
            _viewModel.CloseRecycleBin();
            RestoreFilesFocus();
            e.Handled = true;
        }
        else if (_viewModel.IsPlacesOpen)
        {
            _viewModel.ClosePlaces();
            RestoreFilesFocus();
            e.Handled = true;
        }
        else if (_viewModel.IsDrivesOpen)
        {
            _viewModel.CloseDrives();
            RestoreFilesFocus();
            e.Handled = true;
        }
        else if (_viewModel.IsSettingsOpen)
        {
            _viewModel.CloseSettings();
            RestoreFilesFocus();
            e.Handled = true;
        }
        else if (_viewModel.IsInfoOpen)
        {
            _viewModel.CloseInfo();
            RestoreFilesFocus();
            e.Handled = true;
        }
        else if (_viewModel.IsWhereOpen)
        {
            _viewModel.CloseWhere();
            RestoreFilesFocus();
            e.Handled = true;
        }
        else if (_viewModel.IsArchiveOpen)
        {
            // An idle preview is abandoned. A live archive keeps running in the command-bar task
            // strip, where View and the explicit Stop action remain available.
            _viewModel.CloseArchive();
            RestoreFilesFocus();
            e.Handled = true;
        }
        else if (_viewModel.IsTidyOpen)
        {
            // Esc abandons the plan without moving anything, the same as Cancel.
            _viewModel.CloseTidy();
            RestoreFilesFocus();
            e.Handled = true;
        }
        else if (_viewModel.IsAgentsOpen)
        {
            // Esc dismisses the surface only. Coordination is a running project, not a preview: Back
            // and Esc must never stop an agent, release the turn, or end the project.
            _viewModel.CloseAgents();
            RestoreFilesFocus();
            e.Handled = true;
        }
    }

    /// <summary>
    /// True while a control that legitimately owns the space bar has focus. Everything else in Files
    /// gives Space to the command bar.
    /// </summary>
    private static bool IsTextEntryFocused() =>
        Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox { IsEditable: true };

    private async void OnFilesTabSelected(object sender, MouseButtonEventArgs e)
    {
        _viewModel.SelectFilesWorkspace();
        await RefreshWorkspaceAfterReturnAsync();
        RestoreWorkspaceFocus();
        e.Handled = true;
    }

    private void OnTerminalTabSelected(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TerminalTabViewModel terminal })
        {
            _viewModel.SelectTerminal(terminal);
            FocusSelectedTerminal();
            e.Handled = true;
        }
    }

    private void OnTerminalScroll(object sender, ScrollEventArgs e)
    {
        if (sender is ScrollBar { Parent: DependencyObject parent } &&
            FindVisualDescendant<TerminalControl>(parent) is { } terminal)
        {
            terminal.ScrollToValue(e.NewValue);
        }
    }

    private void OnNewTerminalTab(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenPowerShellTab();
        FocusSelectedTerminal();
    }

    private void OnCloseTerminalTab(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TerminalTabViewModel terminal })
        {
            return;
        }

        e.Handled = true;
        RequestCloseTerminal(terminal);
    }

    /// <summary>Closes a tab, confirming first while its root shell is still alive.</summary>
    private async void RequestCloseTerminal(TerminalTabViewModel terminal)
    {
        if (terminal.HasExited)
        {
            await _viewModel.CloseTerminalAsync(terminal);
            FocusCurrentWorkspace();
            return;
        }

        ShowTerminalConfirmation(
            $"Close {terminal.Title} and end its live shell session?",
            async () =>
            {
                await _viewModel.CloseTerminalAsync(terminal);
                FocusCurrentWorkspace();
            });
    }

    private void ShowTerminalConfirmation(string prompt, Func<Task> action)
    {
        TerminalConfirmationText.Text = prompt;
        _pendingTerminalConfirmation = action;
        TerminalConfirmationOverlay.Visibility = Visibility.Visible;
        _ = TerminalConfirmYesButton.Focus();
    }

    private async void OnTerminalConfirmYes(object sender, RoutedEventArgs e) =>
        await ConfirmTerminalActionAsync();

    private void OnTerminalConfirmNo(object sender, RoutedEventArgs e) =>
        CancelTerminalConfirmation();

    private async Task ConfirmTerminalActionAsync()
    {
        var action = _pendingTerminalConfirmation;
        CancelTerminalConfirmation();
        if (action is not null)
        {
            await action();
        }
    }

    private void CancelTerminalConfirmation()
    {
        _pendingTerminalConfirmation = null;
        TerminalConfirmationOverlay.Visibility = Visibility.Collapsed;
        FocusCurrentWorkspace();
    }

    private void FocusCurrentWorkspace()
    {
        if (_viewModel.IsFilesWorkspaceSelected)
        {
            RestoreWorkspaceFocus();
        }
        else
        {
            FocusSelectedTerminal();
        }
    }

    // Loaded priority runs after the layout pass that realizes the terminal surface, so the very
    // first tab is focusable by the time this runs.
    private void FocusSelectedTerminal() =>
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () => FindVisualDescendant<TerminalControl>(this)?.Focus());

    private async void OnSidebarSurfaceClicked(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is not NavItem surface)
        {
            return;
        }

        e.Handled = true;
        SurfacesList.SelectedItem = surface;
        await ActivateSidebarSurfaceAsync(surface);
    }

    private async Task ActivateSidebarSurfaceAsync(NavItem surface)
    {
        LocationsList.SelectedItem = null;
        SurfacesList.SelectedItem = null;
        switch (surface.Name)
        {
            case "recycle":
                await _viewModel.OpenRecycleBinAsync();
                RestoreRecycleBinFocus();
                break;
            case "places":
                await _viewModel.OpenPlacesAsync();
                RestoreListFocus(PlacesList);
                break;
            case "drives":
                await _viewModel.OpenDrivesAsync();
                RestoreListFocus(DrivesList);
                break;
        }
    }

    private async void OnSidebarLocationClicked(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is not NavItem location)
        {
            return;
        }

        e.Handled = true;
        LocationsList.SelectedItem = location;
        await ActivateSidebarLocationAsync(location);
    }

    private async Task ActivateSidebarLocationAsync(NavItem location)
    {
        LocationsList.SelectedItem = null;
        SurfacesList.SelectedItem = null;
        await _viewModel.NavigateToLocationAsync(location);
        RestoreWorkspaceFocus();
    }

    private async void OnSidebarPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            RestoreFilesFocus();
            return;
        }

        if (e.Key == Key.Enter && sender is ListBox { SelectedItem: NavItem selected })
        {
            e.Handled = true;
            if (ReferenceEquals(sender, LocationsList))
            {
                await ActivateSidebarLocationAsync(selected);
            }
            else
            {
                await ActivateSidebarSurfaceAsync(selected);
            }

            return;
        }

        var offset = e.Key switch
        {
            Key.Up => -1,
            Key.Down => 1,
            _ => 0,
        };
        var total = LocationsList.Items.Count + SurfacesList.Items.Count;
        if (offset == 0 || total == 0 || sender is not ListBox list)
        {
            return;
        }

        e.Handled = true;
        var current = ReferenceEquals(list, LocationsList)
            ? LocationsList.SelectedIndex
            : LocationsList.Items.Count + SurfacesList.SelectedIndex;
        if (current < 0)
        {
            current = offset > 0 ? -1 : 0;
        }

        FocusSidebarIndex((current + offset + total) % total);
    }

    private void OnSidebarGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not ListBox list || !ReferenceEquals(e.NewFocus, list) || list.Items.Count == 0)
        {
            return;
        }

        var localIndex = Math.Max(0, list.SelectedIndex);
        if (ReferenceEquals(list, LocationsList) && list.SelectedIndex < 0)
        {
            for (var index = 0; index < list.Items.Count; index++)
            {
                if (list.Items[index] is NavItem { IsActive: true })
                {
                    localIndex = index;
                    break;
                }
            }
        }

        var globalIndex = ReferenceEquals(list, LocationsList)
            ? localIndex
            : LocationsList.Items.Count + localIndex;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () => FocusSidebarIndex(globalIndex));
    }

    private void FocusSidebarIndex(int index)
    {
        var locationsCount = LocationsList.Items.Count;
        var list = index < locationsCount ? LocationsList : SurfacesList;
        var localIndex = index < locationsCount ? index : index - locationsCount;
        var other = ReferenceEquals(list, LocationsList) ? SurfacesList : LocationsList;

        other.SelectedItem = null;
        list.SelectedIndex = localIndex;
        list.ScrollIntoView(list.SelectedItem);
        list.UpdateLayout();
        if (list.ItemContainerGenerator.ContainerFromIndex(localIndex) is ListBoxItem item)
        {
            _ = item.Focus();
        }
        else
        {
            _ = list.Focus();
        }
    }

    private void OnAddLocation(object sender, RoutedEventArgs e)
    {
        _viewModel.BeginAddLocation();
        FocusLocationName();
    }

    private async void OnSaveLocation(object sender, RoutedEventArgs e)
    {
        await _viewModel.SaveEditedLocationAsync();
        if (_viewModel.IsLocationEditorOpen)
        {
            _ = LocationNameBox.Focus();
        }
        else
        {
            RestoreWorkspaceFocus();
        }
    }

    private void OnCancelLocation(object sender, RoutedEventArgs e)
    {
        _viewModel.CancelLocationEditor();
        RestoreWorkspaceFocus();
    }

    private async void OnRemoveEditedLocation(object sender, RoutedEventArgs e)
    {
        await _viewModel.RemoveEditedLocationAsync();
        RestoreWorkspaceFocus();
    }

    private async void OnLocationEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _viewModel.CancelLocationEditor();
            RestoreWorkspaceFocus();
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        if (ReferenceEquals(sender, LocationNameBox))
        {
            _ = LocationPathBox.Focus();
            LocationPathBox.SelectAll();
            return;
        }

        await _viewModel.SaveEditedLocationAsync();
        if (_viewModel.IsLocationEditorOpen)
        {
            _ = LocationNameBox.Focus();
        }
        else
        {
            RestoreWorkspaceFocus();
        }
    }

    private void OnLocationContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        _contextLocation = FindVisualAncestor<ListBoxItem>(Mouse.DirectlyOver as DependencyObject)?.DataContext as NavItem
            ?? FocusedListItem<NavItem>(LocationsList);
        if (_contextLocation is null)
        {
            e.Handled = true;
        }
    }

    private void OnEditContextLocation(object sender, RoutedEventArgs e)
    {
        if (_contextLocation is not { } location)
        {
            return;
        }

        _viewModel.BeginEditLocation(location);
        FocusLocationName();
    }

    private async void OnRemoveContextLocation(object sender, RoutedEventArgs e)
    {
        if (_contextLocation is not { } location)
        {
            return;
        }

        await _viewModel.RemoveLocationAsync(location);
        RestoreWorkspaceFocus();
    }

    private void FocusLocationName() =>
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () =>
            {
                _ = LocationNameBox.Focus();
                LocationNameBox.SelectAll();
            });

    private void OnEmptyRecycleBin(object sender, RoutedEventArgs e) =>
        _viewModel.RequestEmptyRecycleBin();

    private void OnDeleteRecycledSelection(object sender, RoutedEventArgs e) =>
        _viewModel.RequestDeleteForever(SelectedRecycledItems());

    private async void OnConfirmYes(object sender, RoutedEventArgs e) =>
        await _viewModel.ConfirmYesAsync();

    private void OnConfirmNo(object sender, RoutedEventArgs e) =>
        _viewModel.CancelConfirmation();

    private void OnCloseRecycleBin(object sender, RoutedEventArgs e)
    {
        _viewModel.CloseRecycleBin();
        RestoreFilesFocus();
    }

    private void OnClosePlaces(object sender, RoutedEventArgs e)
    {
        _viewModel.ClosePlaces();
        RestoreFilesFocus();
    }

    private void OnCloseDrives(object sender, RoutedEventArgs e)
    {
        _viewModel.CloseDrives();
        RestoreFilesFocus();
    }

    // Places and Drives rows are navigation targets rather than a file selection, so one click acts
    // (DECISIONS.md, 2026-08-26). The click must land on the row body: a Places section caption
    // lives inside the first row of its group and is not itself a destination.
    private async void OnPlaceClicked(object sender, MouseButtonEventArgs e)
    {
        if (FindActivatedRow<PlaceItemViewModel>(e.OriginalSource) is { } place)
        {
            e.Handled = true;
            await _viewModel.NavigateToPlaceAsync(place);
            RestoreWorkspaceFocus();
        }
    }

    private async void OnPlacesPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && PlacesList.SelectedItem is PlaceItemViewModel place)
        {
            e.Handled = true;
            await _viewModel.NavigateToPlaceAsync(place);
            RestoreWorkspaceFocus();
        }
    }

    private async void OnDriveClicked(object sender, MouseButtonEventArgs e)
    {
        if (FindActivatedRow<DriveItemViewModel>(e.OriginalSource) is { } drive)
        {
            e.Handled = true;
            await _viewModel.NavigateToDriveAsync(drive);
            RestoreWorkspaceFocus();
        }
    }

    private async void OnDrivesPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DrivesList.SelectedItem is DriveItemViewModel drive)
        {
            e.Handled = true;
            await _viewModel.NavigateToDriveAsync(drive);
            RestoreWorkspaceFocus();
        }
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenSettings();
        RestoreListFocus(SettingsCategoryList);
    }

    private void OnAbout(object sender, RoutedEventArgs e) => _viewModel.ShowAbout();

    private void OnCloseSettings(object sender, RoutedEventArgs e)
    {
        _viewModel.CloseSettings();
        RestoreFilesFocus();
    }

    private void OnCloseInfo(object sender, RoutedEventArgs e)
    {
        _viewModel.CloseInfo();
        RestoreFilesFocus();
    }

    private void OnCloseWhere(object sender, RoutedEventArgs e)
    {
        _viewModel.CloseWhere();
        RestoreFilesFocus();
    }

    private void OnStopWhere(object sender, RoutedEventArgs e) => _viewModel.StopWhereScan();

    private void OnWhereListIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _focusWhereWhenReady = WhereList.IsVisible;
        _whereFocusPinned = WhereList.IsVisible;
        if (_focusWhereWhenReady && WhereList.Items.Count > 0)
        {
            RequestWherePrimaryActionFocus(0);
        }
    }

    private void OnWhereItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_viewModel.IsWhereOpen || WhereList.Items.Count == 0)
        {
            return;
        }

        // SelectedIndex tracks the focused row, so a non-zero index here means results arrived above
        // the one holding focus. Re-pinning only then keeps this off the streaming hot path.
        if (_focusWhereWhenReady
            || ReferenceEquals(Keyboard.FocusedElement, WhereList)
            || (_whereFocusPinned && WhereList.SelectedIndex != 0))
        {
            RequestWherePrimaryActionFocus(0);
        }
    }

    private void OnWhereContainersGenerated(object? sender, EventArgs e)
    {
        if (_focusWhereWhenReady &&
            WhereList.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated &&
            WhereList.Items.Count > 0)
        {
            RequestWherePrimaryActionFocus(0);
        }
    }

    private void RequestWherePrimaryActionFocus(int index)
    {
        _focusWhereWhenReady = true;
        _ = Dispatcher.InvokeAsync(
            () =>
            {
                if (_viewModel.IsWhereOpen && WhereList.Items.Count > 0)
                {
                    _focusWhereWhenReady = !FocusWherePrimaryAction(
                        Math.Clamp(index, 0, WhereList.Items.Count - 1));
                }
            },
            DispatcherPriority.ContextIdle);
    }

    /// <summary>
    /// Up and Down for the whole <c>/where</c> surface, not just the result list, so the header
    /// buttons reach the results too. Space is claimed globally by the window handler, which leaves
    /// Enter as the only key that runs a <c>/where</c> action.
    /// </summary>
    private void OnWherePreviewKeyDown(object sender, KeyEventArgs e)
    {
        var offset = e.Key switch
        {
            Key.Up => -1,
            Key.Down => 1,
            _ => 0,
        };
        if (offset == 0 || WhereList.Items.Count == 0)
        {
            return;
        }

        e.Handled = true;
        _whereFocusPinned = false;
        var focusedRow = FindVisualAncestor<ListBoxItem>(Keyboard.FocusedElement as DependencyObject);
        var current = focusedRow is null ? WhereList.SelectedIndex : WhereList.Items.IndexOf(focusedRow.DataContext);
        var target = current < 0
            ? offset > 0 ? 0 : WhereList.Items.Count - 1
            : Math.Clamp(current + offset, 0, WhereList.Items.Count - 1);
        FocusWherePrimaryAction(target);
    }

    /// <summary>
    /// Tab moves between the actions of different rows, so the highlight follows keyboard focus.
    /// Without this the selected row and the focused button drift apart and neither reads as current.
    /// </summary>
    private void OnWhereFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (FindVisualAncestor<ListBoxItem>(e.NewFocus as DependencyObject) is not { } row)
        {
            return;
        }

        if (!_isFocusingWhereRow)
        {
            // Tab or a click, not the pin putting focus back. The user owns the cursor from here.
            _whereFocusPinned = false;
        }

        var index = WhereList.Items.IndexOf(row.DataContext);
        if (index >= 0)
        {
            WhereList.SelectedIndex = index;
        }
    }

    private bool FocusWherePrimaryAction(int index)
    {
        if (index < 0 || index >= WhereList.Items.Count)
        {
            return false;
        }

        _isFocusingWhereRow = true;
        try
        {
            WhereList.SelectedIndex = index;
            WhereList.ScrollIntoView(WhereList.Items[index]);
            WhereList.UpdateLayout();
            if (WhereList.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem container)
            {
                container.ApplyTemplate();
                if (container.Template.FindName("WhereGoButton", container) is Button button)
                {
                    button.BringIntoView();
                    _ = button.Focus();
                    return button.IsKeyboardFocused;
                }
            }
        }
        finally
        {
            _isFocusingWhereRow = false;
        }

        return false;
    }

    private async void OnWhereRowAction(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button { DataContext: WhereItemViewModel item } button ||
            button.Tag is not string action)
        {
            return;
        }

        e.Handled = true;
        switch (action)
        {
            case "go":
                if (await _viewModel.GoToWhereItemAsync(item))
                {
                    RevealFileRow(item.Path);
                }

                break;
            case "open":
                if (await _viewModel.OpenWhereItemAsync(item))
                {
                    RestoreFilesFocus();
                }

                break;
            case "path":
                _viewModel.RequestAddWhereItemToUserPath(item);
                break;
        }
    }

    private async void OnUndoUserPath(object sender, RoutedEventArgs e) =>
        await _viewModel.UndoUserPathChangeAsync();

    private void OnCloseArchive(object sender, RoutedEventArgs e)
    {
        _viewModel.CloseArchive();
        RestoreFilesFocus();
    }

    private void OnArchiveSecondaryAction(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsArchiveBusy)
        {
            _viewModel.StopArchiveRun();
            return;
        }

        _viewModel.CloseArchive();
        RestoreFilesFocus();
    }

    private void OnViewArchiveProgress(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenArchiveProgress();
        RestoreWorkspaceFocus();
    }

    private void OnStopArchive(object sender, RoutedEventArgs e) =>
        _viewModel.StopArchiveRun();

    private async void OnRunArchive(object sender, RoutedEventArgs e)
    {
        await _viewModel.RunArchiveAsync();
        RestoreFilesFocus();
    }

    private async void OnUndoArchive(object sender, RoutedEventArgs e) =>
        await _viewModel.UndoArchiveAsync();

    private void OnOpenWindowsProperties(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _viewModel.OpenWindowsProperties(new WindowInteropHelper(this).Handle);
    }

    // The row template has no x:Class to hang a Click on, so the owning ListBox handles the bubbled
    // Button.Click and reads the row from the button's DataContext — the same shape the Settings
    // program rows use.
    private async void OnInfoRowActionClicked(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: InfoRowViewModel row })
        {
            e.Handled = true;
            await _viewModel.InvokeInfoRowActionAsync(row);
        }
    }

    private async void OnInfoPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && InfoList.SelectedItem is InfoRowViewModel { HasAction: true } row)
        {
            e.Handled = true;
            await _viewModel.InvokeInfoRowActionAsync(row);
        }
    }

    private void OnSettingsCategorySelected(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: SettingsCategoryViewModel category })
        {
            _viewModel.SelectSettingsCategory(category.Key);
        }
    }

    // Settings option rows are choices, not a selection to act on later, so one click applies —
    // the same rule Places and Drives rows follow (DECISIONS.md, 2026-08-26).
    private async void OnThemeOptionClicked(object sender, MouseButtonEventArgs e)
    {
        if (FindActivatedRow<SettingsOptionViewModel>(e.OriginalSource) is { } option)
        {
            e.Handled = true;
            await _viewModel.SelectThemeAsync(option);
        }
    }

    private async void OnThemeOptionKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ThemeOptionList.SelectedItem is SettingsOptionViewModel option)
        {
            e.Handled = true;
            await _viewModel.SelectThemeAsync(option);
        }
    }

    private async void OnAccentOptionClicked(object sender, MouseButtonEventArgs e)
    {
        if (FindActivatedRow<SettingsOptionViewModel>(e.OriginalSource) is { } option)
        {
            e.Handled = true;
            await _viewModel.SelectAccentAsync(option);
        }
    }

    private async void OnAccentOptionKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && AccentOptionList.SelectedItem is SettingsOptionViewModel option)
        {
            e.Handled = true;
            await _viewModel.SelectAccentAsync(option);
        }
    }

    private async void OnStartupOptionClicked(object sender, MouseButtonEventArgs e)
    {
        if (FindActivatedRow<SettingsOptionViewModel>(e.OriginalSource) is { } option)
        {
            e.Handled = true;
            await ApplyStartupOptionAsync(option);
        }
    }

    private async void OnStartupOptionKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && StartupOptionList.SelectedItem is SettingsOptionViewModel option)
        {
            e.Handled = true;
            await ApplyStartupOptionAsync(option);
        }
    }

    private void OnCloseTidy(object sender, RoutedEventArgs e)
    {
        _viewModel.CloseTidy();
        RestoreFilesFocus();
    }

    private void OnCloseAgents(object sender, RoutedEventArgs e)
    {
        _viewModel.CloseAgents();
        RestoreFilesFocus();
    }

    private async void OnSetUpAgents(object sender, RoutedEventArgs e)
    {
        await _viewModel.SetUpAgentProjectAsync();
        RestoreAgentsFocus();
    }

    private async void OnSaveAgentObjective(object sender, RoutedEventArgs e)
    {
        await _viewModel.SaveAgentObjectiveAsync();
    }

    private async void OnTrustAgentFolder(object sender, RoutedEventArgs e)
    {
        await _viewModel.ApproveSharedFolderAsync(SharedFolderTrust.TrustThisFolder);
    }

    private async void OnApproveSharedFolder(object sender, RoutedEventArgs e)
    {
        await _viewModel.ApproveSharedFolderAsync(SharedFolderTrust.UseMyOwnSettings);
    }

    private async void OnStartAgents(object sender, RoutedEventArgs e)
    {
        await _viewModel.StartAgentsAsync();
    }

    /// <summary>
    /// Asks the working agent to stop. This is a request, not a kill, and the project is kept so the
    /// work can be resumed later.
    /// </summary>
    private async void OnStopAgents(object sender, RoutedEventArgs e)
    {
        await _viewModel.StopAgentsAsync();
    }

    private async void OnPassTheAgentTurn(object sender, RoutedEventArgs e)
    {
        await _viewModel.PassTheAgentTurnAsync();
    }

    private async void OnClearAgentAttention(object sender, RoutedEventArgs e)
    {
        await _viewModel.ClearAgentAttentionAsync();
    }

    private void OnViewAgentWork(object sender, RoutedEventArgs e)
    {
        _viewModel.ViewAgentWork();
        RestoreAgentsFocus();
    }

    /// <summary>
    /// Puts focus on the surface's first real action: the objective the user is expected to write,
    /// whether the folder is being set up or already has a project.
    /// </summary>
    private void RestoreAgentsFocus()
    {
        var box = _viewModel.IsAgentProjectSetUp ? AgentObjectiveBox : AgentObjectiveSetupBox;
        if (!box.Focus())
        {
            AgentsBackButton.Focus();
        }
    }

    private async void OnRunTidy(object sender, RoutedEventArgs e)
    {
        var run = _viewModel.RunTidyAsync();
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () =>
            {
                if (_viewModel.IsTidyOpen)
                {
                    RestoreTidyFocus();
                }
            });
        await run;
        RestoreFilesFocus();
    }

    private void OnTidySecondaryAction(object sender, RoutedEventArgs e)
    {
        var wasBusy = _viewModel.IsTidyBusy;
        _viewModel.TidySecondaryAction();
        if (wasBusy && _viewModel.IsTidyOpen)
        {
            RestoreTidyFocus();
        }
        else
        {
            RestoreFilesFocus();
        }
    }

    private void OnCopyPromptPath(object sender, RoutedEventArgs e) =>
        _viewModel.CopyPromptPathToClipboard();

    private void OnTidyPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.FocusedElement is CheckBox checkBox)
        {
            e.Handled = true;
            checkBox.IsChecked = checkBox.IsChecked != true;
            return;
        }

        var offset = e.Key switch
        {
            Key.Up => -1,
            Key.Down => 1,
            _ => 0,
        };
        if (offset == 0 || TidyList.Items.Count == 0)
        {
            return;
        }

        e.Handled = true;
        var focusedRow = FindVisualAncestor<ListBoxItem>(Keyboard.FocusedElement as DependencyObject);
        var current = focusedRow is null ? TidyList.SelectedIndex : TidyList.Items.IndexOf(focusedRow.DataContext);
        var target = current < 0
            ? offset > 0 ? 0 : TidyList.Items.Count - 1
            : Math.Clamp(current + offset, 0, TidyList.Items.Count - 1);
        FocusTidyCategory(target);
    }

    private void OnTidyFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (FindVisualAncestor<ListBoxItem>(e.NewFocus as DependencyObject) is { } row)
        {
            TidyList.SelectedItem = row.DataContext;
        }
    }

    private bool FocusTidyCategory(int index)
    {
        if (index < 0 || index >= TidyList.Items.Count)
        {
            return false;
        }

        TidyList.SelectedIndex = index;
        TidyList.ScrollIntoView(TidyList.Items[index]);
        TidyList.UpdateLayout();
        if (TidyList.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem container)
        {
            container.ApplyTemplate();
            if (container.Template.FindName("TidyCategoryCheckBox", container) is CheckBox checkBox)
            {
                checkBox.BringIntoView();
                _ = checkBox.Focus();
                return checkBox.IsKeyboardFocused;
            }
        }

        return false;
    }

    private void OnViewTidyProgress(object sender, RoutedEventArgs e)
    {
        _viewModel.ViewTidyProgress();
        RestoreWorkspaceFocus();
    }

    private void OnStopTidy(object sender, RoutedEventArgs e) => _viewModel.StopTidy();

    private async void OnTidyPreviewSettingClicked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { IsChecked: not null } checkBox)
        {
            await _viewModel.SetTidyPreviewAsync(checkBox.IsChecked.Value);
        }
    }

    private async void OnArchivePreviewSettingClicked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { IsChecked: not null } checkBox)
        {
            await _viewModel.SetArchivePreviewAsync(checkBox.IsChecked.Value);
        }
    }

    private async void OnArchiveOverwriteSettingClicked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { IsChecked: not null } checkBox)
        {
            await _viewModel.SetArchiveOverwriteAsync(checkBox.IsChecked.Value);
        }
    }

    /// <summary>
    /// "Choose folder…" asks for the folder before anything is written, so an abandoned picker leaves
    /// the previous startup preference exactly as it was.
    /// </summary>
    private async Task ApplyStartupOptionAsync(SettingsOptionViewModel option)
    {
        if (!option.RequiresFolderPick)
        {
            await _viewModel.SelectStartupOptionAsync(option);
            return;
        }

        var picker = new OpenFolderDialog
        {
            Title = "Open Files at launch",
            Multiselect = false,
        };

        if (picker.ShowDialog(this) == true)
        {
            await _viewModel.SetStartupFolderAsync(picker.FolderName);
        }
    }

    private async void OnAddInteractiveProgram(object sender, RoutedEventArgs e)
    {
        await _viewModel.AddInteractiveProgramAsync();
        _ = NewProgramBox.Focus();
    }

    private async void OnAddUserPath(object sender, RoutedEventArgs e)
    {
        await _viewModel.AddTypedUserPathAsync();
        _ = NewUserPathBox.Focus();
    }

    private async void OnNewUserPathKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await _viewModel.AddTypedUserPathAsync();
            _ = NewUserPathBox.Focus();
        }
    }

    private async void OnBrowseUserPath(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog
        {
            Title = "Add a command folder to Windows user PATH",
            Multiselect = false,
        };

        if (picker.ShowDialog(this) == true)
        {
            await _viewModel.AddBrowsedUserPathAsync(picker.FolderName);
        }
    }

    private void OnUserPathRowAction(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button { DataContext: WindowsPathEntryViewModel row })
        {
            return;
        }

        e.Handled = true;
        _viewModel.RequestRemoveUserPathEntry(row);
    }

    private async void OnNewProgramKeyDown(object sender, KeyEventArgs e)
    {
        // Escape abandons what is being typed before it reaches the window handler and closes the
        // whole surface; an empty box means there is nothing to abandon, so Settings closes.
        if (e.Key == Key.Escape && _viewModel.NewProgramName.Length > 0)
        {
            e.Handled = true;
            _viewModel.NewProgramName = string.Empty;
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await _viewModel.AddInteractiveProgramAsync();
    }

    // The row template lives in a ResourceDictionary with no code-behind, so its Remove button's
    // Click is caught here as it bubbles to the owning list.
    private async void OnRemoveInteractiveProgram(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: InteractiveProgramViewModel program })
        {
            e.Handled = true;
            await _viewModel.RemoveInteractiveProgramAsync(program);
        }
    }

    private async void OnOpenSettingsFile(object sender, RoutedEventArgs e) =>
        await _viewModel.OpenSettingsFileAsync();

    private async void OnRevealSettingsFolder(object sender, RoutedEventArgs e)
    {
        await _viewModel.RevealSettingsFolderAsync();
        RestoreWorkspaceFocus();
    }

    /// <summary>
    /// The row view model when the pointer landed on the row body, else <c>null</c>. The row body is
    /// the templated element named <c>Row</c>; anything above it inside the item is chrome.
    /// </summary>
    private static T? FindActivatedRow<T>(object source)
        where T : class
    {
        var node = source as DependencyObject;
        var onRowBody = false;
        while (node is not null and not ListBoxItem)
        {
            onRowBody |= node is FrameworkElement { Name: "Row" };
            node = VisualTreeHelper.GetParent(node);
        }

        return onRowBody ? (node as ListBoxItem)?.DataContext as T : null;
    }

    private async void OnRestoreRecycledSelection(object sender, RoutedEventArgs e) =>
        await _viewModel.RestoreAsync(SelectedRecycledItems());

    private void OnRecycleBinSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isRestoringWorkspaceState)
        {
            _viewModel.SetRecycleBinSelection(SelectedRecycledItems());
        }
    }

    private void OnRecycleBinPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down or Key.Prior or Key.Next or Key.Home or Key.End)
        {
            // A stationary pointer can remain over a different row after keyboard paging. Suppress
            // that hover until the user moves/clicks the mouse so only real selection reads selected.
            RecycleBinList.Tag = "False";
        }
    }

    private void OnRecycleBinMouseMove(object sender, MouseEventArgs e) =>
        RecycleBinList.Tag = "True";

    private void OnRecycleBinPreviewMouseDown(object sender, MouseButtonEventArgs e) =>
        RecycleBinList.Tag = "True";

    private List<RecycledItemViewModel> SelectedRecycledItems() =>
        RecycleBinList.SelectedItems.OfType<RecycledItemViewModel>().ToList();

    private void RestoreFilesState(
        HashSet<string> selectedPaths,
        string? focusedPath,
        bool hadKeyboardFocus,
        double verticalOffset)
    {
        RestoreSelection<FileRowViewModel>(
            FilesList,
            item => selectedPaths.Contains(item.FullPath));
        _viewModel.SetSelection(FilesList.SelectedItems.OfType<FileRowViewModel>().ToList());
        RestoreViewportAndFocus<FileRowViewModel>(
            FilesList,
            verticalOffset,
            hadKeyboardFocus,
            item => string.Equals(item.FullPath, focusedPath, StringComparison.OrdinalIgnoreCase));
    }

    private void RestoreRecycleBinState(
        HashSet<RecycledItem> selectedItems,
        RecycledItem? focusedItem,
        bool hadKeyboardFocus,
        double verticalOffset)
    {
        RestoreSelection<RecycledItemViewModel>(
            RecycleBinList,
            item => selectedItems.Contains(item.Item));
        _viewModel.SetRecycleBinSelection(SelectedRecycledItems());
        RestoreViewportAndFocus<RecycledItemViewModel>(
            RecycleBinList,
            verticalOffset,
            hadKeyboardFocus,
            item => item.Item == focusedItem);
    }

    private void RestoreSelection<T>(ListBox list, Func<T, bool> isSelected)
        where T : class
    {
        _isRestoringWorkspaceState = true;
        try
        {
            list.SelectedItems.Clear();
            foreach (var item in list.Items.OfType<T>().Where(isSelected))
            {
                list.SelectedItems.Add(item);
            }
        }
        finally
        {
            _isRestoringWorkspaceState = false;
        }
    }

    private static void RestoreViewportAndFocus<T>(
        ListBox list,
        double verticalOffset,
        bool hadKeyboardFocus,
        Func<T, bool> wasFocused)
        where T : class
    {
        list.UpdateLayout();
        FindVisualDescendant<ScrollViewer>(list)?.ScrollToVerticalOffset(verticalOffset);

        if (!hadKeyboardFocus)
        {
            return;
        }

        var focusedItem = list.Items.OfType<T>().FirstOrDefault(wasFocused);
        if (focusedItem is null)
        {
            _ = list.Focus();
            return;
        }

        list.ScrollIntoView(focusedItem);
        list.UpdateLayout();
        if (list.ItemContainerGenerator.ContainerFromItem(focusedItem) is ListBoxItem container)
        {
            _ = container.Focus();
        }
    }

    private static T? FocusedListItem<T>(ListBox list)
        where T : class
    {
        if (Keyboard.FocusedElement is not DependencyObject focusedElement)
        {
            return null;
        }

        return ItemsControl.ContainerFromElement(list, focusedElement) is ListBoxItem container
            ? container.DataContext as T
            : null;
    }

    private static double VerticalOffset(ListBox list) =>
        FindVisualDescendant<ScrollViewer>(list)?.VerticalOffset ?? 0;

    private static T? FindVisualDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                return match;
            }

            if (FindVisualDescendant<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? node)
        where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match)
            {
                return match;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return null;
    }

    private void RestoreWorkspaceFocus()
    {
        if (_viewModel.IsRecycleBinOpen)
        {
            RestoreRecycleBinFocus();
        }
        else if (_viewModel.IsPlacesOpen)
        {
            RestoreListFocus(PlacesList);
        }
        else if (_viewModel.IsDrivesOpen)
        {
            RestoreListFocus(DrivesList);
        }
        else if (_viewModel.IsSettingsOpen)
        {
            RestoreListFocus(SettingsCategoryList);
        }
        else if (_viewModel.IsInfoOpen)
        {
            RestoreListFocus(InfoList);
        }
        else if (_viewModel.IsWhereOpen)
        {
            if (WhereList.Items.Count > 0)
            {
                RequestWherePrimaryActionFocus(Math.Max(0, WhereList.SelectedIndex));
            }
            else
            {
                _focusWhereWhenReady = true;
                RestoreListFocus(WhereList);
            }
        }
        else if (_viewModel.IsTidyOpen)
        {
            RestoreTidyFocus();
        }
        else if (_viewModel.IsAgentsOpen)
        {
            RestoreAgentsFocus();
        }
        else
        {
            RestoreFilesFocus();
        }
    }

    private void RestoreFilesFocus() => RestoreListFocus(FilesList);

    private void RevealFileRow(string path)
    {
        var row = _viewModel.Files.FirstOrDefault(
            item => string.Equals(item.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            RestoreFilesFocus();
            return;
        }

        FilesList.SelectedItem = row;
        FilesList.ScrollIntoView(row);
        FilesList.UpdateLayout();
        _viewModel.SetSelection([row]);
        if (FilesList.ItemContainerGenerator.ContainerFromItem(row) is ListBoxItem container)
        {
            _ = container.Focus();
        }
    }

    private void RestoreRecycleBinFocus() => RestoreListFocus(RecycleBinList);

    private void RestoreTidyFocus()
    {
        var target = TidyPrimaryButton.IsVisible && TidyPrimaryButton.IsEnabled
            ? TidyPrimaryButton
            : TidySecondaryButton.IsVisible && TidySecondaryButton.IsEnabled
                ? TidySecondaryButton
                : TidyBackButton;
        _ = target.Focus();
    }

    private static void RestoreListFocus(ListBox list)
    {
        if (list.SelectedItem is { } selected)
        {
            list.ScrollIntoView(selected);
            list.UpdateLayout();
            if (list.ItemContainerGenerator.ContainerFromItem(selected) is ListBoxItem item && item.Focus())
            {
                return;
            }
        }

        _ = list.Focus();
    }

    private void SetOutputExpanded(bool open)
    {
        OutputPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        ViewButton.Content = open ? "Collapse" : "View";
    }
}
