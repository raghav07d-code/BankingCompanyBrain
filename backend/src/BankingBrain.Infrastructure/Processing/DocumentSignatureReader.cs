using BankingBrain.Application.Common.Interfaces;

namespace BankingBrain.Infrastructure.Processing;

internal static class DocumentSignatureReader
{
    public static async Task<ReadOnlyMemory<byte>> ReadAsync(
        IObjectStorage storage,
        string workspaceId,
        string storageKey,
        CancellationToken cancellationToken)
    {
            try
            {
        await using var content = await storage.OpenReadAsync(
            workspaceId,
            storageKey,
            cancellationToken);
                var buffer = new byte[16];
                var offset = 0;
                while (offset < buffer.Length)
                {
                    var read = await content.ReadAsync(
                        buffer.AsMemory(offset, buffer.Length - offset),
                        cancellationToken);
                    if (read == 0)
                        break;
                    offset += read;
                }

                return buffer.AsMemory(0, offset);
            }
            catch (FileNotFoundException)
            {
                return ReadOnlyMemory<byte>.Empty;
            }
            catch (DirectoryNotFoundException)
            {
                return ReadOnlyMemory<byte>.Empty;
            }
    }
}
