namespace UniversitySchedule.Domain.Catalog;

public sealed class ReferenceCatalogDocument
{
    public const int CurrentDocumentId = 1;

    private ReferenceCatalogDocument()
    {
    }

    private ReferenceCatalogDocument(
        string payloadJson,
        string contentHash,
        int schemaVersion,
        DateTimeOffset sourceGeneratedAtUtc,
        DateTimeOffset importedAtUtc)
    {
        Id = CurrentDocumentId;
        Replace(payloadJson, contentHash, schemaVersion, sourceGeneratedAtUtc, importedAtUtc);
    }

    public int Id { get; private set; }

    public string PayloadJson { get; private set; } = string.Empty;

    public string ContentHash { get; private set; } = string.Empty;

    public int SchemaVersion { get; private set; }

    public DateTimeOffset SourceGeneratedAtUtc { get; private set; }

    public DateTimeOffset ImportedAtUtc { get; private set; }

    public static ReferenceCatalogDocument Create(
        string payloadJson,
        string contentHash,
        int schemaVersion,
        DateTimeOffset sourceGeneratedAtUtc,
        DateTimeOffset importedAtUtc) =>
        new(payloadJson, contentHash, schemaVersion, sourceGeneratedAtUtc, importedAtUtc);

    public void Replace(
        string payloadJson,
        string contentHash,
        int schemaVersion,
        DateTimeOffset sourceGeneratedAtUtc,
        DateTimeOffset importedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        if (contentHash.Length != 64)
        {
            throw new ArgumentException("Catalog content hash must be a SHA-256 hex string.", nameof(contentHash));
        }

        if (schemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        }

        PayloadJson = payloadJson;
        ContentHash = contentHash.ToLowerInvariant();
        SchemaVersion = schemaVersion;
        SourceGeneratedAtUtc = sourceGeneratedAtUtc.ToUniversalTime();
        ImportedAtUtc = importedAtUtc.ToUniversalTime();
    }
}
