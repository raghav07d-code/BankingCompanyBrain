using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Models;

namespace BankingBrain.Infrastructure.Processing;

public sealed class OcrDocumentProcessor : IDocumentProcessor
{
    public string Identity => "ocr-unavailable";

    public string Version => "1.0-unavailable";

    public IReadOnlyCollection<string> SupportedMediaTypes =>
        new[] { "image/png", "image/jpeg" };

    public IReadOnlyCollection<string> SupportedExtensions => new[] { ".png", ".jpg", ".jpeg" };

    public IReadOnlyCollection<string> SupportedSignatures => new[] { "PNG", "JPEG" };

    public bool CanProcess(string mediaType, string fileName, ReadOnlyMemory<byte> signature) =>
        SupportedMediaTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase) &&
        SupportedExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase) &&
        ((Path.GetExtension(fileName).Equals(".png", StringComparison.OrdinalIgnoreCase) &&
          signature.Span.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 })) ||
         (!Path.GetExtension(fileName).Equals(".png", StringComparison.OrdinalIgnoreCase) &&
          signature.Span.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })));

    public Task<NormalizedDocument> ProcessAsync(
        DocumentProcessingInput input,
        CancellationToken cancellationToken)
    {
        throw new InvalidDataException(
            "OCR is unavailable in this deployment; the original image remains available for a future OCR processor.");
    }
}
