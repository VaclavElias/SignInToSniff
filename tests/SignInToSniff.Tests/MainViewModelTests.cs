using SignInToSniff.Models;
using SignInToSniff.Proxy;
using SignInToSniff.Threading;
using SignInToSniff.ViewModels;
using SignInToSniff.Launching;
using Xunit;

namespace SignInToSniff.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void DomainFilter_MatchesHostCaseInsensitively()
    {
        var (viewModel, engine) = CreateViewModel();
        engine.Add(CreateSession("api.example.com"));
        engine.Add(CreateSession("cdn.example.net"));

        viewModel.DomainFilter = "API.EXAMPLE.COM";

        Assert.Single(viewModel.Sessions);
        Assert.Equal("api.example.com", viewModel.Sessions[0].Host);
    }

    [Fact]
    public void ClearLogs_RemovesSessionsAndSelection()
    {
        var (viewModel, engine) = CreateViewModel();
        engine.Add(CreateSession("api.example.com"));
        viewModel.SelectedSession = viewModel.Sessions[0];

        viewModel.ClearLogsCommand.Execute(null);

        Assert.Empty(viewModel.Sessions);
        Assert.Null(viewModel.SelectedSession);
        Assert.False(viewModel.HasSessions);
    }

    [Fact]
    public void NewCapture_WithAutoScrollDisabled_DoesNotChangeSelection()
    {
        var (viewModel, engine) = CreateViewModel();
        engine.Add(CreateSession("first.example"));
        viewModel.SelectedSession = viewModel.Sessions[0];
        viewModel.AutoScroll = false;
        var selected = viewModel.SelectedSession;

        engine.Add(CreateSession("second.example"));

        Assert.Same(selected, viewModel.SelectedSession);
    }

    [Fact]
    public void FirstCapture_IsSelectedForImmediateInspection()
    {
        var (viewModel, engine) = CreateViewModel();
        var first = CreateSession("first.example");

        engine.Add(first);

        Assert.Same(first, viewModel.SelectedSession);
    }

    [Fact]
    public void NewestFirst_InsertsAndReportsNewCaptureAtTop()
    {
        var (viewModel, engine) = CreateViewModel();
        viewModel.NewestFirst = true;
        CapturedSession? reported = null;
        viewModel.VisibleSessionAdded += (_, session) => reported = session;

        engine.Add(CreateSession("newest.example"));

        Assert.NotNull(reported);
        Assert.Same(reported, viewModel.Sessions[0]);
    }

    [Fact]
    public void UpdatingResponse_ReplacesRowAndPreservesSelection()
    {
        var (viewModel, engine) = CreateViewModel();
        var pending = CreateSession("api.example.com");
        engine.Add(pending);
        viewModel.SelectedSession = pending;

        engine.Update(pending with { StatusCode = 204, ResponseHeaders = "Server: local", DurationMilliseconds = 12 });

        Assert.Equal(204, viewModel.Sessions[0].StatusCode);
        Assert.Same(viewModel.Sessions[0], viewModel.SelectedSession);
    }

    [Fact]
    public async Task StartAndStopCommands_ReflectEngineState()
    {
        var (viewModel, _) = CreateViewModel();

        await viewModel.StartProxyCommand.ExecuteAsync(null);
        Assert.Equal(ProxyState.Running, viewModel.ProxyState);
        Assert.True(viewModel.CanStopProxy);

        await viewModel.StopProxyCommand.ExecuteAsync(null);
        Assert.Equal(ProxyState.Stopped, viewModel.ProxyState);
        Assert.True(viewModel.CanStartProxy);
    }

    [Fact]
    public void ClearDomainFilter_RestoresAllSessions()
    {
        var (viewModel, engine) = CreateViewModel();
        engine.Add(CreateSession("api.example.com"));
        engine.Add(CreateSession("cdn.example.net"));
        viewModel.DomainFilter = "api.example.com";

        viewModel.ClearDomainFilterCommand.Execute(null);

        Assert.Equal(2, viewModel.Sessions.Count);
        Assert.Equal(string.Empty, viewModel.DomainFilter);
    }

    private static (MainViewModel ViewModel, FakeProxyEngine Engine) CreateViewModel()
    {
        var engine = new FakeProxyEngine();
        return (new MainViewModel(engine, new InlineUiDispatcher(), new FakeClientLauncher()), engine);
    }

    private static CapturedSession CreateSession(string host) => new(
        Guid.NewGuid(), DateTimeOffset.Now, "GET", null, host, $"http://{host}/",
        "Accept: */*", "Not captured", "Waiting", "Not captured", null);

    private sealed class FakeProxyEngine : IProxyEngine
    {
        public string Endpoint => "127.0.0.1:8000";
        public ProxyState State { get; private set; }
        public event EventHandler<ProxyCaptureUpdate>? CaptureReceived;
        public event EventHandler<ProxyStateChanged>? StateChanged;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            SetState(ProxyState.Starting);
            SetState(ProxyState.Running);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            SetState(ProxyState.Stopping);
            SetState(ProxyState.Stopped);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Add(CapturedSession session) =>
            CaptureReceived?.Invoke(this, new ProxyCaptureUpdate(CaptureUpdateKind.Added, session));

        public void Update(CapturedSession session) =>
            CaptureReceived?.Invoke(this, new ProxyCaptureUpdate(CaptureUpdateKind.Updated, session));

        private void SetState(ProxyState state)
        {
            State = state;
            StateChanged?.Invoke(this, new ProxyStateChanged(state));
        }
    }

    private sealed class FakeClientLauncher : IClientLauncher
    {
        public Task<ClientLaunchResult> LaunchFreshChromeAsync(string proxyEndpoint) =>
            Task.FromResult(new ClientLaunchResult(true));

        public Task<ClientLaunchResult> LaunchFreshTerminalAsync(string proxyEndpoint) =>
            Task.FromResult(new ClientLaunchResult(true));
    }
}
