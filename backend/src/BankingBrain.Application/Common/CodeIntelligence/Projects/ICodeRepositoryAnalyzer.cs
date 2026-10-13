namespace BankingBrain.Application.Common.CodeIntelligence.Projects;

public interface ICodeRepositoryAnalyzer
{
    /// <summary>
    /// Analyzes a repository's discovered projects. Every source file is still parsed and
    /// included in its project's Roslyn compilation (required for correct cross-file/
    /// cross-project symbol resolution), but files listed in <paramref name="unchangedFilePaths"/>
    /// are not walked to extract symbols/relationships - the caller already has a current,
    /// persisted extraction for their content and only needs new/changed files re-analyzed.
    /// </summary>
    Task<CodeRepositoryAnalysisResult> AnalyzeAsync(
        CodeRepositoryStructure repository,
        IReadOnlySet<string> unchangedFilePaths,
        CancellationToken cancellationToken);
}
