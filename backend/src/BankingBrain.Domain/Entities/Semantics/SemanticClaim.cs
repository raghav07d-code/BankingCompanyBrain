using BankingBrain.Domain.Enums;

namespace BankingBrain.Domain.Entities.Semantics;

public sealed class SemanticClaim
{
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public Guid DocumentVersionId { get; private set; }
    public Guid ExtractionRunId { get; private set; }
    public Guid EvidenceId { get; private set; }
    public Guid? SubjectEntityId { get; private set; }
    public Guid? ObjectEntityId { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public string Predicate { get; private set; } = string.Empty;
    public string ObjectValue { get; private set; } = string.Empty;
    public decimal ExtractionConfidence { get; private set; }
    public decimal? SourceAuthorityScore { get; private set; }
    public SemanticClaimStatus Status { get; private set; }
    public string IdentityHash { get; private set; } = string.Empty;
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? ValidFromUtc { get; private set; }
    public DateTime? ValidToUtc { get; private set; }
    public DateTime? AsOfUtc { get; private set; }
    public DateTime? SourcePublishedOnUtc { get; private set; }
    public string ValueKind { get; private set; } = "text";
    public decimal? NumericValue { get; private set; }
    public string? CurrencyCode { get; private set; }
    public string? Unit { get; private set; }
    public DateTime? PeriodStartUtc { get; private set; }
    public DateTime? PeriodEndUtc { get; private set; }

    private SemanticClaim() { }

    public static SemanticClaim Create(
        Guid workspaceId,
        Guid documentVersionId,
        Guid extractionRunId,
        Guid evidenceId,
        Guid? subjectEntityId,
        Guid? objectEntityId,
        string subject,
        string predicate,
        string objectValue,
        decimal extractionConfidence,
        decimal? sourceAuthorityScore = null,
        SemanticClaimStatus status = SemanticClaimStatus.Candidate,
        DateTime? validFromUtc = null,
        DateTime? validToUtc = null,
        DateTime? asOfUtc = null,
        DateTime? sourcePublishedOnUtc = null,
        string valueKind = "text",
        decimal? numericValue = null,
        string? currencyCode = null,
        string? unit = null,
        DateTime? periodStartUtc = null,
        DateTime? periodEndUtc = null)
    {
        if (workspaceId == Guid.Empty || documentVersionId == Guid.Empty || extractionRunId == Guid.Empty || evidenceId == Guid.Empty)
            throw new ArgumentException("A claim requires workspace, version, run, and evidence identifiers.");
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(predicate) || string.IsNullOrWhiteSpace(objectValue))
            throw new ArgumentException("Claim subject, predicate, and object/value are required.");
        if (extractionConfidence is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(extractionConfidence));
        if (sourceAuthorityScore is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(sourceAuthorityScore));

        var normalized = $"{SemanticEntity.Normalize(subject)}|{SemanticEntity.Normalize(predicate)}|{SemanticEntity.Normalize(objectValue)}|{evidenceId:N}|{asOfUtc:O}|{validFromUtc:O}|{validToUtc:O}|{periodStartUtc:O}|{periodEndUtc:O}|{numericValue}|{currencyCode}|{unit}";
        return new SemanticClaim
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            DocumentVersionId = documentVersionId,
            ExtractionRunId = extractionRunId,
            EvidenceId = evidenceId,
            SubjectEntityId = subjectEntityId,
            ObjectEntityId = objectEntityId,
            Subject = subject.Trim(),
            Predicate = predicate.Trim(),
            ObjectValue = objectValue.Trim(),
            ExtractionConfidence = extractionConfidence,
            SourceAuthorityScore = sourceAuthorityScore,
            Status = status,
            IdentityHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized))),
            CreatedOnUtc = DateTime.UtcNow
            ,ValidFromUtc = validFromUtc
            ,ValidToUtc = validToUtc
            ,AsOfUtc = asOfUtc
            ,SourcePublishedOnUtc = sourcePublishedOnUtc
            ,ValueKind = valueKind.Trim().ToLowerInvariant()
            ,NumericValue = numericValue
            ,CurrencyCode = currencyCode?.Trim().ToUpperInvariant()
            ,Unit = unit?.Trim()
            ,PeriodStartUtc = periodStartUtc
            ,PeriodEndUtc = periodEndUtc
        };
    }
}
