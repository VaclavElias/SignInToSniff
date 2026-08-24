using System.Text.Json;
using SignInToSniff.Exclusions;

namespace SignInToSniff.Persistence;

public sealed class JsonExclusionStore : IExclusionStore
{
    private readonly string _filePath;
    private readonly IReadOnlyList<ExclusionRule> _defaults;

    public JsonExclusionStore(string fileName = "exclusions.json", IReadOnlyList<ExclusionRule>? defaults = null)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SignInToSniff");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, fileName);
        _defaults = defaults ?? [];
    }

    public IReadOnlyList<ExclusionRule> Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return _defaults;
            return JsonSerializer.Deserialize<List<ExclusionRule>>(File.ReadAllText(_filePath)) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    public async Task SaveAsync(IReadOnlyCollection<ExclusionRule> rules, CancellationToken cancellationToken = default)
    {
        var temporaryPath = _filePath + ".tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, rules, cancellationToken: cancellationToken);
        }
        File.Move(temporaryPath, _filePath, overwrite: true);
    }
}
