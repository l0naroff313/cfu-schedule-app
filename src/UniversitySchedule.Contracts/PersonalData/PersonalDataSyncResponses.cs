namespace UniversitySchedule.Contracts.PersonalData;

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
    bool WasApplied);

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
    bool WasApplied);
