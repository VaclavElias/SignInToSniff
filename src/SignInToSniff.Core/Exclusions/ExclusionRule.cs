using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SignInToSniff.Exclusions;

public enum ExclusionScope
{
    ExactHost,
    DomainAndSubdomains
}

public sealed class ExclusionRule : INotifyPropertyChanged, IEquatable<ExclusionRule>
{
    private int _matchCount;

    public ExclusionRule(string domain, ExclusionScope scope)
    {
        Domain = domain;
        Scope = scope;
    }

    public string Domain { get; }
    public ExclusionScope Scope { get; }

    public string DisplayText => Scope == ExclusionScope.ExactHost ? Domain : $"*.{Domain}";

    [JsonIgnore]
    public int MatchCount
    {
        get => _matchCount;
        private set
        {
            if (_matchCount == value) return;
            _matchCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MatchCountText));
        }
    }

    [JsonIgnore]
    public string MatchCountText => MatchCount == 1 ? "1 request hidden" : $"{MatchCount:N0} requests hidden";

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool Matches(string host) => Scope == ExclusionScope.ExactHost
        ? host.Equals(Domain, StringComparison.OrdinalIgnoreCase)
        : host.Equals(Domain, StringComparison.OrdinalIgnoreCase) ||
          host.EndsWith($".{Domain}", StringComparison.OrdinalIgnoreCase);

    public void SetMatchCount(int value) => MatchCount = value;

    public bool Equals(ExclusionRule? other) => other is not null && Scope == other.Scope &&
        Domain.Equals(other.Domain, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as ExclusionRule);

    public override int GetHashCode() => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(Domain), Scope);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public interface IExclusionStore
{
    IReadOnlyList<ExclusionRule> Load();
    Task SaveAsync(IReadOnlyCollection<ExclusionRule> rules, CancellationToken cancellationToken = default);
}
