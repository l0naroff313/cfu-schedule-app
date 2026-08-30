namespace UniversitySchedule.Domain.PersonalData;

public sealed class SyncedNote
{
    private SyncedNote()
    {
    }

    private SyncedNote(
        Guid installationId,
        Guid id,
        Guid mutationId,
        Guid? lessonId,
        string text,
        string? title,
        string? subject,
        bool isPinned,
        DateTimeOffset createdAtUtc,
        DateTimeOffset clientUpdatedAtUtc,
        DateTimeOffset serverUpdatedAtUtc,
        DateTimeOffset? deletedAtUtc)
    {
        InstallationId = installationId;
        Id = id;
        LastMutationId = mutationId;
        LessonId = lessonId;
        Text = text;
        Title = title;
        Subject = subject;
        IsPinned = isPinned;
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

    public string Text { get; private set; } = string.Empty;

    public string? Title { get; private set; }

    public string? Subject { get; private set; }

    public bool IsPinned { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ClientUpdatedAtUtc { get; private set; }

    public DateTimeOffset ServerUpdatedAtUtc { get; private set; }

    public DateTimeOffset? DeletedAtUtc { get; private set; }

    public long Revision { get; private set; }

    public bool IsDeleted => DeletedAtUtc.HasValue;

    public static SyncedNote Create(
        Guid installationId,
        Guid id,
        Guid mutationId,
        Guid? lessonId,
        string text,
        string? title,
        string? subject,
        bool isPinned,
        DateTimeOffset createdAtUtc,
        DateTimeOffset clientUpdatedAtUtc,
        DateTimeOffset serverUpdatedAtUtc)
    {
        ValidateIds(installationId, id, mutationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new SyncedNote(
            installationId,
            id,
            mutationId,
            lessonId,
            text,
            title,
            subject,
            isPinned,
            createdAtUtc,
            clientUpdatedAtUtc,
            serverUpdatedAtUtc,
            null);
    }

    public static SyncedNote CreateTombstone(
        Guid installationId,
        Guid id,
        Guid mutationId,
        DateTimeOffset deletedAtUtc,
        DateTimeOffset serverUpdatedAtUtc)
    {
        ValidateIds(installationId, id, mutationId);
        return new SyncedNote(
            installationId,
            id,
            mutationId,
            null,
            string.Empty,
            null,
            null,
            false,
            deletedAtUtc,
            deletedAtUtc,
            serverUpdatedAtUtc,
            deletedAtUtc);
    }

    public bool Apply(
        Guid mutationId,
        Guid? lessonId,
        string text,
        string? title,
        string? subject,
        bool isPinned,
        DateTimeOffset createdAtUtc,
        DateTimeOffset clientUpdatedAtUtc,
        DateTimeOffset serverUpdatedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(mutationId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        DateTimeOffset normalizedClientTime = clientUpdatedAtUtc.ToUniversalTime();
        if (mutationId == LastMutationId || normalizedClientTime < ClientUpdatedAtUtc)
        {
            return false;
        }

        LastMutationId = mutationId;
        LessonId = lessonId;
        Text = text;
        Title = title;
        Subject = subject;
        IsPinned = isPinned;
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

    private static void ValidateIds(Guid installationId, Guid id, Guid mutationId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(installationId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(mutationId, Guid.Empty);
    }
}
