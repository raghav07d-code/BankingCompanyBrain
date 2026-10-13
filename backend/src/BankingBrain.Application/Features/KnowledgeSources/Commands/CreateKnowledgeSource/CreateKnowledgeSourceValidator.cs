using FluentValidation;

namespace BankingBrain.Application.Features.KnowledgeSources.Commands.CreateKnowledgeSource;

public sealed class CreateKnowledgeSourceValidator
    : AbstractValidator<CreateKnowledgeSourceCommand>
{
    public CreateKnowledgeSourceValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.Description)
            .MaximumLength(2000);

        RuleFor(x => x.SourceType)
            .IsInEnum();
    }
}