using System.Security.Cryptography;
using System.Text;
using BankingBrain.Application.Common.Embeddings;

namespace BankingBrain.Infrastructure.Processing;

public sealed class DeterministicEmbeddingProvider : IEmbeddingProvider
{
    public string Identity => "deterministic-hash";
    public string Version => "1.0";
    public string Model => "token-hash-64";

    public Task<IReadOnlyList<EmbeddingVector>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken)
    {
        var vectors = inputs.Select(input => new EmbeddingVector(CreateVector(input), Identity, Version, Model)).ToList();
        return Task.FromResult<IReadOnlyList<EmbeddingVector>>(vectors);
    }

    private static IReadOnlyList<float> CreateVector(string input)
    {
        var values = new float[64];
        foreach (var token in input.ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            for (var index = 0; index < values.Length; index++)
                values[index] += (hash[index % hash.Length] / 255f) * (index % 2 == 0 ? 1f : -1f);
        }
        var magnitude = MathF.Sqrt(values.Sum(value => value * value));
        return magnitude == 0 ? values : values.Select(value => value / magnitude).ToArray();
    }
}