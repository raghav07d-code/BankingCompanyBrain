namespace BankingBrain.Infrastructure.Processing;

public sealed class ProcessingOptions
{
    public const string SectionName = "Processing";

    public int MaxAttempts { get; set; } = 3;

    public int RetryDelaySeconds { get; set; } = 5;

    public int LeaseMinutes { get; set; } = 2;

    public int PollMilliseconds { get; set; } = 250;
}