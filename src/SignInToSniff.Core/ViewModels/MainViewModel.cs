using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SignInToSniff.Models;
using SignInToSniff.Proxy;
using SignInToSniff.Threading;
using SignInToSniff.Launching;

namespace SignInToSniff.ViewModels;

public sealed partial class MainViewModel : ViewModelBase, IAsyncDisposable
{
    private const int SessionLimit = 5_000;
    private readonly List<CapturedSession> _allSessions = [];
    private readonly IProxyEngine _proxyEngine;
    private readonly IUiDispatcher _dispatcher;
    private readonly IClientLauncher _clientLauncher;
    private bool _disposed;

    [ObservableProperty] private string _domainFilter = string.Empty;
    [ObservableProperty] private bool _autoScroll = true;
    [ObservableProperty] private bool _showTimeColumn = true;
    [ObservableProperty] private bool _newestFirst;
    [ObservableProperty] private CapturedSession? _selectedSession;
    [ObservableProperty] private ProxyState _proxyState;
    [ObservableProperty] private string? _errorMessage;

    public MainViewModel(IProxyEngine proxyEngine, IUiDispatcher dispatcher, IClientLauncher clientLauncher)
    {
        _proxyEngine = proxyEngine;
        _dispatcher = dispatcher;
        _clientLauncher = clientLauncher;
        ProxyState = proxyEngine.State;
        proxyEngine.CaptureReceived += OnCaptureReceived;
        proxyEngine.StateChanged += OnProxyStateChanged;
    }

    public ObservableCollection<CapturedSession> Sessions { get; } = [];

    public event EventHandler<CapturedSession>? VisibleSessionAdded;

    public string ProxyStatus => ProxyState switch
    {
        ProxyState.Starting => "Proxy starting…",
        ProxyState.Running => "Proxy running",
        ProxyState.Stopping => "Proxy stopping…",
        ProxyState.Faulted => "Proxy faulted",
        _ => "Proxy offline"
    };

    public string Endpoint => _proxyEngine.Endpoint;
    public string SessionCountText => Sessions.Count == 1 ? "1 request" : $"{Sessions.Count} requests";
    public bool HasSessions => Sessions.Count > 0;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool CanStartProxy => ProxyState is ProxyState.Stopped or ProxyState.Faulted;
    public bool CanStopProxy => ProxyState == ProxyState.Running;

    partial void OnDomainFilterChanged(string value) => ApplyFilter();
    partial void OnNewestFirstChanged(bool value) => ApplyFilter();

    partial void OnProxyStateChanged(ProxyState value)
    {
        OnPropertyChanged(nameof(ProxyStatus));
        OnPropertyChanged(nameof(CanStartProxy));
        OnPropertyChanged(nameof(CanStopProxy));
        StartProxyCommand.NotifyCanExecuteChanged();
        StopProxyCommand.NotifyCanExecuteChanged();
        LaunchFreshChromeCommand.NotifyCanExecuteChanged();
        LaunchFreshTerminalCommand.NotifyCanExecuteChanged();
    }

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    [RelayCommand(CanExecute = nameof(CanStartProxy))]
    private async Task StartProxyAsync()
    {
        ErrorMessage = null;
        await _proxyEngine.StartAsync();
    }

    [RelayCommand(CanExecute = nameof(CanStopProxy))]
    private async Task StopProxyAsync() => await _proxyEngine.StopAsync();

    [RelayCommand(CanExecute = nameof(CanStopProxy))]
    private async Task LaunchFreshChromeAsync() =>
        await ApplyLaunchResultAsync(_clientLauncher.LaunchFreshChromeAsync(Endpoint));

    [RelayCommand(CanExecute = nameof(CanStopProxy))]
    private async Task LaunchFreshTerminalAsync() =>
        await ApplyLaunchResultAsync(_clientLauncher.LaunchFreshTerminalAsync(Endpoint));

    [RelayCommand]
    private void ClearDomainFilter() => DomainFilter = string.Empty;

    [RelayCommand]
    private void ClearLogs()
    {
        _allSessions.Clear();
        Sessions.Clear();
        SelectedSession = null;
        NotifyCollectionSummaryChanged();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _proxyEngine.CaptureReceived -= OnCaptureReceived;
        _proxyEngine.StateChanged -= OnProxyStateChanged;
        await _proxyEngine.DisposeAsync();
    }

    private void OnCaptureReceived(object? sender, ProxyCaptureUpdate update) =>
        _ = _dispatcher.InvokeAsync(() => ApplyCaptureUpdate(update));

    private void OnProxyStateChanged(object? sender, ProxyStateChanged update) =>
        _ = _dispatcher.InvokeAsync(() =>
        {
            ProxyState = update.State;
            ErrorMessage = update.ErrorMessage;
        });

    private void ApplyCaptureUpdate(ProxyCaptureUpdate update)
    {
        if (update.Kind == CaptureUpdateKind.Updated) ReplaceSession(update.Session);
        else AddSession(update.Session);
    }

    private void AddSession(CapturedSession session)
    {
        _allSessions.Add(session);
        if (_allSessions.Count > SessionLimit)
        {
            var expired = _allSessions[0];
            _allSessions.RemoveAt(0);
            var visibleExpired = Sessions.FirstOrDefault(item => item.Id == expired.Id);
            if (visibleExpired is not null) Sessions.Remove(visibleExpired);
        }

        if (MatchesFilter(session))
        {
            if (NewestFirst) Sessions.Insert(0, session);
            else Sessions.Add(session);
            SelectedSession ??= session;
            VisibleSessionAdded?.Invoke(this, session);
        }

        NotifyCollectionSummaryChanged();
    }

    private void ReplaceSession(CapturedSession session)
    {
        var allIndex = _allSessions.FindIndex(item => item.Id == session.Id);
        if (allIndex < 0) return;
        _allSessions[allIndex] = session;

        var visible = Sessions.FirstOrDefault(item => item.Id == session.Id);
        if (visible is null) return;
        var visibleIndex = Sessions.IndexOf(visible);
        var wasSelected = SelectedSession?.Id == session.Id;
        Sessions[visibleIndex] = session;
        if (wasSelected) SelectedSession = session;
    }

    private void ApplyFilter()
    {
        var selectedId = SelectedSession?.Id;
        Sessions.Clear();
        var matching = _allSessions.Where(MatchesFilter);
        if (NewestFirst) matching = matching.Reverse();
        foreach (var session in matching) Sessions.Add(session);
        SelectedSession = Sessions.FirstOrDefault(session => session.Id == selectedId) ?? Sessions.FirstOrDefault();
        NotifyCollectionSummaryChanged();
    }

    private bool MatchesFilter(CapturedSession session) =>
        string.IsNullOrWhiteSpace(DomainFilter)
        || session.Host.Contains(DomainFilter.Trim(), StringComparison.OrdinalIgnoreCase);

    private void NotifyCollectionSummaryChanged()
    {
        OnPropertyChanged(nameof(SessionCountText));
        OnPropertyChanged(nameof(HasSessions));
    }

    private async Task ApplyLaunchResultAsync(Task<ClientLaunchResult> launchTask)
    {
        ErrorMessage = null;
        var result = await launchTask;
        if (!result.Succeeded) ErrorMessage = result.ErrorMessage;
    }
}
