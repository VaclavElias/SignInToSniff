using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SignInToSniff.Models;
using SignInToSniff.ViewModels;

namespace SignInToSniff.Views;

public sealed partial class MainWindow : Window
{
    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _viewModel = viewModel;
        viewModel.VisibleSessionAdded += OnVisibleSessionAdded;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.VisibleSessionAdded -= OnVisibleSessionAdded;
            _viewModel = null;
        }
    }

    private void OnVisibleSessionAdded(object? sender, CapturedSession newest)
    {
        if (_viewModel?.AutoScroll != true)
        {
            return;
        }

        if (!_viewModel.NewestFirst)
        {
            SessionList.ScrollIntoView(newest);
            return;
        }

        // ListBox preserves the selected container's viewport position when items are inserted
        // ahead of it. Run after layout and explicitly pin the tape to its new top instead of
        // relying on ScrollIntoView's minimal movement.
        Dispatcher.UIThread.Post(
            () =>
            {
                var scrollViewer = SessionList
                    .GetVisualDescendants()
                    .OfType<ScrollViewer>()
                    .FirstOrDefault();

                if (scrollViewer is not null)
                {
                    scrollViewer.Offset = scrollViewer.Offset.WithY(0);
                }
            },
            DispatcherPriority.Background);
    }
}
