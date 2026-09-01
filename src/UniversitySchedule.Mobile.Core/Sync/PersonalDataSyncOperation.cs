using UniversitySchedule.Mobile.Core.Assignments;
using UniversitySchedule.Mobile.Core.Notes;

namespace UniversitySchedule.Mobile.Core.Sync;

public enum PersonalDataSyncEntityKind
{
    Note = 0,
    Assignment = 1,
}

public enum PersonalDataSyncMutationKind
{
    Upsert = 0,
    Delete = 1,
}

public enum PersonalDataSyncOperationState
{
    Pending = 0,
    Conflict = 1,
    Failed = 2,
}

public sealed record PersonalDataSyncOperation(
    Guid MutationId,
    PersonalDataSyncEntityKind EntityKind,
    PersonalDataSyncMutationKind MutationKind,
    Guid EntityId,
    DateTimeOffset OccurredAtUtc,
    PersonalNote? Note = null,
    PersonalAssignment? Assignment = null,
    PersonalDataSyncOperationState State = PersonalDataSyncOperationState.Pending,
    int AttemptCount = 0,
    DateTimeOffset? LastAttemptAtUtc = null,
    string? LastErrorCode = null,
    string? ConflictServerStateJson = null);
