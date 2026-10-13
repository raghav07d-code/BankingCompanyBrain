namespace LoanApprovalBenchmark;

public static class LoanApprovalPolicy
{
    public const decimal ApprovalLimit = 50000m;

    public static bool IsWithinApprovalLimit(decimal amount) => amount <= ApprovalLimit;
}
