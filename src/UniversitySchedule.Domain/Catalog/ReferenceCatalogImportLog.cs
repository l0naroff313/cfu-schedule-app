namespace UniversitySchedule.Domain.Catalog;

public enum ReferenceCatalogImportStatus
{
    Published = 0,
    Unchanged = 1,
    RejectedAsStale = 2,
    Failed = 3,
}

public sealed class ReferenceCatalogImportLog
{
    private ReferenceCatalogImportLog()
    {
    }

    private ReferenceCatalogImportLog(
        Guid id,
        DateTimeOffset startedAtUtc,
        DateTimeOffset finishedAtUtc,
        ReferenceCatalogImportStatus status,
        string? contentHash,
        int programCount,
        int groupCount,
        int teacherCount,
        string? errorMessage)
    {
        Id = id;
        StartedAtUtc = startedAtUtc.ToUniversalTime();
        FinishedAtUtc = finishedAtUtc.ToUniversalTime();
        Status = status;
        ContentHash = contentHash;
        ProgramCount = programCount;
        GroupCount = groupCount;
        TeacherCount = teacherCount;
        ErrorMessage = errorMessage;
    }

    public Guid Id { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private set; }

    public DateTimeOffset FinishedAtUtc { get; private set; }

    public ReferenceCatalogImportStatus Status { get; private set; }

    public string? ContentHash { get; private set; }

    public int ProgramCount { get; private set; }

    public int GroupCount { get; private set; }

    public int TeacherCount { get; private set; }

    public string? ErrorMessage { get; private set; }

    public static ReferenceCatalogImportLog Create(
        DateTimeOffset startedAtUtc,
        DateTimeOffset finishedAtUtc,
        ReferenceCatalogImportStatus status,
        string contentHash,
        int programCount,
        int groupCount,
        int teacherCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        if (programCount < 0 || groupCount < 0 || teacherCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(programCount), "Import counters cannot be negative.");
        }

        return new ReferenceCatalogImportLog(
            Guid.NewGuid(),
            startedAtUtc,
            finishedAtUtc,
            status,
            contentHash,
            programCount,
            groupCount,
            teacherCount,
            errorMessage: null);
    }

    public static ReferenceCatalogImportLog CreateFailure(
        DateTimeOffset startedAtUtc,
        DateTimeOffset finishedAtUtc,
        string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new ReferenceCatalogImportLog(
            Guid.NewGuid(),
            startedAtUtc,
            finishedAtUtc,
            ReferenceCatalogImportStatus.Failed,
            contentHash: null,
            programCount: 0,
            groupCount: 0,
            teacherCount: 0,
            errorMessage.Trim());
    }
}
