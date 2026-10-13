namespace BankingBrain.Application.Common.Interfaces;

public interface IObjectStorage
{
    Task<StoredObject> StoreAndHashAsync(
        string workspaceId,
        string temporaryKey,
        Stream content,
        long maxFileSize,
        CancellationToken cancellationToken);

    Task<bool> MoveAsync(
        string workspaceId,
        string sourceKey,
        string destinationKey,
        CancellationToken cancellationToken);

    Task StoreAsync(
        string workspaceId,
        string storageKey,
        Stream content,
        CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(
        string workspaceId,
        string storageKey,
        CancellationToken cancellationToken);

    Task<bool> ExistsAsync(
        string workspaceId,
        string storageKey,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        string workspaceId,
        string storageKey,
        CancellationToken cancellationToken);
}

public sealed record StoredObject(string ContentHash, long FileSize);