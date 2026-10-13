namespace BankingBrain.Domain.Entities.DocumentSection;

public sealed class DocumentSection
{
    public Guid Id { get; private set; }

    public Guid DocumentVersionId { get; private set; }

    public int Sequence { get; private set; }

    public string Kind { get; private set; } = string.Empty;

    public string? Title { get; private set; }

    public string? Text { get; private set; }

    public int? PageNumber { get; private set; }

    public string? SourceLocation { get; private set; }

    public int? ParentSequence { get; private set; }

    public string? ContentType { get; private set; }

    public string? Value { get; private set; }

    public int? SlideNumber { get; private set; }

    public string? SheetName { get; private set; }

    public int? RowNumber { get; private set; }

    public int? ColumnNumber { get; private set; }

    public string? CellReference { get; private set; }

    private DocumentSection()
    {
    }

    private DocumentSection(
        Guid id,
        Guid documentVersionId,
        int sequence,
        string kind,
        string? title,
        string? text,
        int? pageNumber,
        string? sourceLocation,
        int? parentSequence,
        string? contentType,
        string? value,
        int? slideNumber,
        string? sheetName,
        int? rowNumber,
        int? columnNumber,
        string? cellReference)
    {
        Id = id;
        DocumentVersionId = documentVersionId;
        Sequence = sequence;
        Kind = kind;
        Title = title;
        Text = text;
        PageNumber = pageNumber;
        SourceLocation = sourceLocation;
        ParentSequence = parentSequence;
        ContentType = contentType;
        Value = value;
        SlideNumber = slideNumber;
        SheetName = sheetName;
        RowNumber = rowNumber;
        ColumnNumber = columnNumber;
        CellReference = cellReference;
    }

    public static DocumentSection Create(
        Guid documentVersionId,
        int sequence,
        string kind,
        string? title,
        string? text,
        int? pageNumber,
        string? sourceLocation)
    {
        return Create(
            documentVersionId,
            sequence,
            kind,
            title,
            text,
            pageNumber,
            sourceLocation,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
    }

    public static DocumentSection Create(
        Guid documentVersionId,
        int sequence,
        string kind,
        string? title,
        string? text,
        int? pageNumber,
        string? sourceLocation,
        int? parentSequence,
        string? contentType,
        string? value,
        int? slideNumber,
        string? sheetName,
        int? rowNumber,
        int? columnNumber,
        string? cellReference)
    {
        if (documentVersionId == Guid.Empty)
            throw new ArgumentException("Version ID is required.", nameof(documentVersionId));
        if (sequence < 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        if (string.IsNullOrWhiteSpace(kind))
            throw new ArgumentException("Section kind is required.", nameof(kind));

        return new DocumentSection(
            Guid.NewGuid(),
            documentVersionId,
            sequence,
            kind.Trim(),
            title?.Trim(),
            text,
            pageNumber,
            sourceLocation?.Trim(),
            parentSequence,
            contentType?.Trim(),
            value,
            slideNumber,
            sheetName?.Trim(),
            rowNumber,
            columnNumber,
            cellReference?.Trim());
    }
}