using BankingBrain.Domain.Enums;

namespace BankingBrain.Application.Features.Documents.Queries.GetDocumentVersions;

public sealed record DocumentVersionResponse(
    Guid DocumentVersionId,
    Guid DocumentId,
    int VersionNumber,
    string ContentHash,
    string MediaType,
    string FileName,
    long FileSize,
    DocumentProcessingState ProcessingState,
    DateTime CreatedOnUtc,
    DateTime? ProcessingStartedOnUtc,
    DateTime? ProcessingCompletedOnUtc,
    string? ProcessingError,
    string? ProcessorVersion);