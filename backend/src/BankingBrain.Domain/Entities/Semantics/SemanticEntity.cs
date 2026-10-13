namespace BankingBrain.Domain.Entities.Semantics;

public sealed class SemanticEntity
{
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public string EntityType { get; private set; } = string.Empty;
    public string CanonicalName { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string? Aliases { get; private set; }
    public string? ExternalIdentifier { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }

    private SemanticEntity() { }

    private SemanticEntity(Guid workspaceId, string entityType, string canonicalName, string? externalIdentifier)
    {
        Id = Guid.NewGuid();
        WorkspaceId = workspaceId;
        EntityType = entityType.Trim();
        CanonicalName = canonicalName.Trim();
        NormalizedName = Normalize(canonicalName);
            ExternalIdentifier = string.IsNullOrWhiteSpace(externalIdentifier) ? null : externalIdentifier.Trim();
        CreatedOnUtc = DateTime.UtcNow;
    }

    public static SemanticEntity Create(Guid workspaceId, string entityType, string canonicalName, string? externalIdentifier = null)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("Workspace ID is required.", nameof(workspaceId));
        if (string.IsNullOrWhiteSpace(entityType)) throw new ArgumentException("Entity type is required.", nameof(entityType));
        if (string.IsNullOrWhiteSpace(canonicalName)) throw new ArgumentException("Canonical name is required.", nameof(canonicalName));
        return new SemanticEntity(workspaceId, entityType, canonicalName, externalIdentifier);
    }

    public void AddAlias(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias)) return;
        var values = (Aliases ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (!values.Contains(alias.Trim(), StringComparer.OrdinalIgnoreCase) &&
            !NormalizedName.Equals(Normalize(alias), StringComparison.Ordinal))
            values.Add(alias.Trim());
        Aliases = string.Join('\n', values);
    }

    public static string Normalize(string value)
    {
        var normalized = new string(value.Trim().ToUpperInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : ' ')
            .ToArray());
        var words = normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        while (words.Count > 1 && words[^1] is "INC" or "INCORPORATED" or "LLC" or "LTD" or "LIMITED" or "CORP" or "CORPORATION" or "PLC")
            words.RemoveAt(words.Count - 1);
        return string.Join(' ', words);
    }
}
