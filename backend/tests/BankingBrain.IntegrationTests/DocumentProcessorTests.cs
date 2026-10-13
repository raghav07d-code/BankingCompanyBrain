using System.IO.Compression;
using System.Text;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Models;
using BankingBrain.Infrastructure.Processing;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using A = DocumentFormat.OpenXml.Drawing;
using W = DocumentFormat.OpenXml.Wordprocessing;
using Xunit;

namespace BankingBrain.IntegrationTests;

public sealed class DocumentProcessorTests
{
    [Fact]
    public async Task Pdf_processor_preserves_page_provenance()
    {
        var processor = new PdfDocumentProcessor();
        await using var content = new MemoryStream(CreatePdf());
        var result = await processor.ProcessAsync(
            new DocumentProcessingInput("application/pdf", "statement.pdf", content),
            default);

        var section = Assert.Single(result.Sections);
        Assert.Equal("pdf:page=1", section.SourceLocation);
        Assert.Equal(1, section.PageNumber);
        Assert.Contains("Statement", section.Text);
    }

    [Fact]
    public async Task Docx_processor_preserves_headings_and_table_cells()
    {
        var processor = new DocxDocumentProcessor();
        await using var content = new MemoryStream(CreateDocx());
        var result = await processor.ProcessAsync(
            new DocumentProcessingInput(
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "statement.docx",
                content),
            default);

        Assert.Contains(result.Sections, section => section.Kind == "heading");
        var cell = Assert.Single(result.Sections, section => section.Kind == "table-cell");
        Assert.Equal("Balance", cell.Text);
        Assert.Equal(0, cell.RowNumber);
        Assert.Equal(0, cell.ColumnNumber);
    }

    [Fact]
    public async Task Xlsx_processor_preserves_sheet_cell_formula_and_value()
    {
        var processor = new XlsxDocumentProcessor();
        await using var content = new MemoryStream(CreateXlsx());
        var result = await processor.ProcessAsync(
            new DocumentProcessingInput(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "balances.xlsx",
                content),
            default);

        var cell = Assert.Single(result.Sections, section => section.Kind == "cell");
        Assert.Equal("Sheet1", cell.SheetName);
        Assert.Equal("A1", cell.CellReference);
        Assert.Equal("100", cell.Value);
        Assert.Contains("A1", cell.SourceLocation);
    }

    [Fact]
    public async Task Pptx_processor_preserves_slide_text_and_location()
    {
        var processor = new PptxDocumentProcessor();
        await using var content = new MemoryStream(CreatePptx());
        var result = await processor.ProcessAsync(
            new DocumentProcessingInput(
                "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                "briefing.pptx",
                content),
            default);

        var text = Assert.Single(result.Sections, section => section.Kind == "text-block");
        Assert.Equal(1, text.SlideNumber);
        Assert.Contains("Risk", text.Text);
    }

    [Fact]
    public void Processors_reject_extension_signature_mismatch()
    {
        var processor = new PdfDocumentProcessor();
        Assert.False(processor.CanProcess("application/pdf", "statement.pdf", "PK\x03\x04"u8.ToArray()));
    }

    [Fact]
    public async Task Ocr_boundary_fails_explicitly_when_runtime_is_unavailable()
    {
        var processor = new OcrDocumentProcessor();
        await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync(
            new DocumentProcessingInput("image/png", "scan.png", new MemoryStream()),
            default));
    }

    [Fact]
    public async Task Malformed_pdf_fails_without_exposing_parser_details()
    {
        var processor = new PdfDocumentProcessor();
        await using var content = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7\nmalformed"));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync(
            new DocumentProcessingInput("application/pdf", "bad.pdf", content),
            default));

        Assert.Contains("could not be read", exception.Message);
    }

    private static byte[] CreatePdf()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        page.AddText("Statement", 14, new UglyToad.PdfPig.Core.PdfPoint(40, 760), font);
        return builder.Build();
    }

    private static byte[] CreateDocx()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(
                new Body(
                    new Paragraph(
                        new W.ParagraphProperties(new W.ParagraphStyleId { Val = "Heading1" }),
                        new W.Run(new W.Text("Account Summary"))),
                    new W.Table(
                        new W.TableRow(new W.TableCell(new W.Paragraph(new W.Run(new W.Text("Balance"))))))));
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private static byte[] CreateXlsx()
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook(new Sheets());
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = new Worksheet(
                new SheetData(new Row(new Cell { CellReference = "A1", CellValue = new CellValue("100") })));
            var sheets = workbookPart.Workbook.GetFirstChild<Sheets>()!;
            sheets.Append(new Sheet
            {
                Name = "Sheet1",
                SheetId = 1,
                Id = workbookPart.GetIdOfPart(worksheetPart)
            });
            workbookPart.Workbook.Save();
        }
        return stream.ToArray();
    }

    private static byte[] CreatePptx()
    {
        using var stream = new MemoryStream();
        using (var document = PresentationDocument.Create(stream, PresentationDocumentType.Presentation, true))
        {
            var presentationPart = document.AddPresentationPart();
            var slidePart = presentationPart.AddNewPart<SlidePart>();
            slidePart.Slide = new Slide(
                new CommonSlideData(
                    new ShapeTree(
                        new NonVisualGroupShapeProperties(
                            new NonVisualDrawingProperties { Id = 1, Name = "Group" },
                            new NonVisualGroupShapeDrawingProperties(),
                            new ApplicationNonVisualDrawingProperties()),
                        new GroupShapeProperties(),
                        new Shape(
                            new NonVisualShapeProperties(
                                new NonVisualDrawingProperties { Id = 2, Name = "Risk" },
                                new NonVisualShapeDrawingProperties(),
                                new ApplicationNonVisualDrawingProperties()),
                            new ShapeProperties(),
                            new TextBody(
                                new A.BodyProperties(),
                                new A.ListStyle(),
                                new A.Paragraph(new A.Run(new A.Text("Risk"))))))));
            presentationPart.Presentation = new Presentation(
                new SlideIdList(new SlideId
                {
                    Id = 256,
                    RelationshipId = presentationPart.GetIdOfPart(slidePart)
                }));
            presentationPart.Presentation.Save();
        }
        return stream.ToArray();
    }
}
