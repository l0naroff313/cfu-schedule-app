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
