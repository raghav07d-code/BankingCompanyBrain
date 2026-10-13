using BankingBrain.Domain.Entities.Semantics;

namespace BankingBrain.Application.Features.Semantics.Interfaces;

public interface IBankingSemanticVocabulary
{
    Task EnsureWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SemanticEntityType>> GetEntityTypesAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SemanticPredicate>> GetPredicatesAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task<bool> IsValidAsync(Guid workspaceId, string entityType, string predicate, string? objectEntityType, CancellationToken cancellationToken);
}
