using MediatR;

namespace LoanApprovalBenchmark;

public static class LoanEndpoints
{
    public static void MapLoanEndpoints(EndpointRouteBuilderStub endpoints, ISender sender)
    {
        endpoints.MapPost("/api/loans/submit", (SubmitLoanApplicationCommand command, CancellationToken ct) =>
            sender.Send<Guid>(command, ct));

        endpoints.MapGet("/api/loans/{id}", (Guid id, CancellationToken ct) =>
            sender.Send<LoanApplication?>(new GetLoanApplicationQuery(id), ct));

        endpoints.MapPost("/api/loans/{id}/approve", (Guid id, ApproveLoanCommand body, CancellationToken ct) =>
            sender.Send<bool>(new ApproveLoanCommand(id, body.Amount, body.DecidedBy), ct));

        endpoints.MapPost("/api/loans/{id}/reject", (Guid id, RejectLoanCommand body, CancellationToken ct) =>
            sender.Send<bool>(new RejectLoanCommand(id, body.DecidedBy), ct));

        endpoints.MapPost("/api/loans/{id}/notify", (Guid id, NotifyApplicantCommand body, CancellationToken ct) =>
            sender.Send<bool>(new NotifyApplicantCommand(id, body.Message), ct));
    }
}
