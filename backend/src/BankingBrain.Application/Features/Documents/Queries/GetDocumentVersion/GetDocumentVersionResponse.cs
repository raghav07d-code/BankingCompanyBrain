using BankingBrain.Application.Features.Documents.Queries.GetDocumentVersions;

namespace BankingBrain.Application.Features.Documents.Queries.GetDocumentVersion;

public sealed record GetDocumentVersionResponse(
    DocumentVersionResponse Version,
    IReadOnlyList<ProcessingAttemptResponse> Attempts);

public sealed record ProcessingAttemptResponse(
    Guid ProcessingAttemptId,
    int AttemptNumber,
    DateTime StartedOnUtc,
    DateTime? CompletedOnUtc,
    string? ProcessorIdentity,
    string? ProcessorVersion,
    bool? Succeeded,
    string? Error);