using BankingBrain.Application.Common.CodeIntelligence.Projects;

namespace BankingBrain.Infrastructure.CodeIntelligence.Projects;

public sealed class DotNetCompilationBuilder : ICodeCompilationBuilder
{
    public CodeRepositoryCompilation Build(
        CodeRepositoryStructure repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var projects = repository.Projects
            .Select(project => new CodeProjectCompilation(
                project.Name,
                project.ProjectFilePath,
                project.SourceFiles,
                project.ProjectReferences,
                project.TargetFramework,
                project.FrameworkReferences))
            .ToArray();

        return new CodeRepositoryCompilation(projects);
    }
}
