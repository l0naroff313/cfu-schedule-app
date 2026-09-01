namespace UniversitySchedule.Contracts.PersonalData;

public enum SyncMutationDisposition
{
    Applied = 0,
    AlreadyApplied = 1,
    Conflict = 2,
}

public sealed record SyncedNoteResponse(
    Guid Id,
    Guid? LessonId,
    string Text,
    string? Title,
    string? Subject,
    bool IsPinned,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset ServerUpdatedAtUtc,
    DateTimeOffset? DeletedAtUtc,
    long Revision,
    bool WasApplied,
    SyncMutationDisposition Disposition);

public sealed record SyncedAssignmentResponse(
    Guid Id,
    Guid? LessonId,
    string Subject,
    string Text,
    DateTimeOffset? DeadlineUtc,
    AssignmentSyncStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset ServerUpdatedAtUtc,
    DateTimeOffset? DeletedAtUtc,
    long Revision,
    bool WasApplied,
    SyncMutationDisposition Disposition);
