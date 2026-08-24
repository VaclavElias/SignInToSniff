using SignInToSniff.Exclusions;
using Xunit;

namespace SignInToSniff.Tests;

public sealed class HostRuleSetTests
{
    [Fact]
    public async Task BroadRule_SupersedesExactRuleAndPersists()
    {
        var store = new MemoryStore([new ExclusionRule("api.example.com", ExclusionScope.ExactHost)]);
        var rules = new HostRuleSet(store);

        await rules.AddAsync("example.com", ExclusionScope.DomainAndSubdomains);

        var rule = Assert.Single(rules.Rules);
        Assert.Equal("example.com", rule.Domain);
        Assert.True(rules.Matches("api.example.com"));
        Assert.False(rules.Matches("notexample.com"));
        Assert.Single(store.Saved);
    }

    [Fact]
    public async Task RemovingRuleStopsMatching()
    {
        var store = new MemoryStore([new ExclusionRule("pinned.example", ExclusionScope.DomainAndSubdomains)]);
        var rules = new HostRuleSet(store);

        await rules.RemoveAsync(rules.Rules[0]);

        Assert.False(rules.Matches("api.pinned.example"));
        Assert.Empty(store.Saved);
    }

    private sealed class MemoryStore(IReadOnlyList<ExclusionRule> initial) : IExclusionStore
    {
        public List<ExclusionRule> Saved { get; } = [];
        public IReadOnlyList<ExclusionRule> Load() => initial;
        public Task SaveAsync(IReadOnlyCollection<ExclusionRule> rules, CancellationToken cancellationToken = default)
        {
            Saved.Clear();
            Saved.AddRange(rules);
            return Task.CompletedTask;
        }
    }
}
