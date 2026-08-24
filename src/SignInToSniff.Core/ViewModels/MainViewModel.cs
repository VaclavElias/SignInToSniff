using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SignInToSniff.Models;

namespace SignInToSniff.ViewModels;

public sealed partial class MainViewModel : ViewModelBase
{
    private readonly List<CapturedSession> _allSessions = [];
    private int _sampleSequence;

    [ObservableProperty]
    private string _domainFilter = string.Empty;

    [ObservableProperty]
    private bool _autoScroll = true;

    [ObservableProperty]
    private bool _showTimeColumn = true;

    [ObservableProperty]
    private bool _newestFirst;

    [ObservableProperty]
    private CapturedSession? _selectedSession;

    public MainViewModel()
    {
        foreach (var session in CreateInitialSamples())
        {
            _allSessions.Add(session);
            Sessions.Add(session);
        }

        SelectedSession = Sessions.FirstOrDefault();
    }

    public ObservableCollection<CapturedSession> Sessions { get; } = [];

    public event EventHandler<CapturedSession>? VisibleSessionAdded;

    public string ProxyStatus => "Proxy offline";

    public string Endpoint => "127.0.0.1:8000";

    public string SessionCountText => Sessions.Count == 1 ? "1 request" : $"{Sessions.Count} requests";

    public bool HasSessions => Sessions.Count > 0;

    partial void OnDomainFilterChanged(string value) => ApplyFilter();

    partial void OnNewestFirstChanged(bool value) => ApplyFilter();

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

    [RelayCommand]
    private void AddSample()
    {
        var sample = CreateNewSample(++_sampleSequence);
        _allSessions.Add(sample);

        if (MatchesFilter(sample))
        {
            if (NewestFirst)
            {
                Sessions.Insert(0, sample);
            }
            else
            {
                Sessions.Add(sample);
            }

            VisibleSessionAdded?.Invoke(this, sample);
        }

        NotifyCollectionSummaryChanged();
    }

    private void ApplyFilter()
    {
        var selectedId = SelectedSession?.Id;
        Sessions.Clear();

        var matchingSessions = _allSessions.Where(MatchesFilter);
        if (NewestFirst)
        {
            matchingSessions = matchingSessions.Reverse();
        }

        foreach (var session in matchingSessions)
        {
            Sessions.Add(session);
        }

        SelectedSession = Sessions.FirstOrDefault(session => session.Id == selectedId)
            ?? Sessions.FirstOrDefault();
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

    private static IEnumerable<CapturedSession> CreateInitialSamples()
    {
        var now = DateTimeOffset.Now;

        yield return new CapturedSession(
            Guid.NewGuid(), now.AddSeconds(-8), "GET", 200, "api.example.com",
            "https://api.example.com/v1/profile",
            "Accept: application/json\nAuthorization: Bearer ••••••••\nUser-Agent: SignInToSniff.Demo/1.0",
            "No request body",
            "Content-Type: application/json; charset=utf-8\nContent-Encoding: br\nCache-Control: no-store",
            "{\n  \"id\": 42,\n  \"displayName\": \"Ada Example\",\n  \"plan\": \"developer\"\n}", 184);

        yield return new CapturedSession(
            Guid.NewGuid(), now.AddSeconds(-5), "POST", 201, "auth.example.com",
            "https://auth.example.com/oauth/token",
            "Content-Type: application/json\nAccept: application/json",
            "{\n  \"grant_type\": \"client_credentials\"\n}",
            "Content-Type: application/json\nCache-Control: no-store",
            "{\n  \"access_token\": \"••••••••\",\n  \"expires_in\": 3600\n}", 327);

        yield return new CapturedSession(
            Guid.NewGuid(), now.AddSeconds(-2), "GET", 404, "cdn.example.net",
            "https://cdn.example.net/assets/avatar.png",
            "Accept: image/avif,image/webp,*/*",
            "No request body",
            "Content-Type: application/json\nContent-Length: 34",
            "{\n  \"error\": \"asset not found\"\n}", 76);
    }

    private static CapturedSession CreateNewSample(int sequence)
    {
        var status = sequence % 4 == 0 ? 500 : 200;
        var host = sequence % 2 == 0 ? "api.example.com" : "telemetry.example.dev";

        return new CapturedSession(
            Guid.NewGuid(), DateTimeOffset.Now, sequence % 3 == 0 ? "POST" : "GET", status, host,
            $"https://{host}/demo/requests/{sequence}",
            "Accept: application/json\nX-Demo-Traffic: true",
            sequence % 3 == 0 ? $"{{\n  \"sequence\": {sequence}\n}}" : "No request body",
            "Content-Type: application/json; charset=utf-8",
            $"{{\n  \"demo\": true,\n  \"sequence\": {sequence}\n}}", 40 + sequence);
    }
}
