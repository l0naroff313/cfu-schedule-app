using System.Text.Json;
using UniversitySchedule.Mobile.Core.Assignments;
using UniversitySchedule.Mobile.Core.Notes;
using UniversitySchedule.Mobile.Core.Storage;

namespace UniversitySchedule.Mobile.Core.Sync;

public sealed class PersonalDataSyncQueue(
    ILocalDataStore localDataStore,
    TimeProvider timeProvider)
{
    private const string StorageKey = "personal-data-sync-queue:v1";
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<IReadOnlyList<PersonalDataSyncOperation>> GetPendingAsync(
        CancellationToken cancellationToken = default)
    {
        LocalDocument? document = await localDataStore.GetAsync(StorageKey, cancellationToken);
        return document is null
            ? []
            : JsonSerializer.Deserialize<IReadOnlyList<PersonalDataSyncOperation>>(document.Content) ?? [];
    }

    public Task EnqueueNoteUpsertAsync(
        PersonalNote note,
        CancellationToken cancellationToken = default) =>
        EnqueueAsync(
            new PersonalDataSyncOperation(
                Guid.NewGuid(),
                PersonalDataSyncEntityKind.Note,
                PersonalDataSyncMutationKind.Upsert,
                note.Id,
                note.UpdatedAtUtc,
                Note: note),
            cancellationToken);

    public Task EnqueueNoteDeleteAsync(
        Guid noteId,
        DateTimeOffset deletedAtUtc,
        CancellationToken cancellationToken = default) =>
        EnqueueAsync(
            new PersonalDataSyncOperation(
                Guid.NewGuid(),
                PersonalDataSyncEntityKind.Note,
                PersonalDataSyncMutationKind.Delete,
                noteId,
                deletedAtUtc),
            cancellationToken);

    public Task EnqueueAssignmentUpsertAsync(
        PersonalAssignment assignment,
        CancellationToken cancellationToken = default) =>
        EnqueueAsync(
            new PersonalDataSyncOperation(
                Guid.NewGuid(),
                PersonalDataSyncEntityKind.Assignment,
                PersonalDataSyncMutationKind.Upsert,
                assignment.Id,
                assignment.UpdatedAtUtc,
                Assignment: assignment),
            cancellationToken);

    public Task EnqueueAssignmentDeleteAsync(
        Guid assignmentId,
        DateTimeOffset deletedAtUtc,
        CancellationToken cancellationToken = default) =>
        EnqueueAsync(
            new PersonalDataSyncOperation(
                Guid.NewGuid(),
                PersonalDataSyncEntityKind.Assignment,
                PersonalDataSyncMutationKind.Delete,
                assignmentId,
                deletedAtUtc),
            cancellationToken);

    public async Task RemoveAsync(Guid mutationId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            List<PersonalDataSyncOperation> operations = (await GetPendingAsync(cancellationToken)).ToList();
            if (operations.RemoveAll(operation => operation.MutationId == mutationId) > 0)
            {
                await SaveAsync(operations, timeProvider.GetUtcNow(), cancellationToken);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task EnqueueAsync(
        PersonalDataSyncOperation operation,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            List<PersonalDataSyncOperation> operations = (await GetPendingAsync(cancellationToken)).ToList();
            operations.Add(operation);
            await SaveAsync(operations, operation.OccurredAtUtc, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private Task SaveAsync(
        IReadOnlyCollection<PersonalDataSyncOperation> operations,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken) =>
        localDataStore.SaveAsync(
            new LocalDocument(StorageKey, JsonSerializer.Serialize(operations), updatedAtUtc),
            cancellationToken);
}
