namespace BankingBrain.Application.Common.Connectors;

public enum SourceArtifactKind
{
    File = 1,
    Directory = 2,
    SourceCode = 3,
    SqlObject = 4,
    DatabaseTable = 5,
    DatabaseColumn = 6,
    ApiEndpoint = 7,
    CloudResource = 8,
    Configuration = 9
}