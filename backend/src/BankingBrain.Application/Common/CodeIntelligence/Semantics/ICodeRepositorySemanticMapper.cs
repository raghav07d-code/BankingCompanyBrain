using BankingBrain.Application.Common.CodeIntelligence.Projects;

namespace BankingBrain.Application.Common.CodeIntelligence.Semantics;

/// <summary>
/// Maps a provider-neutral <see cref="CodeRepositoryAnalysisResult"/> onto the existing
/// semantic entity/relationship shape. Pure and Roslyn-free: everything here operates on
/// Application-layer models already produced by ICodeRepositoryAnalyzer.
/// </summary>
public interface ICodeRepositorySemanticMapper
{
    CodeRepositorySemanticObservations Map(
        CodeRepositoryAnalysisResult analysis,
        CodeFileDocumentIndex documentIndex,
        string repositoryCanonicalName,
        string repositoryExternalIdentifier);
}
