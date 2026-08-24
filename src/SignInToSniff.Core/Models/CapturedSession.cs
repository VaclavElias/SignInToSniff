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
    public string StatusText => StatusCode?.ToString() ?? "…";

    public string DurationText => DurationMilliseconds is { } duration ? $"{duration} ms" : "Pending";

    public string StartedAtText => StartedAt.ToLocalTime().ToString("HH:mm:ss");
}
