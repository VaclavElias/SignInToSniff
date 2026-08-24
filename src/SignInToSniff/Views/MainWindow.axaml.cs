using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.ComponentModel;
using System.Text;
using SignInToSniff.Models;
using SignInToSniff.ViewModels;

namespace SignInToSniff.Views;

public sealed partial class MainWindow : Window
{
    private MainViewModel? _viewModel;
    private Bitmap? _responsePreviewBitmap;
    private int _previewLoadVersion;

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
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _ = LoadResponsePreviewAsync(viewModel.SelectedSession);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.VisibleSessionAdded -= OnVisibleSessionAdded;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
        }

        ClearResponsePreview();
    }

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedSession))
        {
            await LoadResponsePreviewAsync(_viewModel?.SelectedSession);
        }
    }

    private async Task LoadResponsePreviewAsync(CapturedSession? session)
    {
        var loadVersion = ++_previewLoadVersion;
        var bytes = session?.ResponseImageBytes;
        if (bytes is null)
        {
            ClearResponsePreview(incrementVersion: false);
            return;
        }

        Bitmap? bitmap = null;
        try
        {
            bitmap = await Task.Run(() =>
            {
                using var stream = new MemoryStream(bytes, writable: false);
                return Bitmap.DecodeToWidth(stream, 1200);
            });
        }
        catch
        {
            // A malformed or unsupported image should never disrupt request inspection.
        }

        if (loadVersion != _previewLoadVersion || bitmap is null)
        {
            bitmap?.Dispose();
            return;
        }

        var previous = _responsePreviewBitmap;
        _responsePreviewBitmap = bitmap;
        ResponseImagePreview.Source = bitmap;
        previous?.Dispose();
    }

    private void ClearResponsePreview(bool incrementVersion = true)
    {
        if (incrementVersion) _previewLoadVersion++;
        ResponseImagePreview.Source = null;
        _responsePreviewBitmap?.Dispose();
        _responsePreviewBitmap = null;
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
        var imageBytes = isResponse ? session.ResponseImageBytes : null;
        var body = isResponse ? session.ResponseBody : session.RequestBody;
        var headers = isResponse ? session.ResponseHeaders : session.RequestHeaders;
        var extension = imageBytes is not null
            ? GetImageExtension(session.ResponseContentType)
            : headers.Contains("json", StringComparison.OrdinalIgnoreCase) ? ".json" : ".txt";
        var direction = isResponse ? "response" : "request";
        var safeHost = string.Concat(session.Host.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Download {direction} body",
            SuggestedFileName = $"{safeHost}-{direction}-body{extension}",
            FileTypeChoices =
            [
                new FilePickerFileType(imageBytes is not null ? "Image" : extension == ".json" ? "JSON" : "Text")
                {
                    Patterns = [$"*{extension}"]
                },
                new FilePickerFileType("All files") { Patterns = ["*"] }
            ]
        });
        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        if (imageBytes is not null)
        {
            await stream.WriteAsync(imageBytes);
            return;
        }

        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(body);
    }

    private static string GetImageExtension(string? contentType) => contentType?.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/bmp" => ".bmp",
        "image/svg+xml" => ".svg",
        "image/x-icon" or "image/vnd.microsoft.icon" => ".ico",
        _ => ".img"
    };

    private async void OnInstallUserCertificateClick(object? sender, RoutedEventArgs e) =>
        await ChangeCertificateTrustAsync(install: true, machineWide: false);

    private async void OnInstallMachineCertificateClick(object? sender, RoutedEventArgs e) =>
        await ChangeCertificateTrustAsync(install: true, machineWide: true);

    private async void OnRemoveUserCertificateClick(object? sender, RoutedEventArgs e) =>
        await ChangeCertificateTrustAsync(install: false, machineWide: false);

    private async void OnRemoveMachineCertificateClick(object? sender, RoutedEventArgs e) =>
        await ChangeCertificateTrustAsync(install: false, machineWide: true);

    private async Task ChangeCertificateTrustAsync(bool install, bool machineWide)
    {
        if (_viewModel is null) return;
        var scope = machineWide ? "the whole local machine" : "your current user account";
        var warning = install
            ? $"Trust the SignInToSniff root certificate for {scope}?\n\nThis allows SignInToSniff to decrypt HTTPS traffic sent through its proxy. Only enable it on a device you control."
            : $"Remove SignInToSniff certificate trust from {scope}?\n\nHTTPS inspection for that scope will stop after the proxy restarts.";
        if (!await ShowConfirmationAsync(install ? "Enable HTTPS inspection" : "Remove HTTPS trust", warning)) return;

        var result = install
            ? await _viewModel.InstallCertificateAsync(machineWide)
            : await _viewModel.RemoveCertificateAsync(machineWide);
        await ShowNoticeAsync(result.Succeeded ? "Certificate updated" : "Certificate operation failed", result.Message);
    }

    private async Task<bool> ShowConfirmationAsync(string title, string message)
    {
        var dialog = CreateDialog(title, message, includeCancel: true);
        return await dialog.ShowDialog<bool>(this);
    }

    private async Task ShowNoticeAsync(string title, string message)
    {
        var dialog = CreateDialog(title, message, includeCancel: false);
        await dialog.ShowDialog<bool>(this);
    }

    private static Window CreateDialog(string title, string message, bool includeCancel)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 500,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var confirm = new Button { Content = includeCancel ? "Continue" : "OK", MinWidth = 90 };
        confirm.Click += (_, _) => dialog.Close(true);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { confirm }
        };
        if (includeCancel)
        {
            var cancel = new Button { Content = "Cancel", MinWidth = 90 };
            cancel.Click += (_, _) => dialog.Close(false);
            buttons.Children.Insert(0, cancel);
        }
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(22),
            Spacing = 20,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                buttons
            }
        };
        return dialog;
    }

    private void OnDeleteSessionClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null && sender is MenuItem { DataContext: CapturedSession session })
        {
            _viewModel.DeleteSession(session);
        }
    }

    private void OnSessionListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || _viewModel?.SelectedSession is not { } session) return;
        _viewModel.DeleteSession(session);
        e.Handled = true;
    }

    private async void OnExcludeExactHostClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null && sender is MenuItem { DataContext: CapturedSession session })
        {
            await _viewModel.ExcludeExactHostAsync(session);
        }
    }

    private async void OnExcludeSiteDomainClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null && sender is MenuItem { DataContext: CapturedSession session })
        {
            await _viewModel.ExcludeDomainAndSubdomainsAsync(session);
        }
    }

    private async void OnManageExclusionsClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        var window = new ExclusionsWindow { DataContext = _viewModel };
        await window.ShowDialog(this);
    }

    private async void OnManageTlsPassthroughClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        var window = new TlsPassthroughWindow { DataContext = _viewModel };
        await window.ShowDialog(this);
    }

    private async void OnAddExactTlsPassthroughClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null && sender is MenuItem { DataContext: CapturedSession session })
        {
            await _viewModel.AddTlsPassthroughRuleAsync(session.Host, SignInToSniff.Exclusions.ExclusionScope.ExactHost);
        }
    }

    private async void OnAddDomainTlsPassthroughClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null && sender is MenuItem { DataContext: CapturedSession session })
        {
            await _viewModel.AddTlsPassthroughRuleAsync(session.SiteDomain, SignInToSniff.Exclusions.ExclusionScope.DomainAndSubdomains);
        }
    }
}
