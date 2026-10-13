using BankingBrain.Domain.Enums;

namespace BankingBrain.Application.Features.Semantics.Commands.ExtractSemantics;

public sealed record ExtractSemanticsResponse(
    Guid ExtractionRunId,
    Guid DocumentVersionId,
    string ExtractorIdentity,
    string ExtractorVersion,
    SemanticExtractionRunStatus Status,
    int EntityCount,
    int ClaimCount,
    int RelationshipCount,
    Guid? JobId = null,
    ProcessingJobStatus? JobStatus = null);
