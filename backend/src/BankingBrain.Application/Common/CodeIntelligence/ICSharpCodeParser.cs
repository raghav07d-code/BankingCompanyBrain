namespace BankingBrain.Application.Common.CodeIntelligence;

public interface ICSharpCodeParser
{
    CodeFileModel Parse(string filePath, string sourceCode);
}
