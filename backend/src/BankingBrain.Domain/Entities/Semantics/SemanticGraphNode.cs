namespace BankingBrain.Domain.Entities.Semantics;

public sealed class SemanticGraphNode
{
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public Guid EntityId { get; private set; }
    public DateTime ProjectedOnUtc { get; private set; }

    private SemanticGraphNode() { }

    public static SemanticGraphNode Create(Guid workspaceId, Guid entityId) => new()
    {
        Id = Guid.NewGuid(), WorkspaceId = workspaceId, EntityId = entityId, ProjectedOnUtc = DateTime.UtcNow
    };
}