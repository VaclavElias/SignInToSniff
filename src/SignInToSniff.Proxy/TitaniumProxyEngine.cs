using System.Diagnostics;
using System.Net;
using System.Threading.Channels;
using SignInToSniff.Models;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Models;

namespace SignInToSniff.Proxy;

public sealed class TitaniumProxyEngine : IProxyEngine
{
    private const int ListenPort = 8000;
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

    public TitaniumProxyEngine()
    {
        _pumpTask = PumpUpdatesAsync(_pumpCancellation.Token);
    }

    public string Endpoint => $"127.0.0.1:{ListenPort}";

    public ProxyState State { get; private set; } = ProxyState.Stopped;

    public event EventHandler<ProxyCaptureUpdate>? CaptureReceived;

    public event EventHandler<ProxyStateChanged>? StateChanged;

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
                var proxyServer = new ProxyServer(
                    userTrustRootCertificate: false,
                    machineTrustRootCertificate: false,
                    trustRootCertificateAsAdmin: false);
                var endPoint = new ExplicitProxyEndPoint(IPAddress.Loopback, ListenPort, decryptSsl: false);

                proxyServer.BeforeRequest += OnBeforeRequestAsync;
                proxyServer.BeforeResponse += OnBeforeResponseAsync;
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

    private async Task OnBeforeRequestAsync(object sender, SessionEventArgs eventArgs)
    {
        var request = eventArgs.HttpClient.Request;
        var uri = request.RequestUri;
        var headers = request.Headers.Select(header => header.ToString()).ToArray();
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
            FormatHeaders(headers),
            requestBody,
            "Waiting for response…",
            "Waiting for response body…",
            null);

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
        var responseBody = await CaptureBodyAsync(
            response.HasBody,
            response.ContentLength,
            BodyCaptureFormatter.FindHeader(headers, "Content-Type"),
            eventArgs.GetResponseBody,
            "response").ConfigureAwait(false);
        var completed = state.Session with
        {
            StatusCode = response.StatusCode,
            ResponseHeaders = FormatHeaders(headers),
            ResponseBody = responseBody,
            ResponseSizeBytes = response.ContentLength >= 0 ? response.ContentLength : null,
            DurationMilliseconds = state.Stopwatch.ElapsedMilliseconds
        };

        _updates.Writer.TryWrite(new ProxyCaptureUpdate(CaptureUpdateKind.Updated, completed));
    }

    private static async Task<string> CaptureBodyAsync(
        bool hasBody,
        long contentLength,
        string? contentType,
        Func<CancellationToken, Task<byte[]>> readBody,
        string direction)
    {
        if (!hasBody) return $"No {direction} body";
        if (!BodyCaptureFormatter.ShouldRead(contentType, contentLength, out var omissionReason)) return omissionReason!;

        try
        {
            var body = await readBody(CancellationToken.None).ConfigureAwait(false);
            // Titanium exposes a decoded inspection buffer even though the original
            // Content-Encoding header remains present on the proxied response.
            return BodyCaptureFormatter.Format(body, contentType, contentEncoding: null);
        }
        catch (Exception exception)
        {
            return $"[{char.ToUpperInvariant(direction[0])}{direction[1..]} body capture failed: {exception.Message}]";
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

        try
        {
            _proxyServer.Stop();
        }
        finally
        {
            _proxyServer.Dispose();
            _proxyServer = null;
            _endPoint = null;
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

    private sealed record CaptureState(CapturedSession Session, Stopwatch Stopwatch);
}
