namespace UniversitySchedule.Contracts.PersonalData;

public sealed record PersonalDataSnapshotResponse(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<SyncedNoteResponse> Notes,
    IReadOnlyList<SyncedAssignmentResponse> Assignments);
