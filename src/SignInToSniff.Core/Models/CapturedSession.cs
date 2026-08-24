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

    public string StatusText => StatusCode?.ToString() ?? "…";

    public string DurationText => DurationMilliseconds is { } duration ? $"{duration} ms" : "Pending";

    public string SizeText => ResponseSizeBytes switch
    {
        null => "—",
        < 1024 => $"{ResponseSizeBytes} B",
        < 1024 * 1024 => $"{ResponseSizeBytes / 1024d:0.#} KB",
        _ => $"{ResponseSizeBytes / (1024d * 1024d):0.#} MB"
    };

    public string StartedAtText => StartedAt.ToLocalTime().ToString("HH:mm:ss");
}
