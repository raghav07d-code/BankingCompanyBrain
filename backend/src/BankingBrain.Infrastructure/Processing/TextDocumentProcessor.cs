using System.Text;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Models;

namespace BankingBrain.Infrastructure.Processing;

public sealed class TextDocumentProcessor : IDocumentProcessor
{
    public string Identity => "text-normalizer";

    public string Version => "1.0";

    public IReadOnlyCollection<string> SupportedMediaTypes =>
        new[] { "text/*", "application/json", "application/xml" };

    public IReadOnlyCollection<string> SupportedExtensions =>
        new[] { ".txt", ".csv", ".html", ".xml", ".json" };

    public IReadOnlyCollection<string> SupportedSignatures =>
        new[] { "utf8-text" };

    public bool CanProcess(
        string mediaType,
        string fileName,
        ReadOnlyMemory<byte> signature)
    {
        var declaredTypeMatches = mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
            mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
            mediaType.Equals("application/xml", StringComparison.OrdinalIgnoreCase) ||
            mediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(fileName).Equals(".csv", StringComparison.OrdinalIgnoreCase);
        return declaredTypeMatches && signature.Span.IndexOf((byte)0) < 0;
    }

    public async Task<NormalizedDocument> ProcessAsync(
        DocumentProcessingInput input,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            input.Content,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: false);
        var text = await reader.ReadToEndAsync(cancellationToken);
        return new NormalizedDocument(
            new[]
            {
                new NormalizedSection(
                    0,
                    "text",
                    input.FileName,
                    text,
                    null,
                    "document")
            });
    }
}