using FluentValidation;

namespace BankingBrain.Application.Features.Workspaces.Commands.CreateWorkspace;

public sealed class CreateWorkspaceValidator
    : AbstractValidator<CreateWorkspaceCommand>
{
    public CreateWorkspaceValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.Description)
            .MaximumLength(2000);
    }
}