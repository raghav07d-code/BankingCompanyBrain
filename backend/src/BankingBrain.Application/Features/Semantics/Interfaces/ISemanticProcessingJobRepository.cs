using BankingBrain.Domain.Entities.Semantics;

namespace BankingBrain.Application.Features.Semantics.Interfaces;

public interface ISemanticProcessingJobRepository
{
    Task<SemanticProcessingJob?> GetActiveAsync(Guid workspaceId, Guid versionId, string identity, string version, CancellationToken cancellationToken);
    Task<bool> TryAddAsync(SemanticProcessingJob job, CancellationToken cancellationToken);
    Task<SemanticProcessingJob?> ClaimNextAsync(DateTime nowUtc, TimeSpan lease, CancellationToken cancellationToken);
    Task<bool> OwnsLeaseAsync(Guid jobId, Guid leaseToken, CancellationToken cancellationToken);
    Task CompleteAsync(Guid jobId, Guid leaseToken, CancellationToken cancellationToken);
    Task<bool> FailAsync(Guid jobId, Guid leaseToken, string error, DateTime retryAtUtc, bool retryable, int maxAttempts, CancellationToken cancellationToken);
}
