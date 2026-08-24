namespace SignInToSniff.Exclusions;

public enum ExclusionScope
{
    ExactHost,
    DomainAndSubdomains
}

public sealed record ExclusionRule(string Domain, ExclusionScope Scope)
{
    public string DisplayText => Scope == ExclusionScope.ExactHost ? Domain : $"*.{Domain}";

    public bool Matches(string host) => Scope == ExclusionScope.ExactHost
        ? host.Equals(Domain, StringComparison.OrdinalIgnoreCase)
        : host.Equals(Domain, StringComparison.OrdinalIgnoreCase) ||
          host.EndsWith($".{Domain}", StringComparison.OrdinalIgnoreCase);
}

public interface IExclusionStore
{
    IReadOnlyList<ExclusionRule> Load();
    Task SaveAsync(IReadOnlyCollection<ExclusionRule> rules, CancellationToken cancellationToken = default);
}
