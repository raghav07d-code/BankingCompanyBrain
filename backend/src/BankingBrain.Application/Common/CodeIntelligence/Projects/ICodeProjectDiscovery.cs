namespace BankingBrain.Application.Common.CodeIntelligence.Projects;

public interface ICodeProjectDiscovery
{
    CodeRepositoryStructure Discover(string repositoryRoot);
}
