using BankingBrain.Domain.Enums;

namespace BankingBrain.Domain.Entities.DocumentVersion;

public sealed class DocumentVersion
{
    public Guid Id { get; private set; }

    public Guid DocumentId { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public int VersionNumber { get; private set; }

    public string ContentHash { get; private set; } = string.Empty;

    public string MediaType { get; private set; } = string.Empty;

    public string FileName { get; private set; } = string.Empty;

    public string StorageKey { get; private set; } = string.Empty;

    public long FileSize { get; private set; }

    public string? RawContentLocation { get; private set; }

    public string? ExtractedText { get; private set; }

    public DocumentProcessingState ProcessingState { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime? ProcessingStartedOnUtc { get; private set; }

    public DateTime? ProcessingCompletedOnUtc { get; private set; }

    public string? ProcessingError { get; private set; }

    public string? ProcessorVersion { get; private set; }

    private DocumentVersion()
    {
    }

    private DocumentVersion(
        Guid id,
        Guid documentId,
        int versionNumber,
        string contentHash,
        string mediaType,
        string fileName,
        string storageKey,
        long fileSize,
        string? rawContentLocation,
        string? extractedText)
    {
        Id = id;
        DocumentId = documentId;
        VersionNumber = versionNumber;
        ContentHash = contentHash;
        MediaType = mediaType;
        FileName = fileName;
        StorageKey = storageKey;
        FileSize = fileSize;
        RawContentLocation = rawContentLocation;
        ExtractedText = extractedText;
        ProcessingState = DocumentProcessingState.Pending;
        CreatedOnUtc = DateTime.UtcNow;
    }

    public static DocumentVersion Create(
        Guid documentId,
        int versionNumber,
        string contentHash,
        string? rawContentLocation,
        string? extractedText)
    {
        return Create(
            documentId,
            versionNumber,
            contentHash,
            "application/octet-stream",
            "document.bin",
            rawContentLocation ?? $"legacy/{documentId:N}/{versionNumber}",
            0,
            rawContentLocation,
            extractedText);
    }

    public static DocumentVersion Create(
        Guid documentId,
        int versionNumber,
        string contentHash,
        string mediaType,
        string fileName,
        string storageKey,
        long fileSize,
        string? rawContentLocation,
        string? extractedText)
    {
        if (documentId == Guid.Empty)
            throw new ArgumentException(
                "Document ID is required.",
                nameof(documentId));

        if (versionNumber < 1)
            throw new ArgumentOutOfRangeException(
                nameof(versionNumber));

        if (string.IsNullOrWhiteSpace(contentHash))
            throw new ArgumentException(
                "Content hash is required.",
                nameof(contentHash));

        if (string.IsNullOrWhiteSpace(mediaType))
            throw new ArgumentException("Media type is required.", nameof(mediaType));

        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name is required.", nameof(fileName));

        if (string.IsNullOrWhiteSpace(storageKey))
            throw new ArgumentException("Storage key is required.", nameof(storageKey));

        if (fileSize < 0)
            throw new ArgumentOutOfRangeException(nameof(fileSize));

        return new DocumentVersion(
            Guid.NewGuid(),
            documentId,
            versionNumber,
            contentHash.Trim(),
            mediaType.Trim(),
            fileName.Trim(),
            storageKey.Trim(),
            fileSize,
            string.IsNullOrWhiteSpace(rawContentLocation)
                ? null
                : rawContentLocation.Trim(),
            extractedText);
    }

    public void BeginProcessing()
    {
        if (ProcessingState == DocumentProcessingState.Processed)
            throw new InvalidOperationException("Processed versions require explicit reprocessing.");

        ProcessingState = DocumentProcessingState.Processing;
        ProcessingStartedOnUtc = DateTime.UtcNow;
        ProcessingCompletedOnUtc = null;
        ProcessingError = null;
    }

    public void SetWorkspace(Guid workspaceId)
    {
        if (workspaceId == Guid.Empty)
            throw new ArgumentException("Workspace ID is required.", nameof(workspaceId));
        WorkspaceId = workspaceId;
    }

    public void MarkProcessed(string? processorVersion)
    {
        if (ProcessingState != DocumentProcessingState.Processing)
            throw new InvalidOperationException("Only processing versions can be completed.");

        ProcessingState = DocumentProcessingState.Processed;
        ProcessingCompletedOnUtc = DateTime.UtcNow;
        ProcessorVersion = processorVersion;
        ProcessingError = null;
    }

    public void MarkFailed(string error, string? processorVersion)
    {
        ProcessingState = DocumentProcessingState.Failed;
        ProcessingCompletedOnUtc = DateTime.UtcNow;
        ProcessingError = error;
        ProcessorVersion = processorVersion;
    }

    public void ResetForReprocessing()
    {
        if (ProcessingState != DocumentProcessingState.Failed &&
            ProcessingState != DocumentProcessingState.Processed)
            throw new InvalidOperationException("Only failed or explicitly outdated versions can be reprocessed.");

        ProcessingState = DocumentProcessingState.Pending;
        ProcessingStartedOnUtc = null;
        ProcessingCompletedOnUtc = null;
        ProcessingError = null;
    }

    public void RecoverInterruptedProcessing()
    {
        if (ProcessingState != DocumentProcessingState.Processing)
            return;

        ProcessingState = DocumentProcessingState.Pending;
        ProcessingStartedOnUtc = null;
    }
}