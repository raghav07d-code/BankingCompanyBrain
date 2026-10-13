using BankingBrain.Domain.Enums;

namespace BankingBrain.Application.Features.Documents.Commands.ReprocessDocumentVersion;

public sealed record ReprocessDocumentVersionResponse(
    Guid DocumentVersionId,
    DocumentProcessingState ProcessingState);