using System.Collections.ObjectModel;

namespace SignInToSniff.Exclusions;

public interface IHostRuleSet
{
    ReadOnlyObservableCollection<ExclusionRule> Rules { get; }
    bool Matches(string host);
    Task AddAsync(string domain, ExclusionScope scope);
    Task RemoveAsync(ExclusionRule rule);
}

public sealed class HostRuleSet : IHostRuleSet
{
    private readonly IExclusionStore _store;
    private readonly ObservableCollection<ExclusionRule> _rules;

    public HostRuleSet(IExclusionStore store)
    {
        _store = store;
        _rules = new ObservableCollection<ExclusionRule>(store.Load().Distinct());
        Rules = new ReadOnlyObservableCollection<ExclusionRule>(_rules);
    }

    public ReadOnlyObservableCollection<ExclusionRule> Rules { get; }

    public bool Matches(string host)
    {
        lock (_rules) return _rules.Any(rule => rule.Matches(host));
    }

    public async Task AddAsync(string domain, ExclusionScope scope)
    {
        var normalized = NormalizeDomain(domain);
        if (normalized.Length == 0) return;
        var rule = new ExclusionRule(normalized, scope);
        lock (_rules)
        {
            if (scope == ExclusionScope.ExactHost && _rules.Any(existing =>
                    existing.Scope == ExclusionScope.DomainAndSubdomains && existing.Matches(normalized))) return;
            if (scope == ExclusionScope.DomainAndSubdomains)
            {
                for (var index = _rules.Count - 1; index >= 0; index--)
                {
                    if (rule.Matches(_rules[index].Domain)) _rules.RemoveAt(index);
                }
            }
            if (!_rules.Contains(rule)) _rules.Add(rule);
        }
        await _store.SaveAsync(_rules);
    }

    public async Task RemoveAsync(ExclusionRule rule)
    {
        lock (_rules) _rules.Remove(rule);
        await _store.SaveAsync(_rules);
    }

    private static string NormalizeDomain(string domain)
    {
        var value = domain.Trim().TrimEnd('.').ToLowerInvariant();
        if (value.StartsWith("*.", StringComparison.Ordinal)) value = value[2..];
        return Uri.CheckHostName(value) == UriHostNameType.Unknown ? string.Empty : value;
    }
}
