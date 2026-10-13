using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Models;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;

namespace BankingBrain.Infrastructure.Processing;

public sealed class PptxDocumentProcessor : IDocumentProcessor
{
    public string Identity => "pptx-openxml";

    public string Version => "1.0";

    public IReadOnlyCollection<string> SupportedMediaTypes =>
        new[] { "application/vnd.openxmlformats-officedocument.presentationml.presentation" };

    public IReadOnlyCollection<string> SupportedExtensions => new[] { ".pptx" };

    public IReadOnlyCollection<string> SupportedSignatures => new[] { "PK-ZIP-PPTX" };

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
            using var document = PresentationDocument.Open(input.Content, false);
            var presentationPart = document.PresentationPart
                ?? throw new InvalidDataException("The PPTX presentation part is missing.");
            var presentation = presentationPart.Presentation
                ?? throw new InvalidDataException("The PPTX presentation is missing.");
            var slideIdList = presentation.SlideIdList
                ?? throw new InvalidDataException("The PPTX slide list is missing.");
            var slideIds = slideIdList.Elements<SlideId>().ToList();
            if (slideIds.Count > DocumentProcessorLimits.MaxPresentationSlides)
                throw new InvalidDataException("The PPTX contains too many slides.");

            var sections = new List<NormalizedSection>();
            var sequence = 0;
            for (var index = 0; index < slideIds.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var slideNumber = index + 1;
                var relationshipId = slideIds[index].RelationshipId;
                if (string.IsNullOrWhiteSpace(relationshipId))
                    throw new InvalidDataException("The PPTX slide relationship is missing.");
                var slidePart = (SlidePart)presentationPart.GetPartById(relationshipId!);
                var slide = slidePart.Slide
                    ?? throw new InvalidDataException("The PPTX slide is missing.");
                var slideSequence = sequence++;
                sections.Add(new NormalizedSection(
                    slideSequence,
                    "slide",
                    $"Slide {slideNumber}",
                    null,
                    null,
                    $"pptx:slide={slideNumber}",
                    null,
                    "slide",
                    null,
                    slideNumber));

                foreach (var shape in slide.Descendants<Shape>())
                {
                    var text = string.Join("", shape.TextBody?.Descendants<A.Text>()
                        .Select(value => value.Text)
                        .Where(value => !string.IsNullOrWhiteSpace(value)) ?? Array.Empty<string>()).Trim();
                    if (text.Length == 0)
                        continue;
                    var shapeId = shape.NonVisualShapeProperties?.NonVisualDrawingProperties?.Id?.Value;
                    sections.Add(new NormalizedSection(
                        sequence++,
                        "text-block",
                        null,
                        text,
                        null,
                        $"pptx:slide={slideNumber};shape={shapeId}",
                        slideSequence,
                        "text",
                        null,
                        slideNumber));
                }

                foreach (var table in slide.Descendants<A.Table>())
                {
                    var tableSequence = sequence++;
                    sections.Add(new NormalizedSection(
                        tableSequence,
                        "table",
                        null,
                        null,
                        null,
                        $"pptx:slide={slideNumber};table={tableSequence}",
                        slideSequence,
                        "table",
                        null,
                        slideNumber));
                    var rowNumber = 0;
                    foreach (var row in table.Elements<A.TableRow>())
                    {
                        var columnNumber = 0;
                        foreach (var cell in row.Elements<A.TableCell>())
                        {
                            var cellText = string.Join("", cell.TextBody?.Descendants<A.Text>()
                                .Select(value => value.Text) ?? Array.Empty<string>()).Trim();
                            sections.Add(new NormalizedSection(
                                sequence++,
                                "table-cell",
                                null,
                                cellText,
                                null,
                                $"pptx:slide={slideNumber};table={tableSequence};row={rowNumber};column={columnNumber}",
                                ParentSequence: tableSequence,
                                ContentType: "text",
                                SlideNumber: slideNumber,
                                RowNumber: rowNumber,
                                ColumnNumber: columnNumber));
                            columnNumber++;
                        }
                        rowNumber++;
                    }
                }
            }

            return Task.FromResult(new NormalizedDocument(sections, "pptx"));
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidDataException("The PPTX could not be read or is password protected.", exception);
        }
    }
}
