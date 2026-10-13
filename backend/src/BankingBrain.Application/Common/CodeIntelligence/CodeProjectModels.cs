namespace BankingBrain.Application.Common.CodeIntelligence;

public sealed record CodeSourceFile(
    string FilePath,
    string SourceCode);

public sealed record CodeProjectModel(
    string Name,
    string ProjectFilePath,
    IReadOnlyList<CodeFileModel> Files,
    IReadOnlyList<CodeSymbol> Symbols,
    IReadOnlyList<CodeRelationship> Relationships);

public sealed record CodeRelationship(
    string SourceSymbol,
    string TargetSymbol,
    string RelationshipKind,
    string FilePath,
    int StartLine,
    int EndLine);
