using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace SignInToSniff.Proxy;

public static class BodyCaptureFormatter
{
    public const int MaxCapturedBodyBytes = 1024 * 1024;
    private const string TruncationNotice = "\n\n[Body truncated at the 1 MiB capture limit.]";

    public static bool ShouldRead(string? contentType, long contentLength, out string? omissionReason)
    {
        omissionReason = null;
        if (contentLength > MaxCapturedBodyBytes)
        {
            omissionReason = $"[Body omitted: declared size {contentLength:N0} bytes exceeds the 1 MiB capture limit.]";
            return false;
        }

        var mediaType = GetMediaType(contentType);
        if (mediaType == "text/event-stream")
        {
            omissionReason = "[Streaming event body omitted.]";
            return false;
        }

        if (mediaType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase) || IsKnownBinary(mediaType) ||
            (mediaType.StartsWith("application/", StringComparison.OrdinalIgnoreCase) && !IsTextual(mediaType)))
        {
            omissionReason = $"[Binary body omitted{(mediaType.Length == 0 ? "." : $": {mediaType}.")}]";
            return false;
        }

        return true;
    }

    public static string Format(byte[] body, string? contentType, string? contentEncoding)
    {
        if (body.Length == 0) return "No body";

        byte[] decoded;
        bool truncated;
        try
        {
            (decoded, truncated) = DecodeContent(body, contentEncoding);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or NotSupportedException)
        {
            return $"[Body could not be decoded: {exception.Message}]";
        }

        var mediaType = GetMediaType(contentType);
        if (IsKnownBinary(mediaType) || (mediaType.Length == 0 && !LooksLikeText(decoded)))
        {
            return $"[Binary body omitted: {decoded.Length:N0} bytes.]";
        }

        var text = DecodeText(decoded, contentType);
        if (IsJson(mediaType)) text = PrettyPrintJson(text);
        return truncated ? text + TruncationNotice : text;
    }

    public static string? FindHeader(IEnumerable<string> headers, string name)
    {
        foreach (var header in headers)
        {
            var separator = header.IndexOf(':');
            if (separator > 0 && header.AsSpan(0, separator).Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return header[(separator + 1)..].Trim();
            }
        }

        return null;
    }

    private static (byte[] Bytes, bool Truncated) DecodeContent(byte[] source, string? contentEncoding)
    {
        var encodings = (contentEncoding ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var current = source;
        var truncated = false;
        for (var index = encodings.Length - 1; index >= 0; index--)
        {
            var encoding = encodings[index];
            if (encoding.Equals("identity", StringComparison.OrdinalIgnoreCase)) continue;
            using var input = new MemoryStream(current, writable: false);
            using Stream decoder = encoding.ToLowerInvariant() switch
            {
                "gzip" or "x-gzip" => new GZipStream(input, CompressionMode.Decompress),
                "br" => new BrotliStream(input, CompressionMode.Decompress),
                "deflate" => new ZLibStream(input, CompressionMode.Decompress),
                _ => throw new NotSupportedException($"Content-Encoding '{encoding}' is not supported")
            };
            (current, var layerTruncated) = ReadBounded(decoder);
            truncated |= layerTruncated;
        }

        if (encodings.Length == 0 && current.Length > MaxCapturedBodyBytes)
        {
            current = current[..MaxCapturedBodyBytes];
            truncated = true;
        }
        return (current, truncated);
    }

    private static (byte[] Bytes, bool Truncated) ReadBounded(Stream stream)
    {
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (output.Length <= MaxCapturedBodyBytes)
        {
            var remaining = MaxCapturedBodyBytes + 1 - (int)output.Length;
            var read = stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
            if (read == 0) break;
            output.Write(buffer, 0, read);
        }
        var bytes = output.ToArray();
        return bytes.Length > MaxCapturedBodyBytes ? (bytes[..MaxCapturedBodyBytes], true) : (bytes, false);
    }

    private static string DecodeText(byte[] bytes, string? contentType)
    {
        var charset = contentType?.Split(';', StringSplitOptions.TrimEntries)
            .FirstOrDefault(part => part.StartsWith("charset=", StringComparison.OrdinalIgnoreCase))?
            .Split('=', 2)[1].Trim(' ', '"', '\'');
        try
        {
            return charset is null ? Encoding.UTF8.GetString(bytes) : Encoding.GetEncoding(charset).GetString(bytes);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }

    private static string PrettyPrintJson(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException) { return text; }
    }

    private static bool LooksLikeText(byte[] bytes)
    {
        if (bytes.AsSpan().IndexOf((byte)0) >= 0) return false;
        var controls = bytes.Count(value => value < 0x09 || value is > 0x0D and < 0x20);
        return controls <= Math.Max(1, bytes.Length / 100);
    }

    private static string GetMediaType(string? contentType) => (contentType ?? string.Empty).Split(';', 2)[0].Trim().ToLowerInvariant();
    private static bool IsJson(string mediaType) => mediaType == "application/json" || mediaType.EndsWith("+json", StringComparison.Ordinal);
    private static bool IsTextual(string mediaType) =>
        mediaType.StartsWith("text/", StringComparison.Ordinal) || IsJson(mediaType) ||
        mediaType.EndsWith("+xml", StringComparison.Ordinal) ||
        mediaType is "application/xml" or "application/javascript" or "application/ecmascript" or
            "application/x-javascript" or "application/x-www-form-urlencoded" or "application/graphql" or
            "application/sql" or "application/yaml" or "application/x-yaml";
    private static bool IsKnownBinary(string mediaType) =>
        mediaType.StartsWith("image/", StringComparison.Ordinal) || mediaType.StartsWith("audio/", StringComparison.Ordinal) ||
        mediaType.StartsWith("video/", StringComparison.Ordinal) || mediaType.StartsWith("font/", StringComparison.Ordinal) ||
        mediaType is "application/octet-stream" or "application/pdf" or "application/zip" or "application/gzip" or
            "application/x-chrome-extension" or "application/wasm" or
            "application/x-7z-compressed" or "application/x-rar-compressed";
}
