namespace BankingBrain.Domain.Entities.ProcessingAttempt;

public sealed class ProcessingAttempt
{
    public Guid Id { get; private set; }

    public Guid DocumentVersionId { get; private set; }

    public int AttemptNumber { get; private set; }

    public DateTime StartedOnUtc { get; private set; }

    public DateTime? CompletedOnUtc { get; private set; }

    public string? ProcessorIdentity { get; private set; }

    public string? ProcessorVersion { get; private set; }

    public bool? Succeeded { get; private set; }

    public string? Error { get; private set; }

    private ProcessingAttempt()
    {
    }

    private ProcessingAttempt(
        Guid id,
        Guid documentVersionId,
        int attemptNumber,
        string? processorIdentity)
    {
        Id = id;
        DocumentVersionId = documentVersionId;
        AttemptNumber = attemptNumber;
        ProcessorIdentity = processorIdentity;
        StartedOnUtc = DateTime.UtcNow;
    }

    public static ProcessingAttempt Start(
        Guid documentVersionId,
        int attemptNumber,
        string? processorIdentity)
    {
        if (documentVersionId == Guid.Empty)
            throw new ArgumentException("Version ID is required.", nameof(documentVersionId));
        if (attemptNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(attemptNumber));

        return new ProcessingAttempt(
            Guid.NewGuid(),
            documentVersionId,
            attemptNumber,
            processorIdentity);
    }

    public void Complete(
        bool succeeded,
        string? processorVersion,
        string? error)
    {
        CompletedOnUtc = DateTime.UtcNow;
        Succeeded = succeeded;
        ProcessorVersion = processorVersion;
        Error = error;
    }
}