namespace LoanApprovalBenchmark;

public class Applicant
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public decimal AnnualIncome { get; set; }
}

public class LoanApplication
{
    public Guid Id { get; set; }
    public Guid ApplicantId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Pending";
}

public class ApprovalDecision
{
    public Guid Id { get; set; }
    public Guid LoanApplicationId { get; set; }
    public bool Approved { get; set; }
    public string DecidedBy { get; set; } = string.Empty;
}

public class AuditEntry
{
    public Guid Id { get; set; }
    public Guid LoanApplicationId { get; set; }
    public string Action { get; set; } = string.Empty;
}

// Deliberately has no EF table mapping anywhere in this fixture - exercises the
// "no-explicit-table-mapping" unresolved case for Data Access Intelligence.
public class Notification
{
    public Guid Id { get; set; }
    public Guid LoanApplicationId { get; set; }
    public string Message { get; set; } = string.Empty;
}
