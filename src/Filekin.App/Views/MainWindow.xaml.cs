using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Filekin.App.Controls;
using Filekin.App.ViewModels;
using Filekin.Core.FileSystem;
using Filekin.Infrastructure.Windows.Windowing;

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
    private bool _isLoaded;
    private bool _isRefreshingWorkspace;
    private bool _isRestoringWorkspaceState;
    private bool _allowWindowClose;
    private Func<Task>? _pendingTerminalConfirmation;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        FitToWorkArea();
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

            var refresh = await _viewModel.RefreshWorkspaceAsync();
            if (refresh.FilesChanged)
            {
                RestoreFilesState(selectedFilePaths, focusedFilePath, filesHadFocus, filesOffset);
            }

            if (refresh.VisibleRichViewChanged)
            {
                RestoreRecycleBinState(
                    selectedRecycledItems,
                    focusedRecycledItem,
                    recycleBinHadFocus,
                    recycleBinOffset);
            }
        }
        finally
        {
            _isRefreshingWorkspace = false;
        }
    }

    private async void OnClosed(object? sender, EventArgs e) =>
        await _viewModel.DisposeAsync();

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
            case Key.Enter:
                e.Handled = true;
                SetOutputExpanded(false);
                await _viewModel.ExecuteCommandAsync();
                if (!_viewModel.IsFilesWorkspaceSelected)
                {
                    FocusSelectedTerminal();
                }
                else if (_viewModel.IsRecycleBinOpen)
                {
                    RestoreRecycleBinFocus();
                }

                break;
            case Key.Up:
                e.Handled = true;
                _viewModel.RecallPreviousCommand();
                CommandBox.CaretIndex = CommandBox.Text.Length;
                break;
            case Key.Down:
                e.Handled = true;
                _viewModel.RecallNextCommand();
                CommandBox.CaretIndex = CommandBox.Text.Length;
                break;
            case Key.Escape:
                e.Handled = true;
                SetOutputExpanded(false);
                RestoreWorkspaceFocus();
                break;
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
            case Key.Space when Keyboard.Modifiers == ModifierKeys.None:
                // Space from the neutral file list jumps to the command bar (UX-DESIGN.md — Space-to-Command).
                // Ctrl+Space still toggles selection.
                e.Handled = true;
                _ = CommandBox.Focus();
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

    private static IntPtr WindowProcedure(
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

        return IntPtr.Zero;
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
    }

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

    private async void OnSurfaceSelected(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox list)
        {
            return;
        }

        var selected = list.SelectedItem as NavItem;
        // These surfaces act as buttons, not a persistent selection: clear it so a later Back
        // doesn't leave a stale highlight, and so re-clicking the same one fires again.
        list.SelectedItem = null;

        if (selected is { Name: "recycle" })
        {
            await _viewModel.OpenRecycleBinAsync();
            RestoreRecycleBinFocus();
        }
    }

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

    private void RestoreWorkspaceFocus()
    {
        if (_viewModel.IsRecycleBinOpen)
        {
            RestoreRecycleBinFocus();
        }
        else
        {
            RestoreFilesFocus();
        }
    }

    private void RestoreFilesFocus() => RestoreListFocus(FilesList);

    private void RestoreRecycleBinFocus() => RestoreListFocus(RecycleBinList);

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
