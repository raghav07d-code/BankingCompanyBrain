using MediatR;

namespace LoanApprovalBenchmark;

public record SubmitLoanApplicationCommand(Guid ApplicantId, decimal Amount);
public record GetLoanApplicationQuery(Guid LoanApplicationId);
public record ApproveLoanCommand(Guid LoanApplicationId, decimal Amount, string DecidedBy);
public record RejectLoanCommand(Guid LoanApplicationId, string DecidedBy);
public record NotifyApplicantCommand(Guid LoanApplicationId, string Message);

public class SubmitLoanApplicationHandler : IRequestHandler<SubmitLoanApplicationCommand, Guid>
{
    private readonly LoanApplicationRepository _loanRepository;

    public SubmitLoanApplicationHandler(LoanApplicationRepository loanRepository)
    {
        _loanRepository = loanRepository;
    }

    public Task<Guid> Handle(SubmitLoanApplicationCommand request, CancellationToken cancellationToken)
    {
        var application = new LoanApplication { Id = Guid.NewGuid(), ApplicantId = request.ApplicantId, Amount = request.Amount };
        _loanRepository.Add(application);
        return Task.FromResult(application.Id);
    }
}

public class GetLoanApplicationHandler : IRequestHandler<GetLoanApplicationQuery, LoanApplication?>
{
    private readonly LoanApplicationRepository _loanRepository;

    public GetLoanApplicationHandler(LoanApplicationRepository loanRepository)
    {
        _loanRepository = loanRepository;
    }

    public Task<LoanApplication?> Handle(GetLoanApplicationQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_loanRepository.GetById(request.LoanApplicationId));
    }
}

public class ApproveLoanHandler : IRequestHandler<ApproveLoanCommand, bool>
{
    private readonly LoanApplicationRepository _loanRepository;
    private readonly ApprovalDecisionRepository _approvalRepository;

    public ApproveLoanHandler(LoanApplicationRepository loanRepository, ApprovalDecisionRepository approvalRepository)
    {
        _loanRepository = loanRepository;
        _approvalRepository = approvalRepository;
    }

    public Task<bool> Handle(ApproveLoanCommand request, CancellationToken cancellationToken)
    {
        if (!LoanApprovalPolicy.IsWithinApprovalLimit(request.Amount))
        {
            return Task.FromResult(false);
        }

        _loanRepository.UpdateStatus(request.LoanApplicationId, "Approved");
        _approvalRepository.Add(new ApprovalDecision
        {
            Id = Guid.NewGuid(),
            LoanApplicationId = request.LoanApplicationId,
            Approved = true,
            DecidedBy = request.DecidedBy
        });
        return Task.FromResult(true);
    }
}

public class RejectLoanHandler : IRequestHandler<RejectLoanCommand, bool>
{
    private readonly LoanApplicationRepository _loanRepository;
    private readonly ApprovalDecisionRepository _approvalRepository;

    public RejectLoanHandler(LoanApplicationRepository loanRepository, ApprovalDecisionRepository approvalRepository)
    {
        _loanRepository = loanRepository;
        _approvalRepository = approvalRepository;
    }

    public Task<bool> Handle(RejectLoanCommand request, CancellationToken cancellationToken)
    {
        _loanRepository.UpdateStatus(request.LoanApplicationId, "Rejected");
        _approvalRepository.Add(new ApprovalDecision
        {
            Id = Guid.NewGuid(),
            LoanApplicationId = request.LoanApplicationId,
            Approved = false,
            DecidedBy = request.DecidedBy
        });
        return Task.FromResult(true);
    }
}

public class NotifyApplicantHandler : IRequestHandler<NotifyApplicantCommand, bool>
{
    private readonly NotificationRepository _notificationRepository;

    public NotifyApplicantHandler(NotificationRepository notificationRepository)
    {
        _notificationRepository = notificationRepository;
    }

    public Task<bool> Handle(NotifyApplicantCommand request, CancellationToken cancellationToken)
    {
        _notificationRepository.Add(new Notification { Id = Guid.NewGuid(), LoanApplicationId = request.LoanApplicationId, Message = request.Message });
        return Task.FromResult(true);
    }
}
