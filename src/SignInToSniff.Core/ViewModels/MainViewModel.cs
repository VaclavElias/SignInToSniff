using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SignInToSniff.Models;
using SignInToSniff.Proxy;
using SignInToSniff.Threading;
using SignInToSniff.Launching;
using SignInToSniff.Exclusions;

namespace SignInToSniff.ViewModels;

public sealed partial class MainViewModel : ViewModelBase, IAsyncDisposable
{
    private const int SessionLimit = 5_000;
    private readonly List<CapturedSession> _allSessions = [];
    private readonly IProxyEngine _proxyEngine;
    private readonly IUiDispatcher _dispatcher;
    private readonly IClientLauncher _clientLauncher;
    private readonly IExclusionStore _exclusionStore;
    private bool _disposed;
    private int _totalCaptured;

    [ObservableProperty] private string _domainFilter = string.Empty;
    [ObservableProperty] private bool _autoScroll = true;
    [ObservableProperty] private bool _showTimeColumn = true;
    [ObservableProperty] private bool _showSizeColumn = true;
    [ObservableProperty] private bool _newestFirst;
    [ObservableProperty] private CapturedSession? _selectedSession;
    [ObservableProperty] private ProxyState _proxyState;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _certificateStatusText = "Certificate status unknown";

    public MainViewModel(IProxyEngine proxyEngine, IUiDispatcher dispatcher, IClientLauncher clientLauncher, IExclusionStore exclusionStore)
    {
        _proxyEngine = proxyEngine;
        _dispatcher = dispatcher;
        _clientLauncher = clientLauncher;
        _exclusionStore = exclusionStore;
        foreach (var rule in exclusionStore.Load().Distinct()) Exclusions.Add(rule);
        ProxyState = proxyEngine.State;
        proxyEngine.CaptureReceived += OnCaptureReceived;
        proxyEngine.StateChanged += OnProxyStateChanged;
        _ = RefreshCertificateStatusAsync();
    }

    public ObservableCollection<CapturedSession> Sessions { get; } = [];
    public ObservableCollection<ExclusionRule> Exclusions { get; } = [];

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
    public string TotalCapturedText => $"Total captured: {_totalCaptured:N0}";
    public string HiddenRequestsText => $"Hidden: {Math.Max(0, _allSessions.Count - Sessions.Count):N0}";
    public string ExclusionCountText => $"Exclusion rules: {Exclusions.Count:N0}";

    public void DeleteSession(CapturedSession session)
    {
        var removed = _allSessions.RemoveAll(item => item.Id == session.Id) > 0;
        var visible = Sessions.FirstOrDefault(item => item.Id == session.Id);
        if (visible is not null) Sessions.Remove(visible);
        if (removed && _totalCaptured > 0) _totalCaptured--;
        if (SelectedSession?.Id == session.Id) SelectedSession = Sessions.FirstOrDefault();
        NotifyCollectionSummaryChanged();
    }

    public Task ExcludeExactHostAsync(CapturedSession session) =>
        AddExclusionAsync(session.Host, ExclusionScope.ExactHost);

    public Task ExcludeDomainAndSubdomainsAsync(CapturedSession session) =>
        AddExclusionAsync(session.SiteDomain, ExclusionScope.DomainAndSubdomains);

    public async Task AddExclusionAsync(string domain, ExclusionScope scope)
    {
        var normalized = NormalizeDomain(domain);
        if (normalized.Length == 0) return;
        var rule = new ExclusionRule(normalized, scope);
        if (scope == ExclusionScope.ExactHost && Exclusions.Any(existing =>
                existing.Scope == ExclusionScope.DomainAndSubdomains && existing.Matches(normalized)))
        {
            return;
        }
        if (scope == ExclusionScope.DomainAndSubdomains)
        {
            for (var index = Exclusions.Count - 1; index >= 0; index--)
            {
                if (rule.Matches(Exclusions[index].Domain)) Exclusions.RemoveAt(index);
            }
        }
        if (!Exclusions.Contains(rule)) Exclusions.Add(rule);
        ApplyFilter();
        await PersistExclusionsAsync();
    }

    public async Task RemoveExclusionAsync(ExclusionRule rule)
    {
        Exclusions.Remove(rule);
        ApplyFilter();
        await PersistExclusionsAsync();
    }

    public async Task<CertificateOperationResult> InstallCertificateAsync(bool machineWide)
    {
        var result = await _proxyEngine.InstallCertificateAsync(machineWide);
        ApplyCertificateStatus(result.Status);
        ErrorMessage = result.Succeeded ? null : result.Message;
        return result;
    }

    public async Task<CertificateOperationResult> RemoveCertificateAsync(bool machineWide)
    {
        var result = await _proxyEngine.RemoveCertificateAsync(machineWide);
        ApplyCertificateStatus(result.Status);
        ErrorMessage = result.Succeeded ? null : result.Message;
        return result;
    }

    public async Task RefreshCertificateStatusAsync()
    {
        var status = await _proxyEngine.GetCertificateStatusAsync();
        await _dispatcher.InvokeAsync(() => ApplyCertificateStatus(status));
    }

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
        _totalCaptured = 0;
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
        _totalCaptured++;
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
        !IsExcluded(session.Host) && (string.IsNullOrWhiteSpace(DomainFilter)
        || session.Host.Contains(DomainFilter.Trim(), StringComparison.OrdinalIgnoreCase));

    private bool IsExcluded(string host) => Exclusions.Any(rule => rule.Matches(host));

    private static string NormalizeDomain(string domain)
    {
        var value = domain.Trim().TrimEnd('.').ToLowerInvariant();
        if (value.StartsWith("*.", StringComparison.Ordinal)) value = value[2..];
        return Uri.CheckHostName(value) == UriHostNameType.Unknown ? string.Empty : value;
    }

    private async Task PersistExclusionsAsync()
    {
        try
        {
            await _exclusionStore.SaveAsync(Exclusions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"Could not save exclusions: {exception.Message}";
        }
    }

    private void NotifyCollectionSummaryChanged()
    {
        OnPropertyChanged(nameof(SessionCountText));
        OnPropertyChanged(nameof(HasSessions));
        OnPropertyChanged(nameof(TotalCapturedText));
        OnPropertyChanged(nameof(HiddenRequestsText));
        OnPropertyChanged(nameof(ExclusionCountText));
    }

    private async Task ApplyLaunchResultAsync(Task<ClientLaunchResult> launchTask)
    {
        ErrorMessage = null;
        var result = await launchTask;
        if (!result.Succeeded) ErrorMessage = result.ErrorMessage;
    }

    private void ApplyCertificateStatus(CertificateStatus status) => CertificateStatusText = status switch
    {
        { MachineTrusted: true } => "HTTPS certificate: machine trusted",
        { UserTrusted: true } => "HTTPS certificate: user trusted",
        { Exists: true } => "HTTPS certificate: not trusted",
        _ => "HTTPS certificate: not created"
    };
}
