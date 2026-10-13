using BankingBrain.Application.Common.Models;

namespace BankingBrain.Application.Common.Interfaces;

public interface IDocumentProcessor
{
    string Identity { get; }

    string Version { get; }

    IReadOnlyCollection<string> SupportedMediaTypes { get; }

    IReadOnlyCollection<string> SupportedExtensions { get; }

    IReadOnlyCollection<string> SupportedSignatures { get; }

    bool CanProcess(
        string mediaType,
        string fileName,
        ReadOnlyMemory<byte> signature);

    Task<NormalizedDocument> ProcessAsync(
        DocumentProcessingInput input,
        CancellationToken cancellationToken);
}

public sealed record DocumentProcessingInput(
    string MediaType,
    string FileName,
    Stream Content);