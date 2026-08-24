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
}
