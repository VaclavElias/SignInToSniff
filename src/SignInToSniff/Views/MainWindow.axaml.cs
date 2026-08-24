using Avalonia.Controls;
using Avalonia.Interactivity;
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

        SessionList.ScrollIntoView(newest);
    }
}
