using BankingBrain.Domain.Enums;

namespace BankingBrain.Application.Features.Documents.Commands.IngestDocument;

public sealed record IngestDocumentResponse(
    Guid DocumentVersionId,
    Guid DocumentId,
    int VersionNumber,
    string ContentHash,
    DocumentProcessingState ProcessingState,
    bool AlreadyCurrent);