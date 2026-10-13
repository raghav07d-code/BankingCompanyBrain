using BankingBrain.Domain.Entities.ProcessingJob;

namespace BankingBrain.Application.Features.Documents.Interfaces;

public interface IProcessingJobRepository
{
    Task<ProcessingJob?> GetActiveAsync(Guid documentVersionId, CancellationToken cancellationToken);
    Task AddAsync(ProcessingJob job, CancellationToken cancellationToken);
    Task<bool> TryAddAsync(ProcessingJob job, CancellationToken cancellationToken);
    Task<bool> SupersedePendingAsync(Guid documentVersionId, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task<ProcessingJob?> ClaimNextAsync(DateTime nowUtc, TimeSpan lease, CancellationToken cancellationToken);
    Task<bool> OwnsLeaseAsync(Guid jobId, Guid leaseToken, CancellationToken cancellationToken);
    Task CompleteAsync(Guid jobId, Guid leaseToken, CancellationToken cancellationToken);
    Task<bool> FailAsync(
        Guid jobId,
        Guid leaseToken,
        string error,
        DateTime retryAtUtc,
        bool retryable,
        int maxAttempts,
        CancellationToken cancellationToken);
}