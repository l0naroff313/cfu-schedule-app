namespace UniversitySchedule.Domain.Scheduling;

public sealed class ScheduleSourceDocument
{
    private ScheduleSourceDocument()
    {
    }

    private ScheduleSourceDocument(
        string key,
        string sourceUrl,
        string payloadJson,
        DateTimeOffset fetchedAtUtc)
    {
        Key = NormalizeRequired(key, nameof(key));
        Replace(sourceUrl, payloadJson, fetchedAtUtc);
    }

    public string Key { get; private set; } = string.Empty;

    public string SourceUrl { get; private set; } = string.Empty;

    public string PayloadJson { get; private set; } = string.Empty;

    public DateTimeOffset FetchedAtUtc { get; private set; }

    public static ScheduleSourceDocument Create(
        string key,
        string sourceUrl,
        string payloadJson,
        DateTimeOffset fetchedAtUtc) =>
        new(key, sourceUrl, payloadJson, fetchedAtUtc);

    public void Replace(string sourceUrl, string payloadJson, DateTimeOffset fetchedAtUtc)
    {
        SourceUrl = NormalizeRequired(sourceUrl, nameof(sourceUrl));
        PayloadJson = NormalizeRequired(payloadJson, nameof(payloadJson));
        FetchedAtUtc = fetchedAtUtc.ToUniversalTime();
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}
