namespace BankingBrain.Domain.Entities.Semantics;

public sealed class SemanticEntityType
{
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }

    private SemanticEntityType() { }

    public static SemanticEntityType Create(Guid workspaceId, string code, string displayName, string? description = null)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("Workspace ID is required.", nameof(workspaceId));
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Entity type code and display name are required.");
        return new SemanticEntityType { Id = Guid.NewGuid(), WorkspaceId = workspaceId, Code = code.Trim().ToUpperInvariant(), DisplayName = displayName.Trim(), Description = description?.Trim(), CreatedOnUtc = DateTime.UtcNow };
    }
}
