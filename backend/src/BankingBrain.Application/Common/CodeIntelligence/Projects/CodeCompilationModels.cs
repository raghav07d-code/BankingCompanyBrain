namespace BankingBrain.Application.Common.CodeIntelligence.Projects;

public sealed record CodeProjectCompilation(
    string ProjectName,
    string ProjectFilePath,
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<string> ProjectReferences,
    string? TargetFramework = null,
    IReadOnlyList<string>? FrameworkReferences = null);

public sealed record CodeRepositoryCompilation(
    IReadOnlyList<CodeProjectCompilation> Projects);
