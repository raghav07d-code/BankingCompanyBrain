using BankingBrain.Application.Common.CodeIntelligence;

namespace BankingBrain.Application.Common.CodeIntelligence.Projects;

public sealed record CodeRepositoryAnalysisResult(
    string RootPath,
    IReadOnlyList<CodeProjectModel> Projects,
    IReadOnlyList<CodeRelationship> ProjectReferences);
