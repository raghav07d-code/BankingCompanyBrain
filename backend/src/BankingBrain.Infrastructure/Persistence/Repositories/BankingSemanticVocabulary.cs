using BankingBrain.Application.Features.Semantics.Interfaces;
using BankingBrain.Domain.Entities.Semantics;
using BankingBrain.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace BankingBrain.Infrastructure.Persistence.Repositories;

public sealed class BankingSemanticVocabulary : IBankingSemanticVocabulary
{
    private static readonly (string Code, string Display)[] Types =
    {
        ("INSTITUTION", "Institution"), ("ORGANIZATION", "Organization/Company"), ("PERSON", "Person"), ("ACCOUNT", "Account"), ("LOAN_FACILITY", "Loan/Facility"), ("SECURITY", "Security"), ("PORTFOLIO", "Portfolio"), ("FUND", "Fund"), ("TRANSACTION", "Transaction"), ("FINANCIAL_METRIC", "Financial Metric"), ("MENTION", "Unresolved Mention")
    };

    private static readonly (string Code, string Display, bool EntityObject, string[] Subjects, string[] Objects)[] Predicates =
    {
        ("HAS_EXPOSURE", "Has exposure", false, new[]{ "INSTITUTION", "ORGANIZATION", "PORTFOLIO", "FUND" }, Array.Empty<string>()),
        ("HAS_RATING", "Has rating", false, new[]{ "INSTITUTION", "ORGANIZATION", "SECURITY" }, Array.Empty<string>()),
        ("OWNS", "Owns", true, new[]{ "INSTITUTION", "ORGANIZATION", "PERSON", "FUND", "PORTFOLIO" }, new[]{ "ACCOUNT", "LOAN_FACILITY", "SECURITY", "PORTFOLIO", "FUND", "ORGANIZATION", "INSTITUTION" }),
        ("LENDS_TO", "Lends to", true, new[]{ "INSTITUTION", "ORGANIZATION" }, new[]{ "ORGANIZATION", "PERSON", "FUND" }),
        ("BORROWS_FROM", "Borrows from", true, new[]{ "ORGANIZATION", "PERSON", "FUND" }, new[]{ "INSTITUTION", "ORGANIZATION" }),
        ("HAS_BALANCE", "Has balance", false, new[]{ "ACCOUNT", "PORTFOLIO", "FUND" }, Array.Empty<string>()),
        ("HAS_REVENUE", "Has revenue", false, new[]{ "ORGANIZATION", "INSTITUTION" }, Array.Empty<string>()),
        ("HAS_ASSET", "Has asset", true, new[]{ "ORGANIZATION", "INSTITUTION", "PERSON", "FUND" }, new[]{ "SECURITY", "ACCOUNT", "PROPERTY", "LOAN_FACILITY" }),
        ("HAS_LIABILITY", "Has liability", true, new[]{ "ORGANIZATION", "INSTITUTION", "PERSON" }, new[]{ "LOAN_FACILITY", "ACCOUNT" }),
        ("HAS_COVENANT", "Has covenant", false, new[]{ "LOAN_FACILITY" }, Array.Empty<string>()),
        ("HAS_RISK_METRIC", "Has risk metric", false, new[]{ "INSTITUTION", "ORGANIZATION", "PORTFOLIO", "FUND" }, Array.Empty<string>()),
        ("HAS_RELATIONSHIP", "Has relationship", true, new[]{ "INSTITUTION", "ORGANIZATION", "PERSON" }, new[]{ "INSTITUTION", "ORGANIZATION", "PERSON" })
    };

    private readonly ApplicationDbContext _db;
    public BankingSemanticVocabulary(ApplicationDbContext db) => _db = db;

    public async Task EnsureWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var existingTypes = await _db.SemanticEntityTypes.Where(item => item.WorkspaceId == workspaceId).Select(item => item.Code).ToListAsync(cancellationToken);
        foreach (var item in Types.Where(item => !existingTypes.Contains(item.Code))) _db.SemanticEntityTypes.Add(SemanticEntityType.Create(workspaceId, item.Code, item.Display));
        var existingPredicates = await _db.SemanticPredicates.Where(item => item.WorkspaceId == workspaceId).Select(item => item.Code).ToListAsync(cancellationToken);
        foreach (var item in Predicates.Where(item => !existingPredicates.Contains(item.Code))) _db.SemanticPredicates.Add(SemanticPredicate.Create(workspaceId, item.Code, item.Display, item.EntityObject, item.Subjects, item.Objects));
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SemanticEntityType>> GetEntityTypesAsync(Guid workspaceId, CancellationToken cancellationToken) { await EnsureWorkspaceAsync(workspaceId, cancellationToken); return await _db.SemanticEntityTypes.AsNoTracking().Where(item => item.WorkspaceId == workspaceId).OrderBy(item => item.Code).ToListAsync(cancellationToken); }
    public async Task<IReadOnlyList<SemanticPredicate>> GetPredicatesAsync(Guid workspaceId, CancellationToken cancellationToken) { await EnsureWorkspaceAsync(workspaceId, cancellationToken); return await _db.SemanticPredicates.AsNoTracking().Where(item => item.WorkspaceId == workspaceId).OrderBy(item => item.Code).ToListAsync(cancellationToken); }
    public async Task<bool> IsValidAsync(Guid workspaceId, string entityType, string predicate, string? objectEntityType, CancellationToken cancellationToken)
    {
        await EnsureWorkspaceAsync(workspaceId, cancellationToken);
        var item = await _db.SemanticPredicates.FirstOrDefaultAsync(value => value.WorkspaceId == workspaceId && value.Code == predicate.Trim().ToUpper(), cancellationToken);
        return item is not null && item.AllowsSubject(entityType) && item.AllowsObject(objectEntityType);
    }
}
