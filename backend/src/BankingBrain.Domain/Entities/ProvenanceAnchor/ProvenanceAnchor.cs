namespace BankingBrain.Domain.Entities.ProvenanceAnchor;

public sealed class ProvenanceAnchor
{
    public Guid Id { get; private set; }

    public Guid DocumentVersionId { get; private set; }

    public string LocationReference { get; private set; } = string.Empty;

    public int? PageNumber { get; private set; }

    public string? SheetName { get; private set; }

    public string? SectionReference { get; private set; }

    public string? ExtractedValue { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    private ProvenanceAnchor()
    {
    }

    private ProvenanceAnchor(
        Guid id,
        Guid documentVersionId,
        string locationReference,
        int? pageNumber,
        string? sheetName,
        string? sectionReference,
        string? extractedValue)
    {
        Id = id;
        DocumentVersionId = documentVersionId;
        LocationReference = locationReference;
        PageNumber = pageNumber;
        SheetName = sheetName;
        SectionReference = sectionReference;
        ExtractedValue = extractedValue;
        CreatedOnUtc = DateTime.UtcNow;
    }

    public static ProvenanceAnchor Create(
        Guid documentVersionId,
        string locationReference,
        int? pageNumber,
        string? sheetName,
        string? sectionReference,
        string? extractedValue)
    {
        if (documentVersionId == Guid.Empty)
            throw new ArgumentException("Version ID is required.", nameof(documentVersionId));
        if (string.IsNullOrWhiteSpace(locationReference))
            throw new ArgumentException("Location reference is required.", nameof(locationReference));

        return new ProvenanceAnchor(
            Guid.NewGuid(),
            documentVersionId,
            locationReference.Trim(),
            pageNumber,
            sheetName?.Trim(),
            sectionReference?.Trim(),
            extractedValue);
    }
}