using System.Text;
using System.Text.RegularExpressions;

namespace SignInToSniff.Content;

public static partial class FormBodyParser
{
    public const int MaxParts = 500;
    public const int MaxValuePreviewCharacters = 4_096;

    public static IReadOnlyList<FormField> Parse(string body, string? contentType, byte[]? multipartBytes = null)
    {
        var mediaType = GetMediaType(contentType);
        return mediaType switch
        {
            "application/x-www-form-urlencoded" => ParseUrlEncoded(body),
            "multipart/form-data" => ParseMultipart(multipartBytes ?? Encoding.UTF8.GetBytes(body), contentType),
            _ => []
        };
    }

    private static IReadOnlyList<FormField> ParseUrlEncoded(string body)
    {
        var fields = new List<FormField>();
        foreach (var pair in body.Split('&', StringSplitOptions.None).Take(MaxParts))
        {
            var separator = pair.IndexOf('=');
            var name = separator < 0 ? pair : pair[..separator];
            var value = separator < 0 ? string.Empty : pair[(separator + 1)..];
            fields.Add(new FormField(DecodeComponent(name), DecodeComponent(value), string.Empty, string.Empty, null));
        }
        return fields;
    }

    private static IReadOnlyList<FormField> ParseMultipart(byte[] bytes, string? contentType)
    {
        var boundary = GetParameter(contentType ?? string.Empty, "boundary");
        if (string.IsNullOrWhiteSpace(boundary))
        {
            return [new FormField("Parse error", "The multipart boundary is missing.", string.Empty, string.Empty, null)];
        }

        // Latin-1 preserves a one-to-one mapping between characters and bytes while locating boundaries.
        var source = Encoding.Latin1.GetString(bytes);
        var delimiter = "--" + boundary;
        var fields = new List<FormField>();
        foreach (var rawPart in source.Split(delimiter, StringSplitOptions.None).Skip(1))
        {
            if (fields.Count >= MaxParts || rawPart.StartsWith("--", StringComparison.Ordinal)) break;
            var part = rawPart.StartsWith("\r\n", StringComparison.Ordinal) ? rawPart[2..] : rawPart;
            var headerEnd = part.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerEnd < 0) continue;

            var headerText = part[..headerEnd];
            var dataText = part[(headerEnd + 4)..];
            if (dataText.EndsWith("\r\n", StringComparison.Ordinal)) dataText = dataText[..^2];
            var data = Encoding.Latin1.GetBytes(dataText);
            var disposition = FindHeader(headerText, "Content-Disposition") ?? string.Empty;
            var name = GetParameter(disposition, "name") ?? "(unnamed)";
            var fileName = GetParameter(disposition, "filename") ?? string.Empty;
            var partContentType = FindHeader(headerText, "Content-Type") ?? string.Empty;
            var value = fileName.Length > 0 ? "File upload" : DecodePartText(data, partContentType);
            fields.Add(new FormField(name, Truncate(value), fileName, partContentType, fileName.Length > 0 ? data.LongLength : null));
        }
        return fields;
    }

    private static string DecodePartText(byte[] bytes, string contentType)
    {
        var charset = GetParameter(contentType, "charset");
        try
        {
            return charset is null ? Encoding.UTF8.GetString(bytes) : Encoding.GetEncoding(charset).GetString(bytes);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }

    private static string DecodeComponent(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value.Replace('+', ' '));
        }
        catch (UriFormatException)
        {
            return value.Replace('+', ' ');
        }
    }

    private static string Truncate(string value) => value.Length <= MaxValuePreviewCharacters
        ? value
        : value[..MaxValuePreviewCharacters] + "…";

    private static string? FindHeader(string headers, string name)
    {
        foreach (var line in headers.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':');
            if (separator > 0 && line.AsSpan(0, separator).Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return line[(separator + 1)..].Trim();
            }
        }
        return null;
    }

    private static string? GetParameter(string value, string name)
    {
        foreach (Match match in ParameterRegex().Matches(value))
        {
            if (!match.Groups["key"].Value.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            return match.Groups["quoted"].Success
                ? match.Groups["quoted"].Value.Replace("\\\"", "\"")
                : match.Groups["plain"].Value.Trim();
        }
        return null;
    }

    private static string GetMediaType(string? contentType) =>
        (contentType ?? string.Empty).Split(';', 2)[0].Trim().ToLowerInvariant();

    [GeneratedRegex("(?:^|;)\\s*(?<key>[\\w-]+)=(?:\"(?<quoted>(?:\\\\.|[^\"])*)\"|(?<plain>[^;]*))")]
    private static partial Regex ParameterRegex();
}
