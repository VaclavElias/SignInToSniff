using System.IO.Compression;
using System.Text;
using SignInToSniff.Proxy;
using Xunit;

namespace SignInToSniff.Tests;

public sealed class BodyCaptureFormatterTests
{
    [Fact]
    public void Format_PrettyPrintsJson()
    {
        var result = BodyCaptureFormatter.Format(Encoding.UTF8.GetBytes("{\"answer\":42}"), "application/json", null);

        Assert.Contains(Environment.NewLine, result);
        Assert.Contains("\"answer\": 42", result);
    }

    [Theory]
    [InlineData("gzip")]
    [InlineData("br")]
    [InlineData("deflate")]
    public void Format_DecompressesText(string encoding)
    {
        var compressed = Compress(Encoding.UTF8.GetBytes("compressed response"), encoding);

        Assert.Equal("compressed response", BodyCaptureFormatter.Format(compressed, "text/plain", encoding));
    }

    [Fact]
    public void Format_OmitsUnknownBinaryData()
    {
        var result = BodyCaptureFormatter.Format([0, 1, 2, 3, 4], null, null);

        Assert.Contains("Binary body omitted", result);
    }

    [Fact]
    public void Format_CapsDecompressedOutput()
    {
        var source = Enumerable.Repeat((byte)'a', BodyCaptureFormatter.MaxCapturedBodyBytes + 100).ToArray();
        var compressed = Compress(source, "gzip");

        var result = BodyCaptureFormatter.Format(compressed, "text/plain", "gzip");

        Assert.EndsWith("[Body truncated at the 1 MiB capture limit.]", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldRead_RejectsKnownOversizeBodyBeforeBuffering()
    {
        var shouldRead = BodyCaptureFormatter.ShouldRead(
            "text/plain",
            BodyCaptureFormatter.MaxCapturedBodyBytes + 1L,
            out var reason);

        Assert.False(shouldRead);
        Assert.Contains("exceeds", reason);
    }

    [Fact]
    public void ShouldRead_RejectsKnownBinaryMedia()
    {
        Assert.False(BodyCaptureFormatter.ShouldRead("image/png", 100, out var reason));
        Assert.Contains("Binary body omitted", reason);
    }

    [Fact]
    public void ShouldRead_RejectsChromeExtensionPackage()
    {
        Assert.False(BodyCaptureFormatter.ShouldRead("application/x-chrome-extension", 248_531, out var reason));
        Assert.Contains("Binary body omitted", reason);
    }

    private static byte[] Compress(byte[] source, string encoding)
    {
        using var output = new MemoryStream();
        using (Stream compressor = encoding switch
        {
            "gzip" => new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true),
            "br" => new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true),
            "deflate" => new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true),
            _ => throw new ArgumentOutOfRangeException(nameof(encoding))
        })
        {
            compressor.Write(source);
        }
        return output.ToArray();
    }
}
