using FluentValidation;

namespace BankingBrain.Application.Features.Documents.Commands.IngestDocument;

public sealed class IngestDocumentValidator
    : AbstractValidator<IngestDocumentCommand>
{
    public IngestDocumentValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(500);
        RuleFor(x => x.MediaType).NotEmpty().MaximumLength(200);
        RuleFor(x => x.FileSize).GreaterThanOrEqualTo(0);
    }
}