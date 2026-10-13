using BankingBrain.Application.Common.Models;

namespace BankingBrain.Application.Common.Interfaces;

public interface ISemanticExtractor
{
    string Identity { get; }
    string Version { get; }

    Task<SemanticExtractionResult> ExtractAsync(
        SemanticExtractionInput input,
        CancellationToken cancellationToken);
}
