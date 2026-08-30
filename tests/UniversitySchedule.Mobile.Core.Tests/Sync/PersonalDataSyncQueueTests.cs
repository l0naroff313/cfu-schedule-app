using UniversitySchedule.Mobile.Core.Assignments;
using UniversitySchedule.Mobile.Core.Notes;
using UniversitySchedule.Mobile.Core.Storage;
using UniversitySchedule.Mobile.Core.Sync;

namespace UniversitySchedule.Mobile.Core.Tests.Sync;

public sealed class PersonalDataSyncQueueTests
{
    [Fact]
    public async Task Queue_PreservesOfflineMutationsInOrderAndRemovesOnlyAcknowledgedItem()
    {
        var dataStore = new InMemoryLocalDataStore();
        DateTimeOffset now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        var queue = new PersonalDataSyncQueue(dataStore, new FixedTimeProvider(now));
        var note = new PersonalNote(Guid.NewGuid(), null, "Текст", now, now);
        var assignment = new PersonalAssignment(
            Guid.NewGuid(),
            null,
            "Предмет",
            "Задание",
            null,
            PersonalAssignmentStatus.New,
            now,
            now);

        await queue.EnqueueNoteUpsertAsync(note);
        await queue.EnqueueNoteDeleteAsync(note.Id, now.AddMinutes(1));
        await queue.EnqueueAssignmentUpsertAsync(assignment);
        await queue.EnqueueAssignmentDeleteAsync(assignment.Id, now.AddMinutes(2));
        IReadOnlyList<PersonalDataSyncOperation> pending = await queue.GetPendingAsync();

        Assert.Collection(
            pending,
            operation =>
            {
                Assert.Equal(PersonalDataSyncEntityKind.Note, operation.EntityKind);
                Assert.Equal(PersonalDataSyncMutationKind.Upsert, operation.MutationKind);
                Assert.Equal(note, operation.Note);
            },
            operation =>
            {
                Assert.Equal(PersonalDataSyncEntityKind.Note, operation.EntityKind);
                Assert.Equal(PersonalDataSyncMutationKind.Delete, operation.MutationKind);
            },
            operation =>
            {
                Assert.Equal(PersonalDataSyncEntityKind.Assignment, operation.EntityKind);
                Assert.Equal(PersonalDataSyncMutationKind.Upsert, operation.MutationKind);
                Assert.Equal(assignment, operation.Assignment);
            },
            operation => Assert.Equal(PersonalDataSyncMutationKind.Delete, operation.MutationKind));

        await queue.RemoveAsync(pending[0].MutationId);
        var reloadedQueue = new PersonalDataSyncQueue(dataStore, new FixedTimeProvider(now));
        IReadOnlyList<PersonalDataSyncOperation> reloaded = await reloadedQueue.GetPendingAsync();
        Assert.Equal(3, reloaded.Count);
        Assert.DoesNotContain(reloaded, operation => operation.MutationId == pending[0].MutationId);
    }

    [Fact]
    public async Task Queue_PersistsRetryConflictAndLocalResolutionStates()
    {
        var dataStore = new InMemoryLocalDataStore();
        DateTimeOffset now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        var queue = new PersonalDataSyncQueue(dataStore, new FixedTimeProvider(now));
        Guid noteId = Guid.NewGuid();
        await queue.EnqueueNoteDeleteAsync(noteId, now);
        PersonalDataSyncOperation operation = Assert.Single(await queue.GetPendingAsync());

        await queue.RecordRetryAsync(operation.MutationId, "http_503");
        PersonalDataSyncOperation retried = Assert.Single(await queue.GetPendingAsync());
        Assert.Equal(PersonalDataSyncOperationState.Pending, retried.State);
        Assert.Equal(1, retried.AttemptCount);
        Assert.Equal("http_503", retried.LastErrorCode);
        Assert.Equal(now, retried.LastAttemptAtUtc);

        await queue.MarkConflictAsync(operation.MutationId, "server_conflict", "{\"revision\":2}");
        PersonalDataSyncOperation conflict = Assert.Single(await queue.GetPendingAsync());
        Assert.Equal(PersonalDataSyncOperationState.Conflict, conflict.State);
        Assert.Equal(2, conflict.AttemptCount);
        Assert.Equal("{\"revision\":2}", conflict.ConflictServerStateJson);

        await queue.ResolveConflictKeepingLocalAsync(operation.MutationId);
        PersonalDataSyncOperation manualRetry = Assert.Single(await queue.GetPendingAsync());
        Assert.Equal(PersonalDataSyncOperationState.Pending, manualRetry.State);
        Assert.NotEqual(operation.MutationId, manualRetry.MutationId);
        Assert.True(manualRetry.OccurredAtUtc > operation.OccurredAtUtc);
        Assert.Equal(0, manualRetry.AttemptCount);
        Assert.Null(manualRetry.LastErrorCode);
        Assert.Null(manualRetry.ConflictServerStateJson);
    }

    private sealed class InMemoryLocalDataStore : ILocalDataStore
    {
        private readonly Dictionary<string, LocalDocument> _documents = [];

        public Task<LocalDocument?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_documents.GetValueOrDefault(key));

        public Task SaveAsync(LocalDocument document, CancellationToken cancellationToken = default)
        {
            _documents[document.Key] = document;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
