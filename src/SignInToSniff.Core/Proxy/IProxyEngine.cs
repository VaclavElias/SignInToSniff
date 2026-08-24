using SignInToSniff.Models;

namespace SignInToSniff.Proxy;

public enum ProxyState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted
}

public enum CaptureUpdateKind
{
    Added,
    Updated
}

public sealed record ProxyCaptureUpdate(CaptureUpdateKind Kind, CapturedSession Session);

public sealed record ProxyStateChanged(ProxyState State, string? ErrorMessage = null);

public interface IProxyEngine : IAsyncDisposable
{
    string Endpoint { get; }

    ProxyState State { get; }

    event EventHandler<ProxyCaptureUpdate>? CaptureReceived;

    event EventHandler<ProxyStateChanged>? StateChanged;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
