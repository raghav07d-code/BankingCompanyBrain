namespace BankingBrain.Infrastructure.Configuration;

public sealed class LlmOptions
{
    public const string SectionName = "Llm";
    public string Provider { get; set; } = "deterministic";
    public string Model { get; set; } = "deterministic";
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxContextCharacters { get; set; } = 12000;

    /// <summary>
    /// Additional attempts after the initial one, applied only to transient provider
    /// statuses (429/500/502/503/504) - never to permanent/client errors.
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// Base delay for exponential backoff between retries (doubled per attempt, plus
    /// jitter). All retries and their delays still occur within the single overall
    /// TimeoutSeconds budget - they never extend it.
    /// </summary>
    public int BaseDelayMilliseconds { get; set; } = 500;
}