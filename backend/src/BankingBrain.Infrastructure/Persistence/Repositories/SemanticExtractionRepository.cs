using BankingBrain.Application.Common.CodeIntelligence.Semantics;
using BankingBrain.Application.Common.Models;
using BankingBrain.Application.Features.Semantics.Interfaces;
using BankingBrain.Domain.Entities.Semantics;
using BankingBrain.Domain.Enums;
using BankingBrain.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BankingBrain.Infrastructure.Persistence.Repositories;

public sealed class SemanticExtractionRepository : ISemanticExtractionRepository
{
    private readonly ApplicationDbContext _db;
    private readonly IBankingSemanticVocabulary _vocabulary;

    public SemanticExtractionRepository(ApplicationDbContext db, IBankingSemanticVocabulary vocabulary) { _db = db; _vocabulary = vocabulary; }

    public async Task<SemanticSource?> GetSourceAsync(
        Guid workspaceId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken)
    {
        var version = await _db.DocumentVersions.AsNoTracking()
            .Join(_db.Documents, version => version.DocumentId, document => document.Id, (version, document) => new { version, document })
            .FirstOrDefaultAsync(item => item.version.Id == versionId && item.document.Id == documentId && item.document.WorkspaceId == workspaceId, cancellationToken);
        if (version is null || version.version.WorkspaceId != workspaceId)
            return null;

        var sections = await _db.DocumentSections.AsNoTracking()
            .Where(section => section.DocumentVersionId == versionId)
            .OrderBy(section => section.Sequence)
            .Select(section => new NormalizedSection(
                section.Sequence,
                section.Kind,
                section.Title,
                section.Text,
                section.PageNumber,
                section.SourceLocation,
                section.ParentSequence,
                section.ContentType,
                section.Value,
                section.SlideNumber,
                section.SheetName,
                section.RowNumber,
                section.ColumnNumber,
                section.CellReference,
                section.Id))
            .ToListAsync(cancellationToken);
        return new SemanticSource(workspaceId, versionId, version.version.ProcessingState, sections);
    }

    public async Task<SemanticSource?> GetSourceByVersionAsync(Guid workspaceId, Guid versionId, CancellationToken cancellationToken)
    {
        var documentId = await _db.DocumentVersions.AsNoTracking()
            .Where(version => version.Id == versionId && version.WorkspaceId == workspaceId)
            .Select(version => version.DocumentId)
            .FirstOrDefaultAsync(cancellationToken);
        return documentId == Guid.Empty
            ? null
            : await GetSourceAsync(workspaceId, documentId, versionId, cancellationToken);
    }

    public Task<SemanticExtractionRun?> GetSuccessfulAsync(Guid workspaceId, Guid versionId, string identity, string version, CancellationToken cancellationToken) =>
        _db.SemanticExtractionRuns.FirstOrDefaultAsync(run =>
            run.WorkspaceId == workspaceId && run.DocumentVersionId == versionId &&
            run.ExtractorIdentity == identity && run.ExtractorVersion == version &&
            run.Status == SemanticExtractionRunStatus.Succeeded, cancellationToken);

    public Task<SemanticExtractionRun?> GetActiveAsync(Guid workspaceId, Guid versionId, string identity, string version, CancellationToken cancellationToken) =>
        _db.SemanticExtractionRuns.FirstOrDefaultAsync(run =>
            run.WorkspaceId == workspaceId && run.DocumentVersionId == versionId &&
            run.ExtractorIdentity == identity && run.ExtractorVersion == version &&
            (run.Status == SemanticExtractionRunStatus.Pending || run.Status == SemanticExtractionRunStatus.Running), cancellationToken);

    public async Task<IReadOnlySet<Guid>> GetExtractedVersionIdsAsync(
        Guid workspaceId,
        string identity,
        string version,
        IReadOnlyCollection<Guid> candidateVersionIds,
        CancellationToken cancellationToken)
    {
        if (candidateVersionIds.Count == 0)
            return new HashSet<Guid>();

        var extractedVersionIds = await _db.SemanticExtractionRuns.AsNoTracking()
            .Where(run =>
                run.WorkspaceId == workspaceId &&
                run.ExtractorIdentity == identity &&
                run.ExtractorVersion == version &&
                run.Status == SemanticExtractionRunStatus.Succeeded &&
                candidateVersionIds.Contains(run.DocumentVersionId))
            .Select(run => run.DocumentVersionId)
            .ToListAsync(cancellationToken);

        return extractedVersionIds.ToHashSet();
    }

    public Task<bool> VersionBelongsToWorkspaceAsync(Guid workspaceId, Guid versionId, CancellationToken cancellationToken) =>
        _db.DocumentVersions.AnyAsync(version => version.Id == versionId && version.WorkspaceId == workspaceId, cancellationToken);

    public async Task<SemanticExtractionRun> AddRunAsync(SemanticExtractionRun run, CancellationToken cancellationToken)
    {
        await _db.SemanticExtractionRuns.AddAsync(run, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task<bool> TryQueueAsync(SemanticExtractionRun run, SemanticProcessingJob job, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await _db.SemanticExtractionRuns.AddAsync(run, cancellationToken);
        await _db.SemanticProcessingJobs.AddAsync(job, cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is Npgsql.PostgresException postgres && postgres.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task PersistAsync(
        SemanticExtractionRun run,
        SemanticExtractionResult result,
        SemanticSource source,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var entities = await _db.SemanticEntities.Where(entity => entity.WorkspaceId == source.WorkspaceId).ToListAsync(cancellationToken);
        var entityMap = entities.ToDictionary(entity => EntityKey(entity.EntityType, entity.NormalizedName), StringComparer.Ordinal);
        var externalMap = entities
            .Where(entity => !string.IsNullOrWhiteSpace(entity.ExternalIdentifier))
            .ToDictionary(entity => ExternalKey(entity.EntityType, entity.ExternalIdentifier!), StringComparer.Ordinal);
        var aliasMap = entities
            .SelectMany(entity => (entity.Aliases ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(alias => new { Key = EntityKey(entity.EntityType, SemanticEntity.Normalize(alias)), Entity = entity }))
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Entity, StringComparer.Ordinal);
        foreach (var candidate in result.Entities)
        {
            var key = EntityKey(candidate.EntityType, SemanticEntity.Normalize(candidate.CanonicalName));
            var hasExternalIdentifier = !string.IsNullOrWhiteSpace(candidate.ExternalIdentifier);
            if (!hasExternalIdentifier || !externalMap.TryGetValue(ExternalKey(candidate.EntityType, candidate.ExternalIdentifier!), out var entity))
            {
                if (!entityMap.TryGetValue(key, out entity) && !aliasMap.TryGetValue(key, out entity))
                {
                    entity = SemanticEntity.Create(source.WorkspaceId, candidate.EntityType, candidate.CanonicalName, candidate.ExternalIdentifier);
                    await _db.SemanticEntities.AddAsync(entity, cancellationToken);
                    entityMap[key] = entity;
                    if (hasExternalIdentifier) externalMap[ExternalKey(candidate.EntityType, candidate.ExternalIdentifier!)] = entity;
                }
            }
            entityMap[key] = entity;
            foreach (var alias in candidate.Aliases)
            {
                entity.AddAlias(alias);
                aliasMap[EntityKey(candidate.EntityType, SemanticEntity.Normalize(alias))] = entity;
            }
        }

        var sectionMap = source.Sections.ToDictionary(section => section.Sequence);
        foreach (var candidate in result.Claims)
        {
            if (!sectionMap.TryGetValue(candidate.EvidenceSequence, out var section))
                continue;
            if (!await _vocabulary.IsValidAsync(source.WorkspaceId, candidate.SubjectEntityType ?? "MENTION", candidate.Predicate, candidate.ObjectEntityType, cancellationToken))
                continue;
            var evidence = await AddEvidenceAsync(source, run, section, candidate.ObjectValue, candidate.ObjectValue, cancellationToken);
            var subjectEntity = FindEntity(entityMap, candidate.SubjectEntityType, candidate.Subject);
            var objectEntity = FindEntity(entityMap, candidate.ObjectEntityType, candidate.ObjectValue);
            var claim = SemanticClaim.Create(
                source.WorkspaceId,
                source.DocumentVersionId,
                run.Id,
                evidence.Id,
                subjectEntity?.Id,
                objectEntity?.Id,
                candidate.Subject,
                candidate.Predicate.ToLowerInvariant(),
                candidate.ObjectValue,
                candidate.Confidence,
                sourceAuthorityScore: candidate.SourceAuthorityScore,
                valueKind: candidate.ValueKind,
                numericValue: candidate.NumericValue,
                currencyCode: candidate.CurrencyCode,
                unit: candidate.Unit,
                validFromUtc: candidate.ValidFromUtc,
                validToUtc: candidate.ValidToUtc,
                asOfUtc: candidate.AsOfUtc,
                sourcePublishedOnUtc: candidate.SourcePublishedOnUtc,
                periodStartUtc: candidate.PeriodStartUtc,
                periodEndUtc: candidate.PeriodEndUtc);
            if (!await _db.SemanticClaims.AnyAsync(item => item.WorkspaceId == source.WorkspaceId && item.DocumentVersionId == source.DocumentVersionId && item.IdentityHash == claim.IdentityHash, cancellationToken))
                _db.SemanticClaims.Add(claim);
        }

        foreach (var candidate in result.Relationships)
        {
            if (!sectionMap.TryGetValue(candidate.EvidenceSequence, out var section))
                continue;
            var subjectEntity = FindEntity(entityMap, candidate.SubjectEntityType, candidate.Subject);
            var objectEntity = FindEntity(entityMap, candidate.ObjectEntityType, candidate.Object);
            if (subjectEntity is null || objectEntity is null)
                continue;
            if (!await _vocabulary.IsValidAsync(source.WorkspaceId, candidate.SubjectEntityType, candidate.Predicate, candidate.ObjectEntityType, cancellationToken))
                continue;
            var evidence = await AddEvidenceAsync(source, run, section, candidate.Object, candidate.Object, cancellationToken);
            var relationship = SemanticRelationship.Create(
                source.WorkspaceId,
                source.DocumentVersionId,
                run.Id,
                evidence.Id,
                subjectEntity.Id,
                objectEntity.Id,
                candidate.Predicate.ToLowerInvariant(),
                candidate.Confidence);
            if (!await _db.SemanticRelationships.AnyAsync(item => item.WorkspaceId == source.WorkspaceId && item.DocumentVersionId == source.DocumentVersionId && item.IdentityHash == relationship.IdentityHash, cancellationToken))
                _db.SemanticRelationships.Add(relationship);
        }

        run.Complete(result.Entities.Count, result.Claims.Count, result.Relationships.Count);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task FailAsync(SemanticExtractionRun run, string category, string error, CancellationToken cancellationToken)
    {
        run.Fail(category, error);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SemanticExtractionRun>> GetRunsAsync(Guid workspaceId, Guid versionId, CancellationToken cancellationToken) =>
        await _db.SemanticExtractionRuns.AsNoTracking()
            .Where(run => run.WorkspaceId == workspaceId && run.DocumentVersionId == versionId)
            .OrderByDescending(run => run.StartedOnUtc).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SemanticEntity>> GetEntitiesAsync(Guid workspaceId, CancellationToken cancellationToken) =>
        await _db.SemanticEntities.AsNoTracking().Where(entity => entity.WorkspaceId == workspaceId).OrderBy(entity => entity.CanonicalName).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SemanticClaim>> GetClaimsAsync(Guid workspaceId, Guid? versionId, Guid? entityId, string? predicate, DateTime? asOfUtc, DateTime? validAtUtc, CancellationToken cancellationToken)
    {
        var query = _db.SemanticClaims.AsNoTracking().Where(claim => claim.WorkspaceId == workspaceId && (versionId == null || claim.DocumentVersionId == versionId));
        if (entityId is not null) query = query.Where(claim => claim.SubjectEntityId == entityId || claim.ObjectEntityId == entityId);
        if (!string.IsNullOrWhiteSpace(predicate)) query = query.Where(claim => claim.Predicate.ToUpper() == predicate.ToUpper());
        if (asOfUtc is not null) query = query.Where(claim => claim.AsOfUtc == null || claim.AsOfUtc <= asOfUtc);
        if (validAtUtc is not null) query = query.Where(claim => (claim.ValidFromUtc == null || claim.ValidFromUtc <= validAtUtc) && (claim.ValidToUtc == null || claim.ValidToUtc >= validAtUtc));
        return await query.OrderBy(claim => claim.CreatedOnUtc).ToListAsync(cancellationToken);
    }

    public async Task<SemanticClaimDetails?> GetClaimAsync(Guid workspaceId, Guid claimId, CancellationToken cancellationToken)
    {
        var claim = await _db.SemanticClaims.AsNoTracking().FirstOrDefaultAsync(item => item.Id == claimId && item.WorkspaceId == workspaceId, cancellationToken);
        if (claim is null) return null;
        var evidence = await _db.SemanticEvidence.AsNoTracking().FirstOrDefaultAsync(item => item.Id == claim.EvidenceId && item.WorkspaceId == workspaceId, cancellationToken);
        if (evidence is null) return null;
        var subject = claim.SubjectEntityId == null ? null : await _db.SemanticEntities.AsNoTracking().FirstOrDefaultAsync(item => item.Id == claim.SubjectEntityId && item.WorkspaceId == workspaceId, cancellationToken);
        var objectEntity = claim.ObjectEntityId == null ? null : await _db.SemanticEntities.AsNoTracking().FirstOrDefaultAsync(item => item.Id == claim.ObjectEntityId && item.WorkspaceId == workspaceId, cancellationToken);
        return new SemanticClaimDetails(claim, evidence, subject, objectEntity);
    }

    private async Task<SemanticEvidence> AddEvidenceAsync(SemanticSource source, SemanticExtractionRun run, NormalizedSection section, string? text, string? value, CancellationToken cancellationToken)
    {
        var extractedText = section.Text ?? text;
        var extractedValue = section.Value ?? value;
        var existing = await _db.SemanticEvidence.FirstOrDefaultAsync(item =>
            item.WorkspaceId == source.WorkspaceId && item.DocumentVersionId == source.DocumentVersionId &&
            item.DocumentSectionId == section.DocumentSectionId && item.SourceLocation == (section.SourceLocation ?? $"section:{section.Sequence}") &&
            item.ExtractedText == extractedText && item.ExtractedValue == extractedValue, cancellationToken);
        if (existing is not null)
            return existing;
        var evidence = SemanticEvidence.Create(
            source.WorkspaceId,
            source.DocumentVersionId,
            run.Id,
            section.DocumentSectionId,
            section.SourceLocation ?? $"section:{section.Sequence}",
            section.PageNumber,
            section.SheetName,
            section.SlideNumber,
            section.RowNumber,
            section.ColumnNumber,
            section.CellReference,
            extractedText,
            extractedValue);
        _db.SemanticEvidence.Add(evidence);
        return evidence;
    }

    public async Task<CodeSemanticPersistSummary> PersistCodeObservationsAsync(
        Guid workspaceId,
        string extractorIdentity,
        string extractorVersion,
        IReadOnlyList<CodeEntityObservation> entities,
        IReadOnlyList<CodeFileRelationshipBatch> fileRelationships,
        CancellationToken cancellationToken)
    {
        var entityIndex = await EnsureCodeEntitiesAsync(workspaceId, entities, cancellationToken);

        var runsSucceeded = 0;
        var runsFailed = 0;
        var runsSkipped = 0;
        var relationshipsPersisted = 0;

        foreach (var file in fileRelationships)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var successful = await GetSuccessfulAsync(workspaceId, file.DocumentVersionId, extractorIdentity, extractorVersion, cancellationToken);
            if (successful is not null)
            {
                runsSkipped++;
                continue;
            }

            var run = await GetActiveAsync(workspaceId, file.DocumentVersionId, extractorIdentity, extractorVersion, cancellationToken);
            if (run is null)
            {
                run = SemanticExtractionRun.Start(workspaceId, file.DocumentVersionId, extractorIdentity, extractorVersion);
                await _db.SemanticExtractionRuns.AddAsync(run, cancellationToken);
            }

            run.MarkRunning();
            await _db.SaveChangesAsync(cancellationToken);

            try
            {
                var persisted = await PersistCodeRelationshipsAsync(run, file.DocumentVersionId, entityIndex, file.Relationships, cancellationToken);
                relationshipsPersisted += persisted;
                runsSucceeded++;
            }
            catch (Exception exception)
            {
                run.Fail("code-intelligence-error", exception.Message);
                await _db.SaveChangesAsync(cancellationToken);
                runsFailed++;
            }
        }

        return new CodeSemanticPersistSummary(entityIndex.Count, relationshipsPersisted, runsSucceeded, runsFailed, runsSkipped);
    }

    private async Task<Dictionary<string, SemanticEntity>> EnsureCodeEntitiesAsync(
        Guid workspaceId,
        IReadOnlyList<CodeEntityObservation> observations,
        CancellationToken cancellationToken)
    {
        var existing = await _db.SemanticEntities
            .Where(entity => entity.WorkspaceId == workspaceId && entity.ExternalIdentifier != null)
            .ToListAsync(cancellationToken);

        var index = existing.ToDictionary(entity => entity.ExternalIdentifier!, StringComparer.Ordinal);

        foreach (var observation in observations)
        {
            if (index.ContainsKey(observation.ExternalIdentifier))
                continue;

            var entity = SemanticEntity.Create(
                workspaceId,
                observation.EntityType,
                observation.CanonicalName,
                observation.ExternalIdentifier);

            _db.SemanticEntities.Add(entity);
            index[observation.ExternalIdentifier] = entity;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return index;
    }

    private async Task<int> PersistCodeRelationshipsAsync(
        SemanticExtractionRun run,
        Guid documentVersionId,
        IReadOnlyDictionary<string, SemanticEntity> entityIndex,
        IReadOnlyList<CodeRelationshipObservation> relationships,
        CancellationToken cancellationToken)
    {
        var documentSectionId = await _db.DocumentSections.AsNoTracking()
            .Where(section => section.DocumentVersionId == documentVersionId)
            .OrderBy(section => section.Sequence)
            .Select(section => (Guid?)section.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // The (WorkspaceId, DocumentVersionId) scope is fixed for this whole call, so the
        // set of identity hashes it could ever collide with is loaded once here instead of
        // one AnyAsync round trip per relationship. Comparison semantics are unchanged:
        // same columns, same hash.
        var existingIdentityHashes = (await _db.SemanticRelationships
                .AsNoTracking()
                .Where(item => item.WorkspaceId == run.WorkspaceId && item.DocumentVersionId == documentVersionId)
                .Select(item => item.IdentityHash)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var persisted = 0;

        foreach (var relationship in relationships)
        {
            if (!entityIndex.TryGetValue(relationship.SubjectExternalIdentifier, out var subjectEntity) ||
                !entityIndex.TryGetValue(relationship.ObjectExternalIdentifier, out var objectEntity))
            {
                continue;
            }

            var evidence = SemanticEvidence.Create(
                run.WorkspaceId,
                documentVersionId,
                run.Id,
                documentSectionId,
                relationship.SourceLocation,
                null, null, null, null, null, null,
                null,
                null);

            var semanticRelationship = SemanticRelationship.Create(
                run.WorkspaceId,
                documentVersionId,
                run.Id,
                evidence.Id,
                subjectEntity.Id,
                objectEntity.Id,
                relationship.Predicate,
                relationship.Confidence);

            if (!existingIdentityHashes.Add(semanticRelationship.IdentityHash))
            {
                continue;
            }

            _db.SemanticEvidence.Add(evidence);
            _db.SemanticRelationships.Add(semanticRelationship);
            persisted++;
        }

        run.Complete(0, 0, persisted);
        await _db.SaveChangesAsync(cancellationToken);
        return persisted;
    }

    private static SemanticEntity? FindEntity(Dictionary<string, SemanticEntity> entities, string? type, string name)
    {
        if (type is null) return null;
        entities.TryGetValue(EntityKey(type, SemanticEntity.Normalize(name)), out var entity);
        return entity;
    }

    private static string EntityKey(string type, string normalizedName) => $"{type}|{normalizedName}";
    private static string ExternalKey(string type, string externalIdentifier) => $"{type}|{externalIdentifier.Trim().ToUpperInvariant()}";
}
