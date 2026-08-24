using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Text;
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
        Closed += OnClosed;
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.DisposeAsync();
        }
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

    private async void OnCopyRequestBodyClick(object? sender, RoutedEventArgs e) =>
        await CopyBodyAsync(_viewModel?.SelectedSession?.RequestBody);

    private async void OnCopyResponseBodyClick(object? sender, RoutedEventArgs e) =>
        await CopyBodyAsync(_viewModel?.SelectedSession?.ResponseBody);

    private async void OnDownloadRequestBodyClick(object? sender, RoutedEventArgs e) =>
        await DownloadBodyAsync(_viewModel?.SelectedSession, isResponse: false);

    private async void OnDownloadResponseBodyClick(object? sender, RoutedEventArgs e) =>
        await DownloadBodyAsync(_viewModel?.SelectedSession, isResponse: true);

    private async Task CopyBodyAsync(string? body)
    {
        if (string.IsNullOrEmpty(body) || Clipboard is null) return;
        await Clipboard.SetTextAsync(body);
    }

    private async Task DownloadBodyAsync(CapturedSession? session, bool isResponse)
    {
        if (session is null) return;
        var body = isResponse ? session.ResponseBody : session.RequestBody;
        var headers = isResponse ? session.ResponseHeaders : session.RequestHeaders;
        var extension = headers.Contains("json", StringComparison.OrdinalIgnoreCase) ? ".json" : ".txt";
        var direction = isResponse ? "response" : "request";
        var safeHost = string.Concat(session.Host.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Download {direction} body",
            SuggestedFileName = $"{safeHost}-{direction}-body{extension}",
            FileTypeChoices =
            [
                new FilePickerFileType(extension == ".json" ? "JSON" : "Text")
                {
                    Patterns = [$"*{extension}"]
                },
                new FilePickerFileType("All files") { Patterns = ["*"] }
            ]
        });
        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(body);
    }
}
