namespace UniversitySchedule.ScheduleImporter.Sources;

public sealed record SourceDocumentVersion(
    string? ETag,
    DateTimeOffset? LastModifiedUtc,
    long? ContentLength)
{
    public bool Matches(SourceDocumentVersion other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (!string.IsNullOrWhiteSpace(ETag) && !string.IsNullOrWhiteSpace(other.ETag))
        {
            return string.Equals(ETag, other.ETag, StringComparison.Ordinal);
        }

        if (LastModifiedUtc is null || other.LastModifiedUtc is null ||
            ContentLength is null || other.ContentLength is null)
        {
            return false;
        }

        return LastModifiedUtc.Value.ToUniversalTime() == other.LastModifiedUtc.Value.ToUniversalTime() &&
            ContentLength == other.ContentLength;
    }
}
