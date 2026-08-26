using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        FitToWorkArea();
        SourceInitialized += OnSourceInitialized;
        StateChanged += OnStateChanged;
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
        _ = FilesList.Focus();
    }

    private async void OnClosed(object? sender, EventArgs e) =>
        await _viewModel.DisposeAsync();

    private async void OnCommandPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                SetOutputExpanded(false);
                await _viewModel.ExecuteCommandAsync();
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
                _ = FilesList.Focus();
                break;
        }
    }

    private void OnOpenExternalTerminal(object sender, RoutedEventArgs e) =>
        _viewModel.OpenExternalTerminal();

    private async void OnPathSegmentClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
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

    private void OnFilesSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        _viewModel.SetSelection(FilesList.SelectedItems.OfType<FileRowViewModel>().ToList());

    private async void OnFilesActivate(object sender, MouseButtonEventArgs e)
    {
        if (FindRow(e.OriginalSource) is { } row)
        {
            await _viewModel.ActivateAsync(row);
        }
    }

    private async void OnFilesKeyDown(object sender, KeyEventArgs e)
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

        if (OutputPanel.Visibility == Visibility.Visible)
        {
            SetOutputExpanded(false);
            _ = FilesList.Focus();
            e.Handled = true;
        }
        else if (_viewModel.IsRecycleBinOpen)
        {
            _viewModel.CloseRecycleBin();
            _ = FilesList.Focus();
            e.Handled = true;
        }
    }

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
        }
    }

    private void OnEmptyRecycleBin(object sender, RoutedEventArgs e) =>
        _viewModel.RequestEmptyRecycleBin();

    private void OnDeleteItem(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RecycledItemViewModel item })
        {
            _viewModel.RequestDeleteForever(item);
        }
    }

    private async void OnConfirmYes(object sender, RoutedEventArgs e) =>
        await _viewModel.ConfirmYesAsync();

    private void OnConfirmNo(object sender, RoutedEventArgs e) =>
        _viewModel.CancelConfirmation();

    private void OnCloseRecycleBin(object sender, RoutedEventArgs e)
    {
        _viewModel.CloseRecycleBin();
        _ = FilesList.Focus();
    }

    private async void OnRestoreItem(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RecycledItemViewModel item })
        {
            await _viewModel.RestoreAsync(item);
        }
    }

    private void SetOutputExpanded(bool open)
    {
        OutputPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        ViewButton.Content = open ? "Collapse" : "View";
    }
}
