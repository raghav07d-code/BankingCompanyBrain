using System.Text;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Models;

namespace BankingBrain.Infrastructure.Processing;

/// <summary>
/// Normalizes a SQL script into a single text section, the same shape
/// <see cref="TextDocumentProcessor"/> produces for plain text files - SQL Lineage
/// extraction (<see cref="BankingBrain.Infrastructure.SqlLineage.SqlLineageExtractionService"/>)
/// reads this section's full text back out via the existing
/// ISemanticExtractionRepository.GetSourceByVersionAsync, the same way other semantic
/// extraction already reads DocumentSection content, instead of re-reading the file from disk.
/// </summary>
public sealed class SqlDocumentProcessor : IDocumentProcessor
{
    public string Identity => "sql-normalizer";

    public string Version => "1.0";

    public IReadOnlyCollection<string> SupportedMediaTypes => new[] { "application/sql" };

    public IReadOnlyCollection<string> SupportedExtensions => new[] { ".sql" };

    public IReadOnlyCollection<string> SupportedSignatures => new[] { "utf8-text" };

    public bool CanProcess(
        string mediaType,
        string fileName,
        ReadOnlyMemory<byte> signature)
    {
        var declaredTypeMatches = mediaType.Equals("application/sql", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(fileName).Equals(".sql", StringComparison.OrdinalIgnoreCase);
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
                    "sql",
                    input.FileName,
                    text,
                    null,
                    "document")
            });
    }
}
