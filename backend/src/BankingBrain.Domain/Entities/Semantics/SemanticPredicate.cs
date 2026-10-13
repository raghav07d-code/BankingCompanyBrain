namespace BankingBrain.Domain.Entities.Semantics;

public sealed class SemanticPredicate
{
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public bool AllowsEntityObject { get; private set; }
    public string AllowedSubjectTypes { get; private set; } = string.Empty;
    public string AllowedObjectTypes { get; private set; } = string.Empty;
    public DateTime CreatedOnUtc { get; private set; }

    private SemanticPredicate() { }

    public static SemanticPredicate Create(Guid workspaceId, string code, string displayName, bool allowsEntityObject, IEnumerable<string> allowedSubjectTypes, IEnumerable<string>? allowedObjectTypes = null)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("Workspace ID is required.", nameof(workspaceId));
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Predicate code and display name are required.");
        return new SemanticPredicate { Id = Guid.NewGuid(), WorkspaceId = workspaceId, Code = code.Trim().ToUpperInvariant(), DisplayName = displayName.Trim(), AllowsEntityObject = allowsEntityObject, AllowedSubjectTypes = string.Join('\n', allowedSubjectTypes.Select(type => type.Trim().ToUpperInvariant()).Distinct()), AllowedObjectTypes = string.Join('\n', (allowedObjectTypes ?? Array.Empty<string>()).Select(type => type.Trim().ToUpperInvariant()).Distinct()), CreatedOnUtc = DateTime.UtcNow };
    }

    public bool AllowsSubject(string entityType) => AllowedSubjectTypes.Split('\n', StringSplitOptions.RemoveEmptyEntries).Contains(entityType.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase);
    public bool AllowsObject(string? entityType) => !AllowsEntityObject || (entityType is not null && AllowedObjectTypes.Split('\n', StringSplitOptions.RemoveEmptyEntries).Contains(entityType.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase));
}
