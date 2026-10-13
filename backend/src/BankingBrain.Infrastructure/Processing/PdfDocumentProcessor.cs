using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace BankingBrain.Infrastructure.Processing;

public sealed class PdfDocumentProcessor : IDocumentProcessor
{
    public string Identity => "pdf-text-layout";

    public string Version => "1.0";

    public IReadOnlyCollection<string> SupportedMediaTypes =>
        new[] { "application/pdf" };

    public IReadOnlyCollection<string> SupportedExtensions =>
        new[] { ".pdf" };

    public IReadOnlyCollection<string> SupportedSignatures =>
        new[] { "%PDF-" };

    public bool CanProcess(string mediaType, string fileName, ReadOnlyMemory<byte> signature) =>
        SupportedMediaTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase) &&
        SupportedExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase) &&
        signature.Span.StartsWith("%PDF-"u8);

    public Task<NormalizedDocument> ProcessAsync(
        DocumentProcessingInput input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var document = PdfDocument.Open(input.Content);
            if (document.NumberOfPages > DocumentProcessorLimits.MaxPdfPages)
                throw new InvalidDataException("The PDF contains too many pages.");

            var sections = new List<NormalizedSection>();
            var sequence = 0;
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = ContentOrderTextExtractor.GetText(page).Trim();
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                sections.Add(new NormalizedSection(
                    sequence++,
                    "paragraph",
                    null,
                    text,
                    page.Number,
                    $"pdf:page={page.Number}",
                    null,
                    "text",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null));
            }

            return Task.FromResult(new NormalizedDocument(sections, "pdf"));
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidDataException("The PDF could not be read or is password protected.", exception);
        }
    }
}
