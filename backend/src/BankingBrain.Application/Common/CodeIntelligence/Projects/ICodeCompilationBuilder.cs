namespace BankingBrain.Application.Common.CodeIntelligence.Projects;

public interface ICodeCompilationBuilder
{
    CodeRepositoryCompilation Build(
        CodeRepositoryStructure repository);
}
