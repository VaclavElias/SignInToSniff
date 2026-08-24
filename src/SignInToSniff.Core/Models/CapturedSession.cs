namespace SignInToSniff.Models;

public sealed record CapturedSession(
    Guid Id,
    DateTimeOffset StartedAt,
    string Method,
    int? StatusCode,
    string Host,
    string Url,
    string RequestHeaders,
    string RequestBody,
    string ResponseHeaders,
    string ResponseBody,
    long? DurationMilliseconds)
{
    public long? ResponseSizeBytes { get; init; }
    public string Protocol { get; init; } = "HTTP/?";
    public long ReceivedBytes { get; init; }
    public long SentBytes { get; init; }
    public string? ProxyError { get; init; }
    public byte[]? ResponseImageBytes { get; init; }
    public byte[]? RequestFormBytes { get; init; }
    public byte[]? ResponseFormBytes { get; init; }
    public string? RequestContentType { get; init; }
    public string? ResponseContentType { get; init; }

    public string StatusText => StatusCode?.ToString() ?? "…";

    public string DurationText => DurationMilliseconds is { } duration ? $"{duration} ms" : "Pending";

    public string SizeText => ResponseSizeBytes switch
    {
        null => "—",
        < 1024 => $"{ResponseSizeBytes} B",
        < 1024 * 1024 => $"{ResponseSizeBytes / 1024d:0.#} KB",
        _ => $"{ResponseSizeBytes / (1024d * 1024d):0.#} MB"
    };

    public string TransferText => $"Captured ↑ {ReceivedBytes:N0} B  ↓ {SentBytes:N0} B";

    public bool HasProxyError => !string.IsNullOrWhiteSpace(ProxyError);

    public bool HasImagePreview => ResponseImageBytes is { Length: > 0 };

    public bool HasRequestJson => IsJsonContentType(RequestContentType);

    public bool HasResponseJson => !HasImagePreview && IsJsonContentType(ResponseContentType);

    public bool HasRequestForm => IsFormContentType(RequestContentType);

    public bool HasResponseForm => !HasImagePreview && IsFormContentType(ResponseContentType);

    public bool HasRequestPlainBody => !HasRequestJson && !HasRequestForm;

    public bool HasResponsePlainBody => !HasImagePreview && !HasResponseJson && !HasResponseForm;

    public string StartedAtText => StartedAt.ToLocalTime().ToString("HH:mm:ss");

    public string SiteDomain
    {
        get
        {
            if (Uri.CheckHostName(Host) != UriHostNameType.Dns) return Host;
            var labels = Host.TrimEnd('.').Split('.');
            if (labels.Length <= 2) return Host;
            var commonSecondLevel = labels[^1].Length == 2 && labels[^2] is "ac" or "co" or "com" or "gov" or "net" or "org";
            return string.Join('.', commonSecondLevel && labels.Length >= 3 ? labels[^3..] : labels[^2..]);
        }
    }

    private static bool IsJsonContentType(string? contentType)
    {
        var mediaType = (contentType ?? string.Empty).Split(';', 2)[0].Trim();
        return mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
               mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFormContentType(string? contentType)
    {
        var mediaType = (contentType ?? string.Empty).Split(';', 2)[0].Trim();
        return mediaType.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) ||
               mediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase);
    }
}
