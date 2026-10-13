namespace BankingBrain.Application.Common.CodeIntelligence;

public interface ICSharpProjectAnalyzer
{
    CodeProjectModel Analyze(
        string projectName,
        IReadOnlyCollection<CodeSourceFile> sourceFiles);
}
