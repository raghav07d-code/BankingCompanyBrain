using BankingBrain.Domain.Entities.Semantics;
using Xunit;

namespace BankingBrain.IntegrationTests;

public sealed class BankingSemanticDomainTests
{
    [Fact]
    public void Claim_preserves_temporal_and_financial_context()
    {
        var asOf = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        var claim = SemanticClaim.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, "Acme Bank", "HAS_BALANCE", "USD 1250000", .9m, asOfUtc: asOf, valueKind: "money", numericValue: 1250000m, currencyCode: "USD", unit: "account-balance", periodStartUtc: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), periodEndUtc: asOf);

        Assert.Equal(asOf, claim.AsOfUtc);
        Assert.Equal(1250000m, claim.NumericValue);
        Assert.Equal("USD", claim.CurrencyCode);
        Assert.Equal("money", claim.ValueKind);
        Assert.NotNull(claim.PeriodStartUtc);
    }

    [Fact]
    public void Different_values_and_periods_remain_distinct_historical_claims()
    {
        var workspace = Guid.NewGuid(); var version = Guid.NewGuid(); var run = Guid.NewGuid();
        var first = SemanticClaim.Create(workspace, version, run, Guid.NewGuid(), null, null, "Acme", "HAS_RATING", "BBB", .8m, asOfUtc: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var second = SemanticClaim.Create(workspace, version, run, Guid.NewGuid(), null, null, "Acme", "HAS_RATING", "BBB+", .8m, asOfUtc: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.NotEqual(first.IdentityHash, second.IdentityHash);
        Assert.NotEqual(first.ObjectValue, second.ObjectValue);
    }

    [Fact]
    public void Banking_predicate_catalog_restricts_subject_types()
    {
        var predicate = SemanticPredicate.Create(Guid.NewGuid(), "HAS_RATING", "Has rating", false, new[] { "INSTITUTION", "ORGANIZATION" });
        Assert.True(predicate.AllowsSubject("institution"));
        Assert.False(predicate.AllowsSubject("transaction"));
    }
}
