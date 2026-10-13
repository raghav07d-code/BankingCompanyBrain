using BankingBrain.Application.Features.Documents.Interfaces;
using BankingBrain.Domain.Entities.Document;
using BankingBrain.Domain.Entities.DocumentSection;
using BankingBrain.Domain.Entities.DocumentVersion;
using BankingBrain.Domain.Entities.ProcessingAttempt;
using BankingBrain.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BankingBrain.Infrastructure.Persistence.Repositories;

public sealed class DocumentVersionRepository : IDocumentVersionRepository
{
    private readonly ApplicationDbContext _dbContext;

    public DocumentVersionRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<DocumentVersion?> GetByIdAsync(
        Guid workspaceId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken)
    {
        return _dbContext.DocumentVersions
            .AsNoTracking()
            .Where(version => version.Id == versionId && version.DocumentId == documentId)
            .Join(
                _dbContext.Documents,
                version => version.DocumentId,
                document => document.Id,
                (version, document) => new { version, document })
            .Where(item => item.document.WorkspaceId == workspaceId)
            .Select(item => item.version)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<DocumentVersion?> GetTrackedByIdAsync(
        Guid workspaceId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken)
    {
        return _dbContext.DocumentVersions
            .Where(version => version.Id == versionId && version.DocumentId == documentId)
            .Join(
                _dbContext.Documents,
                version => version.DocumentId,
                document => document.Id,
                (version, document) => new { version, document })
            .Where(item => item.document.WorkspaceId == workspaceId)
            .Select(item => item.version)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<DocumentVersion?> GetByHashAsync(
        Guid documentId,
        string contentHash,
        CancellationToken cancellationToken)
    {
        return _dbContext.DocumentVersions.FirstOrDefaultAsync(
            version => version.DocumentId == documentId &&
                       version.ContentHash == contentHash,
            cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentVersion>> GetByDocumentAsync(
        Guid workspaceId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.DocumentVersions
            .AsNoTracking()
            .Where(version => version.DocumentId == documentId)
            .Join(
                _dbContext.Documents,
                version => version.DocumentId,
                document => document.Id,
                (version, document) => new { version, document })
            .Where(item => item.document.WorkspaceId == workspaceId)
            .OrderByDescending(item => item.version.VersionNumber)
            .Select(item => item.version)
            .ToListAsync(cancellationToken);
    }

    public Task<int> GetNextVersionNumberAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        return _dbContext.DocumentVersions
            .Where(version => version.DocumentId == documentId)
            .Select(version => (int?)version.VersionNumber)
            .MaxAsync(cancellationToken)
            .ContinueWith(task => (task.Result ?? 0) + 1, cancellationToken);
    }

    public Task AddAsync(
        DocumentVersion version,
        CancellationToken cancellationToken)
    {
        return _dbContext.DocumentVersions.AddAsync(version, cancellationToken).AsTask();
    }

    public Task AddSectionAsync(
        DocumentSection section,
        CancellationToken cancellationToken)
    {
        return _dbContext.DocumentSections.AddAsync(section, cancellationToken).AsTask();
    }

    public Task AddAttemptAsync(
        ProcessingAttempt attempt,
        CancellationToken cancellationToken)
    {
        return _dbContext.ProcessingAttempts.AddAsync(attempt, cancellationToken).AsTask();
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception)
            when (IsVersionConflict(exception))
        {
            _dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    public Task<int> GetNextAttemptNumberAsync(
        Guid versionId,
        CancellationToken cancellationToken)
    {
        return _dbContext.ProcessingAttempts
            .Where(attempt => attempt.DocumentVersionId == versionId)
            .Select(attempt => (int?)attempt.AttemptNumber)
            .MaxAsync(cancellationToken)
            .ContinueWith(task => (task.Result ?? 0) + 1, cancellationToken);
    }

    public Task<Document?> GetDocumentAsync(
        Guid workspaceId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        return _dbContext.Documents.FirstOrDefaultAsync(
            document => document.Id == documentId &&
                        document.WorkspaceId == workspaceId &&
                        document.IsActive,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ProcessingAttempt>> GetAttemptsAsync(
        Guid versionId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.ProcessingAttempts
            .AsNoTracking()
            .Where(attempt => attempt.DocumentVersionId == versionId)
            .OrderBy(attempt => attempt.AttemptNumber)
            .ToListAsync(cancellationToken);
    }

    private static bool IsVersionConflict(DbUpdateException exception)
    {
        var postgresException = exception.InnerException as PostgresException;
        return postgresException?.SqlState == PostgresErrorCodes.UniqueViolation &&
            (postgresException.ConstraintName == "IX_DocumentVersions_DocumentId_ContentHash" ||
             postgresException.ConstraintName == "IX_DocumentVersions_DocumentId_VersionNumber");
    }
}