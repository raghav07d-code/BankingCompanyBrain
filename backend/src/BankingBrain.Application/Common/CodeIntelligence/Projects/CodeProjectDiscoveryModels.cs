namespace BankingBrain.Application.Common.CodeIntelligence.Projects;

public sealed record CodeProject(
    string Name,
    string ProjectFilePath,
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<string> ProjectReferences,
    string? TargetFramework = null,
    IReadOnlyList<string>? FrameworkReferences = null);

public sealed record CodeRepositoryStructure(
    string RootPath,
    IReadOnlyList<CodeProject> Projects,
    IReadOnlyList<string> SolutionFiles,
    IReadOnlyList<string> UnassignedCSharpFiles);
