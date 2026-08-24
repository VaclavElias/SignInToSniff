using System.Net;
using System.Net.Sockets;
using System.Text;
using SignInToSniff.Proxy;
using Xunit;

namespace SignInToSniff.Tests;

public sealed class TitaniumProxyEngineTests
{
    [Fact]
    public async Task ExplicitHttpProxy_CapturesRequestAndResponseMetadata()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var origin = new TcpListener(IPAddress.Loopback, 0);
        origin.Start();
        var originPort = ((IPEndPoint)origin.LocalEndpoint).Port;
        var originTask = ServeSingleResponseAsync(origin, timeout.Token);

        await using var engine = new TitaniumProxyEngine();
        var added = new TaskCompletionSource<ProxyCaptureUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        var updated = new TaskCompletionSource<ProxyCaptureUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.CaptureReceived += (_, update) =>
        {
            if (update.Kind == CaptureUpdateKind.Added) added.TrySetResult(update);
            if (update.Kind == CaptureUpdateKind.Updated) updated.TrySetResult(update);
        };

        await engine.StartAsync(timeout.Token);
        Assert.Equal(ProxyState.Running, engine.State);

        using var handler = new HttpClientHandler
        {
            Proxy = new WebProxy($"http://{engine.Endpoint}"),
            UseProxy = true
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        using var response = await client.GetAsync($"http://127.0.0.1:{originPort}/metadata-test", timeout.Token);

        var requestUpdate = await added.Task.WaitAsync(timeout.Token);
        var responseUpdate = await updated.Task.WaitAsync(timeout.Token);
        await originTask;

        Assert.Equal("GET", requestUpdate.Session.Method);
        Assert.Equal("127.0.0.1", requestUpdate.Session.Host);
        Assert.Contains("/metadata-test", requestUpdate.Session.Url, StringComparison.Ordinal);
        Assert.Equal(200, responseUpdate.Session.StatusCode);
        Assert.Contains("X-SignInToSniff-Test", responseUpdate.Session.ResponseHeaders, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(responseUpdate.Session.DurationMilliseconds);
    }

    private static async Task ServeSingleResponseAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken)))
            {
            }

            const string body = "metadata captured";
            var response = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nX-SignInToSniff-Test: yes\r\nConnection: close\r\n\r\n{body}");
            await stream.WriteAsync(response, cancellationToken);
        }
        finally
        {
            listener.Stop();
        }
    }
}
