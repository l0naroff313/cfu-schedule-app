using UniversitySchedule.Domain.PersonalData;

namespace UniversitySchedule.Application.PersonalData;

public sealed record NoteSyncCommand(
    Guid InstallationId,
    Guid NoteId,
    Guid MutationId,
    Guid? LessonId,
    string Text,
    string? Title,
    string? Subject,
    bool IsPinned,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AssignmentSyncCommand(
    Guid InstallationId,
    Guid AssignmentId,
    Guid MutationId,
    Guid? LessonId,
    string Subject,
    string Text,
    DateTimeOffset? DeadlineUtc,
    SyncedAssignmentStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record DeletePersonalDataCommand(
    Guid InstallationId,
    Guid EntityId,
    Guid MutationId,
    DateTimeOffset DeletedAtUtc);

public sealed record PersonalDataSyncResult<T>(T Entity, bool WasApplied);
