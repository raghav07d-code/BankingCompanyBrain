using BankingBrain.Application.Features.Documents.Interfaces;
using BankingBrain.Domain.Entities.Document;
using BankingBrain.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BankingBrain.Infrastructure.Persistence.Repositories;

public sealed class DocumentRepository : IDocumentRepository
{
    private readonly ApplicationDbContext _dbContext;

    public DocumentRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task AddAsync(
        Document document,
        CancellationToken cancellationToken)
    {
        return _dbContext.Documents.AddAsync(
            document,
            cancellationToken).AsTask();
    }

    public async Task<bool> SaveChangesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception)
            when (IsDuplicateName(exception))
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<Document>> GetByWorkspaceAsync(
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Documents
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<Document?> GetByIdAsync(
        Guid workspaceId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        return _dbContext.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == documentId &&
                     x.WorkspaceId == workspaceId &&
                     x.IsActive,
                cancellationToken);
    }

    public Task<Document?> GetByExternalIdentifierAsync(
        Guid workspaceId,
        Guid knowledgeSourceId,
        string externalIdentifier,
        CancellationToken cancellationToken)
    {
        return _dbContext.Documents
            .FirstOrDefaultAsync(
                x => x.WorkspaceId == workspaceId &&
                     x.KnowledgeSourceId == knowledgeSourceId &&
                     x.ExternalIdentifier == externalIdentifier,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Document>> GetByKnowledgeSourceAsync(
        Guid workspaceId,
        Guid knowledgeSourceId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Documents
            .Where(x =>
                x.WorkspaceId == workspaceId &&
                x.KnowledgeSourceId == knowledgeSourceId)
            .ToListAsync(cancellationToken);
    }

    private static bool IsDuplicateName(DbUpdateException exception)
    {
        var postgresException = exception.InnerException as PostgresException;

        return postgresException?.SqlState == PostgresErrorCodes.UniqueViolation &&
            postgresException.ConstraintName == "IX_Documents_WorkspaceId_KnowledgeSourceId_Name";
    }
}