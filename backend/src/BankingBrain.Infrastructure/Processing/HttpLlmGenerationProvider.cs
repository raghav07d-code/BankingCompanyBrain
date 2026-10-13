using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BankingBrain.Application.Common.GroundedIntelligence;
using BankingBrain.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace BankingBrain.Infrastructure.Processing;

public sealed class HttpLlmGenerationProvider : ILlmGenerationProvider
{
    private static readonly HashSet<int> TransientStatusCodes = [429, 500, 502, 503, 504];

    private readonly HttpClient _httpClient;
    private readonly LlmOptions _options;

    public HttpLlmGenerationProvider(HttpClient httpClient, IOptions<LlmOptions> options) { _httpClient = httpClient; _options = options.Value; }
    public string Identity => "configured-http";
    public string Version => "1.0";
    public string Model => _options.Model;

    public async Task<LlmGenerationResult> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint)) throw new InvalidOperationException("LLM endpoint is not configured.");

        // One shared deadline for the whole call, retries included: CancelAfter is set
        // once here, not per attempt, so backoff waits and retried requests all draw
        // from the same bounded budget instead of extending the overall timeout.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 300)));

        var maxRetries = Math.Max(0, _options.MaxRetries);
        var baseDelayMilliseconds = Math.Max(1, _options.BaseDelayMilliseconds);

        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint);
            if (!string.IsNullOrWhiteSpace(_options.ApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(new { model = _options.Model, messages = new[] { new { role = "system", content = systemPrompt }, new { role = "user", content = userPrompt } }, temperature = 0 }), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);

            if (response.IsSuccessStatusCode)
            {
                using var json = JsonDocument.Parse(body);
                var content = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
                try
                {
                    var structured = JsonSerializer.Deserialize<ProviderResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (structured is not null) return new LlmGenerationResult(structured.Answer, structured.EvidenceStatus, structured.Citations ?? Array.Empty<string>(), Identity, Version, Model);
                }
                catch (JsonException) { }
                return new LlmGenerationResult(content, "grounded", Array.Empty<string>(), Identity, Version, Model);
            }

            // Only the fixed set of transient provider statuses are retried - permanent/
            // client errors (400/401/403/404/etc.) fail immediately, same as before.
            if (!TransientStatusCodes.Contains((int)response.StatusCode) || attempt >= maxRetries)
                throw new HttpRequestException($"LLM provider returned {(int)response.StatusCode}.", null, response.StatusCode);

            await Task.Delay(ComputeBackoffDelay(baseDelayMilliseconds, attempt), timeout.Token);
        }
    }

    private static TimeSpan ComputeBackoffDelay(int baseDelayMilliseconds, int attempt)
    {
        var exponential = baseDelayMilliseconds * Math.Pow(2, attempt);
        var jitter = Random.Shared.NextDouble() * baseDelayMilliseconds;
        return TimeSpan.FromMilliseconds(exponential + jitter);
    }

    private sealed record ProviderResponse(string Answer, string EvidenceStatus, IReadOnlyList<string>? Citations);
}
