namespace LoanApprovalBenchmark;

public class LoanApplicationRepository
{
    private readonly LoanDbContext _db;

    public LoanApplicationRepository(LoanDbContext db)
    {
        _db = db;
    }

    public void Add(LoanApplication application)
    {
        _db.LoanApplications.Add(application);
    }

    public LoanApplication? GetById(Guid id)
    {
        return null;
    }

    public void UpdateStatus(Guid id, string status)
    {
        _db.LoanApplications.Add(new LoanApplication { Id = id, Status = status });
    }
}

public class ApprovalDecisionRepository
{
    private readonly LoanDbContext _db;

    public ApprovalDecisionRepository(LoanDbContext db)
    {
        _db = db;
    }

    public void Add(ApprovalDecision decision)
    {
        _db.ApprovalDecisions.Add(decision);
    }
}

public class AuditRepository
{
    private readonly LoanDbContext _db;

    public AuditRepository(LoanDbContext db)
    {
        _db = db;
    }

    public void Add(AuditEntry entry)
    {
        _db.AuditEntries.Add(entry);
    }
}

public class NotificationRepository
{
    private readonly LoanDbContext _db;

    public NotificationRepository(LoanDbContext db)
    {
        _db = db;
    }

    public void Add(Notification notification)
    {
        // Deliberately accesses an unmapped DbSet - no ToTable configuration exists
        // for Notification anywhere in this fixture.
        _db.Notifications.Add(notification);
    }
}
