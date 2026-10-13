namespace BankingBrain.Application.Common.Connectors.Reconciliation;

public sealed record GitReconciliationResult(
    Guid WorkspaceId,
    Guid KnowledgeSourceId,
    string Revision,
    int CreatedDocuments,
    int CreatedVersions,
    int UnchangedArtifacts,
    int ReactivatedDocuments,
    int DeactivatedDocuments,
    int SkippedArtifacts);
