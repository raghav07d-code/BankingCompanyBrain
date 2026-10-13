using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Models;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace BankingBrain.Infrastructure.Processing;

public sealed class XlsxDocumentProcessor : IDocumentProcessor
{
    public string Identity => "xlsx-openxml";

    public string Version => "1.0";

    public IReadOnlyCollection<string> SupportedMediaTypes =>
        new[] { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" };

    public IReadOnlyCollection<string> SupportedExtensions => new[] { ".xlsx" };

    public IReadOnlyCollection<string> SupportedSignatures => new[] { "PK-ZIP-XLSX" };

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
            using var document = SpreadsheetDocument.Open(input.Content, false);
            var workbookPart = document.WorkbookPart
                ?? throw new InvalidDataException("The XLSX workbook part is missing.");
            var workbook = workbookPart.Workbook
                ?? throw new InvalidDataException("The XLSX workbook is missing.");
            var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
            var sections = new List<NormalizedSection>();
            var sequence = 0;
            var rowCount = 0;
            var cellCount = 0;
            var sheets = workbook.Sheets
                ?? throw new InvalidDataException("The XLSX sheet list is missing.");
            foreach (var sheet in sheets.Elements<Sheet>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relationshipId = sheet.Id?.Value
                    ?? throw new InvalidDataException("The XLSX sheet relationship is missing.");
                var worksheetPart = (WorksheetPart)workbookPart.GetPartById(relationshipId);
                var sheetSequence = sequence++;
                var sheetName = sheet.Name?.Value ?? "Sheet";
                sections.Add(new NormalizedSection(
                    sheetSequence,
                    "sheet",
                    sheetName,
                    null,
                    null,
                    $"xlsx:sheet={sheetName}",
                    null,
                    "sheet"));

                var worksheet = worksheetPart.Worksheet
                    ?? throw new InvalidDataException("The XLSX worksheet is missing.");
                var sheetData = worksheet.GetFirstChild<SheetData>();
                if (sheetData is null)
                    continue;
                foreach (var row in sheetData.Elements<Row>())
                {
                    if (++rowCount > DocumentProcessorLimits.MaxSpreadsheetRows)
                        throw new InvalidDataException("The XLSX contains too many rows.");
                    var rowNumber = checked((int)(row.RowIndex?.Value ?? (uint)rowCount));
                    var rowSequence = sequence++;
                    sections.Add(new NormalizedSection(
                        rowSequence,
                        "row",
                        null,
                        null,
                        null,
                        $"xlsx:sheet={sheetName};row={rowNumber}",
                        ParentSequence: sheetSequence,
                        ContentType: "row",
                        SheetName: sheetName,
                        RowNumber: rowNumber));
                    foreach (var cell in row.Elements<Cell>())
                    {
                        if (++cellCount > DocumentProcessorLimits.MaxSpreadsheetCells)
                            throw new InvalidDataException("The XLSX contains too many cells.");
                        var reference = cell.CellReference?.Value ?? $"R{rowNumber}C{cellCount}";
                        var rawValue = cell.CellValue?.Text ?? string.Empty;
                        var value = cell.DataType?.Value == CellValues.SharedString &&
                                    int.TryParse(rawValue, out var sharedIndex) &&
                                    sharedStrings is not null &&
                                    sharedIndex >= 0 &&
                                    sharedIndex < sharedStrings.Count()
                            ? sharedStrings.ElementAt(sharedIndex).InnerText
                            : rawValue;
                        var formula = cell.CellFormula?.Text;
                        sections.Add(new NormalizedSection(
                            sequence++,
                            "cell",
                            reference,
                            formula is null ? value : $"{formula} => {value}",
                            null,
                            $"xlsx:sheet={sheetName};cell={reference}",
                            ParentSequence: rowSequence,
                            ContentType: "cell",
                            Value: value,
                            SheetName: sheetName,
                            RowNumber: rowNumber,
                            CellReference: reference));
                    }
                }
            }

            return Task.FromResult(new NormalizedDocument(sections, "xlsx"));
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidDataException("The XLSX could not be read or is password protected.", exception);
        }
    }
}
