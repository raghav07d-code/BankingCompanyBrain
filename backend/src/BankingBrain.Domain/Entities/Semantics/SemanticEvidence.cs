namespace BankingBrain.Domain.Entities.Semantics;

public sealed class SemanticEvidence
{
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public Guid DocumentVersionId { get; private set; }
    public Guid ExtractionRunId { get; private set; }
    public Guid? DocumentSectionId { get; private set; }
    public string SourceLocation { get; private set; } = string.Empty;
    public int? PageNumber { get; private set; }
    public string? SheetName { get; private set; }
    public int? SlideNumber { get; private set; }
    public int? RowNumber { get; private set; }
    public int? ColumnNumber { get; private set; }
    public string? CellReference { get; private set; }
    public string? ExtractedText { get; private set; }
    public string? ExtractedValue { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }

    private SemanticEvidence() { }

    public static SemanticEvidence Create(
        Guid workspaceId,
        Guid documentVersionId,
        Guid extractionRunId,
        Guid? documentSectionId,
        string sourceLocation,
        int? pageNumber,
        string? sheetName,
        int? slideNumber,
        int? rowNumber,
        int? columnNumber,
        string? cellReference,
        string? extractedText,
        string? extractedValue)
    {
        if (workspaceId == Guid.Empty || documentVersionId == Guid.Empty || extractionRunId == Guid.Empty)
            throw new ArgumentException("Semantic evidence requires workspace, version, and extraction run identifiers.");
        if (string.IsNullOrWhiteSpace(sourceLocation))
            throw new ArgumentException("Evidence source location is required.", nameof(sourceLocation));
        return new SemanticEvidence
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            DocumentVersionId = documentVersionId,
            ExtractionRunId = extractionRunId,
            DocumentSectionId = documentSectionId,
            SourceLocation = sourceLocation.Trim(),
            PageNumber = pageNumber,
            SheetName = sheetName?.Trim(),
            SlideNumber = slideNumber,
            RowNumber = rowNumber,
            ColumnNumber = columnNumber,
            CellReference = cellReference?.Trim(),
            ExtractedText = extractedText,
            ExtractedValue = extractedValue,
            CreatedOnUtc = DateTime.UtcNow
        };
    }
}
