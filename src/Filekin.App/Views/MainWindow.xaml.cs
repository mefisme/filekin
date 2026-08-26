using System;
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

public partial class MainWindow : Window
{
    private const string MaximizeGlyph = "\uE922";
    private const string RestoreGlyph = "\uE923";

    private readonly ShellViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        SourceInitialized += OnSourceInitialized;
        StateChanged += OnStateChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        _ = FilesList.Focus();
    }

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

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || OutputPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        SetOutputExpanded(false);
        _ = FilesList.Focus();
        e.Handled = true;
    }

    private void SetOutputExpanded(bool open)
    {
        OutputPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        ViewButton.Content = open ? "Collapse" : "View";
    }
}
