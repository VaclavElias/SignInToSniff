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
        var completedTransfer = new TaskCompletionSource<ProxyCaptureUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.CaptureReceived += (_, update) =>
        {
            if (update.Kind == CaptureUpdateKind.Added) added.TrySetResult(update);
            if (update.Kind == CaptureUpdateKind.Updated) updated.TrySetResult(update);
            if (update.Kind == CaptureUpdateKind.Updated && update.Session.SentBytes > 0) completedTransfer.TrySetResult(update);
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
        var transferUpdate = await completedTransfer.Task.WaitAsync(timeout.Token);
        await originTask;

        Assert.Equal("GET", requestUpdate.Session.Method);
        Assert.Equal("127.0.0.1", requestUpdate.Session.Host);
        Assert.Contains("/metadata-test", requestUpdate.Session.Url, StringComparison.Ordinal);
        Assert.Equal(200, responseUpdate.Session.StatusCode);
        Assert.Contains("X-SignInToSniff-Test", responseUpdate.Session.ResponseHeaders, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("metadata captured", responseUpdate.Session.ResponseBody);
        Assert.Equal("17 B", responseUpdate.Session.SizeText);
        Assert.NotNull(responseUpdate.Session.DurationMilliseconds);
        Assert.Equal("HTTP/1.1", responseUpdate.Session.Protocol);
        Assert.True(transferUpdate.Session.ReceivedBytes > 0);
        Assert.True(transferUpdate.Session.SentBytes > 0);
        Assert.Null(transferUpdate.Session.ProxyError);
    }

    [Fact]
    public async Task ExplicitHttpProxy_CapturesAndFormatsRequestBody()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var origin = new TcpListener(IPAddress.Loopback, 0);
        origin.Start();
        var originPort = ((IPEndPoint)origin.LocalEndpoint).Port;
        var originTask = ServePostResponseAsync(origin, timeout.Token);

        await using var engine = new TitaniumProxyEngine();
        var added = new TaskCompletionSource<ProxyCaptureUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.CaptureReceived += (_, update) =>
        {
            if (update.Kind == CaptureUpdateKind.Added) added.TrySetResult(update);
        };
        await engine.StartAsync(timeout.Token);

        using var handler = new HttpClientHandler { Proxy = new WebProxy($"http://{engine.Endpoint}"), UseProxy = true };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        using var content = new StringContent("{\"hello\":\"world\"}", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync($"http://127.0.0.1:{originPort}/post-test", content, timeout.Token);
        var requestUpdate = await added.Task.WaitAsync(timeout.Token);
        await originTask;

        Assert.Equal("POST", requestUpdate.Session.Method);
        Assert.Contains("\"hello\": \"world\"", requestUpdate.Session.RequestBody);
    }

    [Fact]
    public async Task ExplicitHttpProxy_DecodesCompressedResponseBodyForDisplay()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var origin = new TcpListener(IPAddress.Loopback, 0);
        origin.Start();
        var originPort = ((IPEndPoint)origin.LocalEndpoint).Port;
        var originTask = ServeGzipResponseAsync(origin, timeout.Token);

        await using var engine = new TitaniumProxyEngine();
        var updated = new TaskCompletionSource<ProxyCaptureUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.CaptureReceived += (_, update) =>
        {
            if (update.Kind == CaptureUpdateKind.Updated) updated.TrySetResult(update);
        };
        await engine.StartAsync(timeout.Token);

        using var handler = new HttpClientHandler { Proxy = new WebProxy($"http://{engine.Endpoint}"), UseProxy = true };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        using var response = await client.GetAsync($"http://127.0.0.1:{originPort}/gzip-test", timeout.Token);
        var responseUpdate = await updated.Task.WaitAsync(timeout.Token);
        await originTask;

        Assert.Equal("compressed over proxy", responseUpdate.Session.ResponseBody);
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
                $"HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {body.Length}\r\nX-SignInToSniff-Test: yes\r\nConnection: close\r\n\r\n{body}");
            await stream.WriteAsync(response, cancellationToken);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task ServePostResponseAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var contentLength = 0;
            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(cancellationToken)))
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    contentLength = int.Parse(line.Split(':', 2)[1].Trim());
                }
            }
            var body = new char[contentLength];
            await reader.ReadBlockAsync(body, cancellationToken);

            var response = Encoding.ASCII.GetBytes("HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response, cancellationToken);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task ServeGzipResponseAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken))) { }

            using var compressed = new MemoryStream();
            await using (var gzip = new System.IO.Compression.GZipStream(compressed, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
            {
                await gzip.WriteAsync(Encoding.UTF8.GetBytes("compressed over proxy"), cancellationToken);
            }
            var bytes = compressed.ToArray();
            var headers = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Encoding: gzip\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headers, cancellationToken);
            await stream.WriteAsync(bytes, cancellationToken);
        }
        finally
        {
            listener.Stop();
        }
    }
}
