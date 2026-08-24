using SignInToSniff.Exclusions;
using Xunit;

namespace SignInToSniff.Tests;

public sealed class ExclusionRuleTests
{
    [Fact]
    public void ExactHost_MatchesOnlyThatHost()
    {
        var rule = new ExclusionRule("users.google.com", ExclusionScope.ExactHost);

        Assert.True(rule.Matches("USERS.GOOGLE.COM"));
        Assert.False(rule.Matches("mail.google.com"));
    }

    [Fact]
    public void DomainAndSubdomains_UsesDomainBoundary()
    {
        var rule = new ExclusionRule("google.com", ExclusionScope.DomainAndSubdomains);

        Assert.True(rule.Matches("google.com"));
        Assert.True(rule.Matches("users.google.com"));
        Assert.False(rule.Matches("notgoogle.com"));
    }
}
