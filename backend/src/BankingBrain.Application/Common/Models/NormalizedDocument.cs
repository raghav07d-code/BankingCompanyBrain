namespace BankingBrain.Application.Common.Models;

public sealed record NormalizedDocument(
    IReadOnlyList<NormalizedSection> Sections,
    string ContentType = "document");

public sealed record NormalizedSection(
    int Sequence,
    string Kind,
    string? Title,
    string? Text,
    int? PageNumber,
    string? SourceLocation,
    int? ParentSequence = null,
    string? ContentType = null,
    string? Value = null,
    int? SlideNumber = null,
    string? SheetName = null,
    int? RowNumber = null,
    int? ColumnNumber = null,
    string? CellReference = null,
    Guid? DocumentSectionId = null);