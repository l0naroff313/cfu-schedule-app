namespace UniversitySchedule.Domain.PersonalData;

public enum SyncedAssignmentStatus
{
    New = 0,
    InProgress = 1,
    Completed = 2,
}

public sealed class SyncedAssignment
{
    private SyncedAssignment()
    {
    }

    private SyncedAssignment(
        Guid installationId,
        Guid id,
        Guid mutationId,
        Guid? lessonId,
        string subject,
        string text,
        DateTimeOffset? deadlineUtc,
        SyncedAssignmentStatus status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset clientUpdatedAtUtc,
        DateTimeOffset serverUpdatedAtUtc,
        DateTimeOffset? deletedAtUtc)
    {
        InstallationId = installationId;
        Id = id;
        LastMutationId = mutationId;
        LessonId = lessonId;
        Subject = subject;
        Text = text;
        DeadlineUtc = deadlineUtc?.ToUniversalTime();
        Status = status;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        ClientUpdatedAtUtc = clientUpdatedAtUtc.ToUniversalTime();
        ServerUpdatedAtUtc = serverUpdatedAtUtc.ToUniversalTime();
        DeletedAtUtc = deletedAtUtc?.ToUniversalTime();
        Revision = 1;
    }

    public Guid InstallationId { get; private set; }

    public Guid Id { get; private set; }

    public Guid LastMutationId { get; private set; }

    public Guid? LessonId { get; private set; }

    public string Subject { get; private set; } = string.Empty;

    public string Text { get; private set; } = string.Empty;

    public DateTimeOffset? DeadlineUtc { get; private set; }

    public SyncedAssignmentStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ClientUpdatedAtUtc { get; private set; }

    public DateTimeOffset ServerUpdatedAtUtc { get; private set; }

    public DateTimeOffset? DeletedAtUtc { get; private set; }

    public long Revision { get; private set; }

    public bool IsDeleted => DeletedAtUtc.HasValue;

    public static SyncedAssignment Create(
        Guid installationId,
        Guid id,
        Guid mutationId,
        Guid? lessonId,
        string subject,
        string text,
        DateTimeOffset? deadlineUtc,
        SyncedAssignmentStatus status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset clientUpdatedAtUtc,
        DateTimeOffset serverUpdatedAtUtc)
    {
        Validate(installationId, id, mutationId, text, status);
        return new SyncedAssignment(
            installationId,
            id,
            mutationId,
            lessonId,
            subject,
            text,
            deadlineUtc,
            status,
            createdAtUtc,
            clientUpdatedAtUtc,
            serverUpdatedAtUtc,
            null);
    }

    public static SyncedAssignment CreateTombstone(
        Guid installationId,
        Guid id,
        Guid mutationId,
        DateTimeOffset deletedAtUtc,
        DateTimeOffset serverUpdatedAtUtc)
    {
        ValidateIds(installationId, id, mutationId);
        return new SyncedAssignment(
            installationId,
            id,
            mutationId,
            null,
            string.Empty,
            string.Empty,
            null,
            SyncedAssignmentStatus.New,
            deletedAtUtc,
            deletedAtUtc,
            serverUpdatedAtUtc,
            deletedAtUtc);
    }

    public bool Apply(
        Guid mutationId,
        Guid? lessonId,
        string subject,
        string text,
        DateTimeOffset? deadlineUtc,
        SyncedAssignmentStatus status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset clientUpdatedAtUtc,
        DateTimeOffset serverUpdatedAtUtc)
    {
        Validate(InstallationId, Id, mutationId, text, status);
        DateTimeOffset normalizedClientTime = clientUpdatedAtUtc.ToUniversalTime();
        if (mutationId == LastMutationId || normalizedClientTime < ClientUpdatedAtUtc)
        {
            return false;
        }

        LastMutationId = mutationId;
        LessonId = lessonId;
        Subject = subject;
        Text = text;
        DeadlineUtc = deadlineUtc?.ToUniversalTime();
        Status = status;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        ClientUpdatedAtUtc = normalizedClientTime;
        ServerUpdatedAtUtc = serverUpdatedAtUtc.ToUniversalTime();
        DeletedAtUtc = null;
        Revision++;
        return true;
    }

    public bool Delete(
        Guid mutationId,
        DateTimeOffset deletedAtUtc,
        DateTimeOffset serverUpdatedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(mutationId, Guid.Empty);
        DateTimeOffset normalizedDeletedAt = deletedAtUtc.ToUniversalTime();
        if (mutationId == LastMutationId || normalizedDeletedAt < ClientUpdatedAtUtc)
        {
            return false;
        }

        LastMutationId = mutationId;
        ClientUpdatedAtUtc = normalizedDeletedAt;
        ServerUpdatedAtUtc = serverUpdatedAtUtc.ToUniversalTime();
        DeletedAtUtc = normalizedDeletedAt;
        Revision++;
        return true;
    }

    private static void Validate(
        Guid installationId,
        Guid id,
        Guid mutationId,
        string text,
        SyncedAssignmentStatus status)
    {
        ValidateIds(installationId, id, mutationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }
    }

    private static void ValidateIds(Guid installationId, Guid id, Guid mutationId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(installationId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(mutationId, Guid.Empty);
    }
}
