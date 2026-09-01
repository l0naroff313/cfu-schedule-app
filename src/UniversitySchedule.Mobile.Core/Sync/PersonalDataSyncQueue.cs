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

    public async Task<int> DiscardEntityOperationsAsync(
        PersonalDataSyncEntityKind entityKind,
        Guid entityId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(entityId, Guid.Empty);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            List<PersonalDataSyncOperation> operations = (await GetPendingAsync(cancellationToken)).ToList();
            int removed = operations.RemoveAll(operation =>
                operation.EntityKind == entityKind && operation.EntityId == entityId);
            if (removed > 0)
            {
                await SaveAsync(operations, timeProvider.GetUtcNow(), cancellationToken);
            }

            return removed;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ReplaceEntityOperationsAsync(
        PersonalDataSyncOperation replacement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentOutOfRangeException.ThrowIfEqual(replacement.EntityId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(replacement.MutationId, Guid.Empty);
        if (replacement.State != PersonalDataSyncOperationState.Pending)
        {
            throw new ArgumentException("A replacement sync operation must be pending.", nameof(replacement));
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            List<PersonalDataSyncOperation> operations = (await GetPendingAsync(cancellationToken)).ToList();
            int firstIndex = operations.FindIndex(operation =>
                operation.EntityKind == replacement.EntityKind &&
                operation.EntityId == replacement.EntityId);
            operations.RemoveAll(operation =>
                operation.EntityKind == replacement.EntityKind &&
                operation.EntityId == replacement.EntityId);
            if (firstIndex < 0 || firstIndex > operations.Count)
            {
                operations.Add(replacement);
            }
            else
            {
                operations.Insert(firstIndex, replacement);
            }

            await SaveAsync(operations, timeProvider.GetUtcNow(), cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task RecordRetryAsync(
        Guid mutationId,
        string errorCode,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            mutationId,
            operation => operation with
            {
                State = PersonalDataSyncOperationState.Pending,
                AttemptCount = operation.AttemptCount + 1,
                LastAttemptAtUtc = timeProvider.GetUtcNow().ToUniversalTime(),
                LastErrorCode = errorCode,
            },
            cancellationToken);

    public Task MarkConflictAsync(
        Guid mutationId,
        string errorCode,
        string? serverStateJson = null,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            mutationId,
            operation => operation with
            {
                State = PersonalDataSyncOperationState.Conflict,
                AttemptCount = operation.AttemptCount + 1,
                LastAttemptAtUtc = timeProvider.GetUtcNow().ToUniversalTime(),
                LastErrorCode = errorCode,
                ConflictServerStateJson = serverStateJson,
            },
            cancellationToken);

    public Task MarkFailedAsync(
        Guid mutationId,
        string errorCode,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            mutationId,
            operation => operation with
            {
                State = PersonalDataSyncOperationState.Failed,
                AttemptCount = operation.AttemptCount + 1,
                LastAttemptAtUtc = timeProvider.GetUtcNow().ToUniversalTime(),
                LastErrorCode = errorCode,
            },
            cancellationToken);

    public Task RetryFailedAsync(Guid mutationId, CancellationToken cancellationToken = default) =>
        UpdateAsync(
            mutationId,
            operation => operation.State == PersonalDataSyncOperationState.Failed
                ? operation with
                {
                    State = PersonalDataSyncOperationState.Pending,
                    LastErrorCode = null,
                }
                : throw new InvalidOperationException("Only a failed sync operation can be retried directly."),
            cancellationToken);

    public Task ResolveConflictKeepingLocalAsync(
        Guid mutationId,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            mutationId,
            operation => CreateLocalConflictResolution(operation),
            cancellationToken);

    private PersonalDataSyncOperation CreateLocalConflictResolution(
        PersonalDataSyncOperation operation)
    {
        if (operation.State != PersonalDataSyncOperationState.Conflict)
        {
            throw new InvalidOperationException("Only a conflicted sync operation can be resolved.");
        }

        DateTimeOffset currentTime = timeProvider.GetUtcNow().ToUniversalTime();
        DateTimeOffset resolvedAtUtc = currentTime > operation.OccurredAtUtc
            ? currentTime
            : operation.OccurredAtUtc.AddTicks(1);
        return operation with
        {
            MutationId = Guid.NewGuid(),
            OccurredAtUtc = resolvedAtUtc,
            Note = operation.Note is null
                ? null
                : operation.Note with { UpdatedAtUtc = resolvedAtUtc },
            Assignment = operation.Assignment is null
                ? null
                : operation.Assignment with { UpdatedAtUtc = resolvedAtUtc },
            State = PersonalDataSyncOperationState.Pending,
            AttemptCount = 0,
            LastAttemptAtUtc = null,
            LastErrorCode = null,
            ConflictServerStateJson = null,
        };
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

    private async Task UpdateAsync(
        Guid mutationId,
        Func<PersonalDataSyncOperation, PersonalDataSyncOperation> update,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            List<PersonalDataSyncOperation> operations = (await GetPendingAsync(cancellationToken)).ToList();
            int index = operations.FindIndex(operation => operation.MutationId == mutationId);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Sync mutation '{mutationId:D}' was not found.");
            }

            operations[index] = update(operations[index]);
            await SaveAsync(operations, timeProvider.GetUtcNow(), cancellationToken);
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
