using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace BankingBrain.Infrastructure.Processing;

public sealed class DocxDocumentProcessor : IDocumentProcessor
{
    public string Identity => "docx-openxml";

    public string Version => "1.0";

    public IReadOnlyCollection<string> SupportedMediaTypes =>
        new[] { "application/vnd.openxmlformats-officedocument.wordprocessingml.document" };

    public IReadOnlyCollection<string> SupportedExtensions => new[] { ".docx" };

    public IReadOnlyCollection<string> SupportedSignatures => new[] { "PK-ZIP-DOCX" };

    public bool CanProcess(string mediaType, string fileName, ReadOnlyMemory<byte> signature) =>
        SupportedMediaTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase) &&
        SupportedExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase) &&
        signature.Span.StartsWith("PK\x03\x04"u8);

    public Task<NormalizedDocument> ProcessAsync(
        DocumentProcessingInput input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var document = WordprocessingDocument.Open(input.Content, false);
            var body = document.MainDocumentPart?.Document?.Body
                ?? throw new InvalidDataException("The DOCX document body is missing.");
            var sections = new List<NormalizedSection>();
            var sequence = 0;
            var itemCount = 0;
            foreach (var element in body.Elements())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++itemCount > DocumentProcessorLimits.MaxOpenXmlItems)
                    throw new InvalidDataException("The DOCX contains too many elements.");

                if (element is Paragraph paragraph)
                {
                    var text = paragraph.InnerText.Trim();
                    if (text.Length == 0)
                        continue;
                    var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
                    var isHeading = style?.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) == true;
                    var isList = paragraph.ParagraphProperties?.NumberingProperties is not null;
                    sections.Add(new NormalizedSection(
                        sequence++,
                        isHeading ? "heading" : isList ? "list-item" : "paragraph",
                        isHeading ? text : null,
                        isHeading ? null : text,
                        null,
                        $"docx:paragraph={itemCount}",
                        null,
                        isHeading ? "heading" : isList ? "list-item" : "text"));
                }
                else if (element is Table table)
                {
                    var tableSequence = sequence++;
                    sections.Add(new NormalizedSection(
                        tableSequence,
                        "table",
                        null,
                        null,
                        null,
                        $"docx:table={itemCount}",
                        null,
                        "table"));
                    var rowNumber = 0;
                    foreach (var row in table.Elements<TableRow>())
                    {
                        var columnNumber = 0;
                        foreach (var cell in row.Elements<TableCell>())
                        {
                            if (++itemCount > DocumentProcessorLimits.MaxOpenXmlItems)
                                throw new InvalidDataException("The DOCX table contains too many cells.");
                            var text = cell.InnerText.Trim();
                            sections.Add(new NormalizedSection(
                                sequence++,
                                "table-cell",
                                null,
                                text,
                                null,
                                $"docx:table={itemCount};row={rowNumber};column={columnNumber}",
                                ParentSequence: tableSequence,
                                ContentType: "text",
                                RowNumber: rowNumber,
                                ColumnNumber: columnNumber));
                            columnNumber++;
                        }
                        rowNumber++;
                    }
                }
            }

            return Task.FromResult(new NormalizedDocument(sections, "docx"));
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidDataException("The DOCX could not be read or is password protected.", exception);
        }
    }
}
