using UniversitySchedule.Contracts.PersonalData;
using UniversitySchedule.Mobile.Core.Assignments;
using UniversitySchedule.Mobile.Core.Notes;

namespace UniversitySchedule.Mobile.Core.Sync;

public sealed record PersonalDataSnapshotRestoreResult(
    PersonalDataSnapshotDownloadOutcome Outcome,
    int RestoredNoteCount,
    int RestoredAssignmentCount,
    int ProtectedEntityCount,
    DateTimeOffset? SnapshotGeneratedAtUtc = null,
    string? ErrorCode = null);

public sealed class PersonalDataSnapshotRestorer(
    UniversityScheduleApiClient apiClient,
    PersonalDataSyncQueue queue,
    PersonalNoteStore noteStore,
    PersonalAssignmentStore assignmentStore)
{
    private readonly SemaphoreSlim _restoreLock = new(1, 1);

    public async Task<PersonalDataSnapshotRestoreResult> RestoreAsync(
        CancellationToken cancellationToken = default)
    {
        await _restoreLock.WaitAsync(cancellationToken);
        try
        {
            PersonalDataSnapshotDownloadResult download = await apiClient.DownloadSnapshotAsync(cancellationToken);
            if (download.Outcome != PersonalDataSnapshotDownloadOutcome.Succeeded ||
                download.Snapshot is not PersonalDataSnapshotResponse snapshot)
            {
                return new PersonalDataSnapshotRestoreResult(
                    download.Outcome,
                    0,
                    0,
                    0,
                    ErrorCode: download.ErrorCode);
            }

            IReadOnlyList<PersonalDataSyncOperation> operations = await queue.GetPendingAsync(cancellationToken);
            HashSet<Guid> protectedNoteIds = operations
                .Where(operation => operation.EntityKind == PersonalDataSyncEntityKind.Note)
                .Select(operation => operation.EntityId)
                .ToHashSet();
            HashSet<Guid> protectedAssignmentIds = operations
                .Where(operation => operation.EntityKind == PersonalDataSyncEntityKind.Assignment)
                .Select(operation => operation.EntityId)
                .ToHashSet();

            PersonalNote[] activeNotes = snapshot.Notes
                .Where(note => !note.DeletedAtUtc.HasValue)
                .Select(ToPersonalNote)
                .ToArray();
            Guid[] deletedNoteIds = snapshot.Notes
                .Where(note => note.DeletedAtUtc.HasValue)
                .Select(note => note.Id)
                .ToArray();
            PersonalAssignment[] activeAssignments = snapshot.Assignments
                .Where(assignment => !assignment.DeletedAtUtc.HasValue)
                .Select(ToPersonalAssignment)
                .ToArray();
            Guid[] deletedAssignmentIds = snapshot.Assignments
                .Where(assignment => assignment.DeletedAtUtc.HasValue)
                .Select(assignment => assignment.Id)
                .ToArray();

            int restoredNotes = await noteStore.ApplySynchronizationSnapshotAsync(
                activeNotes,
                deletedNoteIds,
                protectedNoteIds,
                cancellationToken);
            int restoredAssignments = await assignmentStore.ApplySynchronizationSnapshotAsync(
                activeAssignments,
                deletedAssignmentIds,
                protectedAssignmentIds,
                cancellationToken);
            return new PersonalDataSnapshotRestoreResult(
                download.Outcome,
                restoredNotes,
                restoredAssignments,
                protectedNoteIds.Count + protectedAssignmentIds.Count,
                snapshot.GeneratedAtUtc.ToUniversalTime());
        }
        finally
        {
            _restoreLock.Release();
        }
    }

    private static PersonalNote ToPersonalNote(SyncedNoteResponse response) =>
        new(
            response.Id,
            response.LessonId,
            response.Text,
            response.CreatedAtUtc,
            response.UpdatedAtUtc,
            response.Title,
            response.Subject,
            response.IsPinned);

    private static PersonalAssignment ToPersonalAssignment(SyncedAssignmentResponse response) =>
        new(
            response.Id,
            response.LessonId,
            response.Subject,
            response.Text,
            response.DeadlineUtc,
            (PersonalAssignmentStatus)response.Status,
            response.CreatedAtUtc,
            response.UpdatedAtUtc);
}
