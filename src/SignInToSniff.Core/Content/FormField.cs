namespace SignInToSniff.Content;

public sealed record FormField(
    string Name,
    string Value,
    string FileName,
    string ContentType,
    long? SizeBytes)
{
    public bool IsFile => !string.IsNullOrEmpty(FileName);
    public string SizeText => SizeBytes is { } size ? $"{size:N0} B" : string.Empty;
}
