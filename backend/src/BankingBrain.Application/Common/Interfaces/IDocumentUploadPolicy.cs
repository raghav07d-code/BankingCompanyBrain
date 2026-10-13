namespace BankingBrain.Application.Common.Interfaces;

public interface IDocumentUploadPolicy
{
    long MaxFileSize { get; }
    bool IsAllowed(string fileName, string mediaType);
    Task<bool> HasValidContentAsync(
        string fileName,
        string mediaType,
        Stream content,
        CancellationToken cancellationToken);
}