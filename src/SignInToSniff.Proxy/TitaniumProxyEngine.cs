using System.Diagnostics;
using System.Net;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using SignInToSniff.Models;
using SignInToSniff.Exclusions;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Models;

namespace SignInToSniff.Proxy;

public sealed class TitaniumProxyEngine : IProxyEngine
{
    private const int ListenPort = 8000;
    private const string RootCertificateName = "SignInToSniff Root Certificate Authority";
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly Channel<ProxyCaptureUpdate> _updates = Channel.CreateBounded<ProxyCaptureUpdate>(
        new BoundedChannelOptions(2_048)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });
    private readonly CancellationTokenSource _pumpCancellation = new();
    private readonly Task _pumpTask;
    private ProxyServer? _proxyServer;
    private ExplicitProxyEndPoint? _endPoint;
    private bool _disposed;
    private readonly IHostRuleSet? _tlsPassthroughRules;

    public TitaniumProxyEngine(IHostRuleSet? tlsPassthroughRules = null)
    {
        _tlsPassthroughRules = tlsPassthroughRules;
        _pumpTask = PumpUpdatesAsync(_pumpCancellation.Token);
    }

    public string Endpoint => $"127.0.0.1:{ListenPort}";

    public ProxyState State { get; private set; } = ProxyState.Stopped;

    public ProxyMetrics Metrics { get; private set; } = new(0, 0);

    public event EventHandler<ProxyCaptureUpdate>? CaptureReceived;

    public event EventHandler<ProxyStateChanged>? StateChanged;

    public event EventHandler<ProxyMetrics>? MetricsChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (State is ProxyState.Running or ProxyState.Starting)
            {
                return;
            }

            SetState(ProxyState.Starting);

            try
            {
                var proxyServer = CreateProxyServer();
                var certificateStatus = GetCertificateStatus(proxyServer);
                var endPoint = new ExplicitProxyEndPoint(IPAddress.Loopback, ListenPort, decryptSsl: certificateStatus.HttpsReady);

                proxyServer.BeforeRequest += OnBeforeRequestAsync;
                proxyServer.BeforeResponse += OnBeforeResponseAsync;
                proxyServer.AfterResponse += OnAfterResponseAsync;
                proxyServer.ClientConnectionCountChanged += OnConnectionCountChanged;
                proxyServer.ServerConnectionCountChanged += OnConnectionCountChanged;
                endPoint.BeforeTunnelConnectRequest += OnBeforeTunnelConnectRequestAsync;
                proxyServer.AddEndPoint(endPoint);
                proxyServer.Start();

                _proxyServer = proxyServer;
                _endPoint = endPoint;
                SetState(ProxyState.Running);
            }
            catch (Exception exception)
            {
                CleanupServer();
                SetState(ProxyState.Faulted, CreateStartupError(exception));
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (State is ProxyState.Stopped or ProxyState.Stopping)
            {
                return;
            }

            SetState(ProxyState.Stopping);
            CleanupServer();
            SetState(ProxyState.Stopped);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _disposed = true;
        _updates.Writer.TryComplete();
        _pumpCancellation.Cancel();

        try
        {
            await _pumpTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _pumpCancellation.Dispose();
        _lifecycleLock.Dispose();
    }

    public Task<CertificateStatus> GetCertificateStatusAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var server = CreateProxyServer();
            return GetCertificateStatus(server);
        }, cancellationToken);

    public Task<CertificateOperationResult> InstallCertificateAsync(bool machineWide, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var server = CreateProxyServer();
            if (!server.CertificateManager.CreateRootCertificate())
            {
                var failedStatus = GetCertificateStatus(server);
                return new CertificateOperationResult(false, "Could not create the SignInToSniff root certificate.", failedStatus);
            }

            var attempted = machineWide
                ? server.CertificateManager.TrustRootCertificateAsAdmin(machineTrusted: true)
                : TrustForCurrentUser(server);
            var status = GetCertificateStatus(server);
            var succeeded = attempted && (machineWide ? status.MachineTrusted : status.UserTrusted);
            var message = succeeded
                ? $"Certificate trusted for the {(machineWide ? "local machine" : "current user")}. Restart the proxy to enable HTTPS inspection."
                : "Certificate installation was cancelled or did not complete.";
            return new CertificateOperationResult(succeeded, message, status);
        }, cancellationToken);

    public Task<CertificateOperationResult> RemoveCertificateAsync(bool machineWide, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var server = CreateProxyServer();
            if (server.CertificateManager.RootCertificate is null)
            {
                return new CertificateOperationResult(true, "No SignInToSniff root certificate is installed.", new CertificateStatus(false, false, false));
            }

            var attempted = machineWide
                ? server.CertificateManager.RemoveTrustedRootCertificateAsAdmin(machineTrusted: true)
                : RemoveForCurrentUser(server);
            var status = GetCertificateStatus(server);
            var succeeded = attempted && (machineWide ? !status.MachineTrusted : !status.UserTrusted);
            return new CertificateOperationResult(
                succeeded,
                succeeded ? "Certificate trust was removed. Restart the proxy to disable HTTPS inspection." : "Certificate removal was cancelled or did not complete.",
                status);
        }, cancellationToken);

    private async Task OnBeforeRequestAsync(object sender, SessionEventArgs eventArgs)
    {
        var request = eventArgs.HttpClient.Request;
        var uri = request.RequestUri;
        var headers = request.Headers.Select(header => header.ToString()).ToArray();
        var formattedHeaders = FormatHeaders(headers);
        var requestBody = await CaptureBodyAsync(
            request.HasBody,
            request.ContentLength,
            BodyCaptureFormatter.FindHeader(headers, "Content-Type"),
            eventArgs.GetRequestBody,
            "request").ConfigureAwait(false);
        var session = new CapturedSession(
            Guid.NewGuid(),
            DateTimeOffset.Now,
            request.Method,
            null,
            uri.Host,
            request.Url,
            formattedHeaders,
            requestBody.Text,
            "Waiting for response…",
            "Waiting for response body…",
            null)
        {
            Protocol = FormatProtocol(request.HttpVersion),
            RequestContentType = requestBody.ContentType,
            ReceivedBytes = GetCapturedSize(formattedHeaders, request.ContentLength, requestBody.ByteCount)
        };

        eventArgs.HttpClient.UserData = new CaptureState(session, Stopwatch.StartNew());
        _updates.Writer.TryWrite(new ProxyCaptureUpdate(CaptureUpdateKind.Added, session));
    }

    private async Task OnBeforeResponseAsync(object sender, SessionEventArgs eventArgs)
    {
        if (eventArgs.HttpClient.UserData is not CaptureState state)
        {
            return;
        }

        state.Stopwatch.Stop();
        var response = eventArgs.HttpClient.Response;
        var headers = response.Headers.Select(header => header.ToString()).ToArray();
        var formattedHeaders = FormatHeaders(headers);
        var responseBody = await CaptureBodyAsync(
            response.HasBody,
            response.ContentLength,
            BodyCaptureFormatter.FindHeader(headers, "Content-Type"),
            eventArgs.GetResponseBody,
            "response",
            allowImagePreview: true).ConfigureAwait(false);
        var completed = state.Session with
        {
            StatusCode = response.StatusCode,
            ResponseHeaders = formattedHeaders,
            ResponseBody = responseBody.Text,
            ResponseSizeBytes = response.ContentLength > 0 ? response.ContentLength : responseBody.ByteCount,
            Protocol = FormatProtocol(response.HttpVersion),
            SentBytes = GetCapturedSize(formattedHeaders, response.ContentLength, responseBody.ByteCount),
            ResponseImageBytes = responseBody.ImageBytes,
            ResponseContentType = responseBody.ContentType,
            DurationMilliseconds = state.Stopwatch.ElapsedMilliseconds
        };

        state.Session = completed;
        _updates.Writer.TryWrite(new ProxyCaptureUpdate(CaptureUpdateKind.Updated, completed));
    }

    private Task OnAfterResponseAsync(object sender, SessionEventArgs eventArgs)
    {
        if (eventArgs.HttpClient.UserData is not CaptureState state) return Task.CompletedTask;
        var completed = state.Session with
        {
            ProxyError = eventArgs.Exception?.GetBaseException().Message
        };
        state.Session = completed;
        _updates.Writer.TryWrite(new ProxyCaptureUpdate(CaptureUpdateKind.Updated, completed));
        return Task.CompletedTask;
    }

    private Task OnBeforeTunnelConnectRequestAsync(object sender, TunnelConnectSessionEventArgs eventArgs)
    {
        if (_tlsPassthroughRules?.Matches(eventArgs.HttpClient.Request.RequestUri.Host) == true)
        {
            eventArgs.DecryptSsl = false;
        }
        return Task.CompletedTask;
    }

    private static async Task<BodyCaptureResult> CaptureBodyAsync(
        bool hasBody,
        long contentLength,
        string? contentType,
        Func<CancellationToken, Task<byte[]>> readBody,
        string direction,
        bool allowImagePreview = false)
    {
        if (!hasBody) return new BodyCaptureResult($"No {direction} body", 0);
        var mediaType = (contentType ?? string.Empty).Split(';', 2)[0].Trim().ToLowerInvariant();
        if (allowImagePreview && mediaType.StartsWith("image/", StringComparison.Ordinal))
        {
            if (contentLength <= 0)
            {
                return new BodyCaptureResult("[Image preview omitted: response size is unknown.]", null);
            }
            if (contentLength > BodyCaptureFormatter.MaxCapturedBodyBytes)
            {
                return new BodyCaptureResult(
                    $"[Image preview omitted: declared size {contentLength:N0} bytes exceeds the 1 MiB preview limit.]",
                    contentLength);
            }
            try
            {
                var imageBytes = await readBody(CancellationToken.None).ConfigureAwait(false);
                if (imageBytes.Length > BodyCaptureFormatter.MaxCapturedBodyBytes)
                {
                    return new BodyCaptureResult("[Image preview omitted: decoded data exceeds the 1 MiB preview limit.]", imageBytes.LongLength);
                }
                return new BodyCaptureResult(
                    $"[Image preview: {mediaType}, {imageBytes.Length:N0} bytes]",
                    imageBytes.LongLength,
                    imageBytes,
                    mediaType);
            }
            catch (Exception exception)
            {
                return new BodyCaptureResult($"[Image preview capture failed: {exception.Message}]", null);
            }
        }
        if (!BodyCaptureFormatter.ShouldRead(contentType, contentLength, out var omissionReason))
        {
            return new BodyCaptureResult(omissionReason!, contentLength > 0 ? contentLength : null);
        }

        try
        {
            var body = await readBody(CancellationToken.None).ConfigureAwait(false);
            // Titanium exposes a decoded inspection buffer even though the original
            // Content-Encoding header remains present on the proxied response.
            return new BodyCaptureResult(
                BodyCaptureFormatter.Format(body, contentType, contentEncoding: null),
                body.LongLength,
                ContentType: mediaType);
        }
        catch (Exception exception)
        {
            return new BodyCaptureResult($"[{char.ToUpperInvariant(direction[0])}{direction[1..]} body capture failed: {exception.Message}]", null);
        }
    }

    private async Task PumpUpdatesAsync(CancellationToken cancellationToken)
    {
        await foreach (var update in _updates.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            CaptureReceived?.Invoke(this, update);
        }
    }

    private void CleanupServer()
    {
        if (_proxyServer is null)
        {
            return;
        }

        _proxyServer.BeforeRequest -= OnBeforeRequestAsync;
        _proxyServer.BeforeResponse -= OnBeforeResponseAsync;
        _proxyServer.AfterResponse -= OnAfterResponseAsync;
        _proxyServer.ClientConnectionCountChanged -= OnConnectionCountChanged;
        _proxyServer.ServerConnectionCountChanged -= OnConnectionCountChanged;
        if (_endPoint is not null) _endPoint.BeforeTunnelConnectRequest -= OnBeforeTunnelConnectRequestAsync;

        try
        {
            _proxyServer.Stop();
        }
        finally
        {
            _proxyServer.Dispose();
            _proxyServer = null;
            _endPoint = null;
            Metrics = new ProxyMetrics(0, 0);
            MetricsChanged?.Invoke(this, Metrics);
        }
    }

    private void SetState(ProxyState state, string? errorMessage = null)
    {
        State = state;
        StateChanged?.Invoke(this, new ProxyStateChanged(state, errorMessage));
    }

    private static string FormatHeaders(IEnumerable<string> headers)
    {
        var formatted = string.Join(Environment.NewLine, headers);
        return string.IsNullOrWhiteSpace(formatted) ? "No headers" : formatted;
    }

    private static string CreateStartupError(Exception exception) =>
        exception is System.Net.Sockets.SocketException
            ? $"Could not start the proxy on 127.0.0.1:{ListenPort}. The port may already be in use."
            : $"Could not start the proxy: {exception.Message}";

    private static string FormatProtocol(Version version) => version.Major switch
    {
        >= 2 => $"HTTP/{version.Major}",
        > 0 => $"HTTP/{version.Major}.{version.Minor}",
        _ => "HTTP/?"
    };

    private static long GetCapturedSize(string headers, long declaredBodySize, long? inspectedBodySize) =>
        System.Text.Encoding.UTF8.GetByteCount(headers) + Math.Max(0, declaredBodySize > 0 ? declaredBodySize : inspectedBodySize ?? 0);

    private void OnConnectionCountChanged(object? sender, EventArgs eventArgs)
    {
        if (_proxyServer is null) return;
        Metrics = new ProxyMetrics(_proxyServer.ClientConnectionCount, _proxyServer.ServerConnectionCount);
        MetricsChanged?.Invoke(this, Metrics);
    }

    private sealed class CaptureState(CapturedSession session, Stopwatch stopwatch)
    {
        public CapturedSession Session { get; set; } = session;
        public Stopwatch Stopwatch { get; } = stopwatch;
    }
    private sealed record BodyCaptureResult(
        string Text,
        long? ByteCount,
        byte[]? ImageBytes = null,
        string? ContentType = null);

    private static ProxyServer CreateProxyServer()
    {
        var server = new ProxyServer(
            RootCertificateName,
            "SignInToSniff",
            userTrustRootCertificate: false,
            machineTrustRootCertificate: false,
            trustRootCertificateAsAdmin: false);
        var certificateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SignInToSniff");
        Directory.CreateDirectory(certificateDirectory);
        server.CertificateManager.PfxFilePath = Path.Combine(certificateDirectory, "rootCert.pfx");
        server.CertificateManager.RootCertificate = server.CertificateManager.LoadRootCertificate();
        server.Logging.EnableConsole = false;
        server.Logging.EnableFile = true;
        server.Logging.FilePath = Path.Combine(certificateDirectory, "logs", "proxy.log");
        server.Logging.MinimumLevel = LogLevel.Warning;
        return server;
    }

    private static CertificateStatus GetCertificateStatus(ProxyServer server)
    {
        var manager = server.CertificateManager;
        if (manager.RootCertificate is null) return new CertificateStatus(false, false, false);
        var machineTrusted = false;
        var userTrusted = false;
        try { machineTrusted = manager.IsRootCertificateMachineTrusted(); } catch { }
        try { userTrusted = manager.IsRootCertificateUserTrusted(); } catch { }
        return new CertificateStatus(true, userTrusted, machineTrusted);
    }

    private static bool TrustForCurrentUser(ProxyServer server)
    {
        server.CertificateManager.TrustRootCertificate();
        return true;
    }

    private static bool RemoveForCurrentUser(ProxyServer server)
    {
        server.CertificateManager.RemoveTrustedRootCertificate();
        return true;
    }
}
