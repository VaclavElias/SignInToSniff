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

public sealed record ProxyMetrics(int ClientConnections, int ServerConnections);

public sealed record CertificateStatus(bool Exists, bool UserTrusted, bool MachineTrusted)
{
    public bool HttpsReady => UserTrusted || MachineTrusted;
}

public sealed record CertificateOperationResult(bool Succeeded, string Message, CertificateStatus Status);

public interface IProxyEngine : IAsyncDisposable
{
    string Endpoint { get; }

    ProxyState State { get; }

    ProxyMetrics Metrics { get; }

    event EventHandler<ProxyCaptureUpdate>? CaptureReceived;

    event EventHandler<ProxyStateChanged>? StateChanged;

    event EventHandler<ProxyMetrics>? MetricsChanged;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task<CertificateStatus> GetCertificateStatusAsync(CancellationToken cancellationToken = default);

    Task<CertificateOperationResult> InstallCertificateAsync(bool machineWide, CancellationToken cancellationToken = default);

    Task<CertificateOperationResult> RemoveCertificateAsync(bool machineWide, CancellationToken cancellationToken = default);
}
