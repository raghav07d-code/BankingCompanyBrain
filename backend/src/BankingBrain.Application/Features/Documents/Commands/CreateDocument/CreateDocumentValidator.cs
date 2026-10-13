using FluentValidation;

namespace BankingBrain.Application.Features.Documents.Commands.CreateDocument;

public sealed class CreateDocumentValidator
    : AbstractValidator<CreateDocumentCommand>
{
    public CreateDocumentValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(300);

        RuleFor(x => x.ExternalIdentifier)
            .MaximumLength(500);

        RuleFor(x => x.MimeType)
            .NotEmpty()
            .MaximumLength(200);
    }
}