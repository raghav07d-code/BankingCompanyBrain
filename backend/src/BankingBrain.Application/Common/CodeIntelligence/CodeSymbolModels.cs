namespace BankingBrain.Application.Common.CodeIntelligence;

public enum CodeSymbolKind
{
    Namespace = 1,
    Class = 2,
    Interface = 3,
    Struct = 4,
    Record = 5,
    Enum = 6,
    Method = 7,
    Property = 8,
    Field = 9
}

public sealed record CodeSymbol(
    string Name,
    CodeSymbolKind Kind,
    string FullyQualifiedName,
    string? ParentFullyQualifiedName,
    int StartLine,
    int EndLine);

public sealed record CodeTypeRelationship(
    string SourceType,
    string TargetType,
    string RelationshipKind);

public sealed record CodeFileModel(
    string FilePath,
    string? Namespace,
    IReadOnlyList<CodeSymbol> Symbols,
    IReadOnlyList<CodeTypeRelationship> TypeRelationships);
