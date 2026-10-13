namespace BankingBrain.Domain.Enums;

public enum KnowledgeSourceRefreshRunStatus
{
    Queued = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
    Superseded = 5
}
