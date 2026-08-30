using System.Text.Json;
using UniversitySchedule.Contracts.PersonalData;
using UniversitySchedule.Mobile.Core.Assignments;
using UniversitySchedule.Mobile.Core.Notes;
using UniversitySchedule.Mobile.Core.Storage;
using UniversitySchedule.Mobile.Core.Sync;

namespace UniversitySchedule.Mobile.Core.Tests.Sync;

public sealed class PersonalDataConflictResolutionServiceTests
{
    [Fact]
    public async Task KeepServerAsync_AppliesServerNoteAndDiscardsEveryLocalMutation()
    {
        TestContext context = CreateContext();
        Guid noteId = Guid.NewGuid();
        var local = new PersonalNote(
            noteId,
            Guid.NewGuid(),
            "Локальный текст",
            context.Now,
            context.Now,
            "Локальная заметка",
            "Физика",
            true);
        await context.NoteStore.ReplaceFromSynchronizationAsync(noteId, local);
        await context.Queue.EnqueueNoteUpsertAsync(local);
        PersonalDataSyncOperation first = Assert.Single(await context.Queue.GetPendingAsync());
        SyncedNoteResponse server = CreateServerNote(local, "Серверный текст", context.Now.AddMinutes(5));
        await context.Queue.MarkConflictAsync(first.MutationId, "server_conflict", JsonSerializer.Serialize(server));
        await context.Queue.EnqueueNoteUpsertAsync(local with { Text = "Более поздняя локальная правка" });

        PersonalDataConflictItem item = Assert.Single(await context.Service.GetConflictsAsync());
        Assert.True(item.CanKeepServer);
        Assert.Contains("Локальный текст", item.LocalSummary);
        Assert.Contains("Серверный текст", item.ServerSummary);

        await context.Service.KeepServerAsync(first.MutationId);

        PersonalNote stored = Assert.Single(await context.NoteStore.GetAllAsync());
        Assert.Equal("Серверный текст", stored.Text);
        Assert.Empty(await context.Queue.GetPendingAsync());
    }

    [Fact]
    public async Task KeepLocalAsync_CollapsesAssignmentQueueAndMakesItNewerThanServer()
    {
        TestContext context = CreateContext();
        Guid assignmentId = Guid.NewGuid();
        var original = new PersonalAssignment(
            assignmentId,
            null,
            "Алгебра",
            "Старая локальная версия",
            null,
            PersonalAssignmentStatus.New,
            context.Now,
            context.Now);
        var latest = original with
        {
            Text = "Последняя локальная версия",
            Status = PersonalAssignmentStatus.InProgress,
            UpdatedAtUtc = context.Now.AddMinutes(1),
        };
        await context.AssignmentStore.ReplaceFromSynchronizationAsync(assignmentId, latest);
        await context.Queue.EnqueueAssignmentUpsertAsync(original);
        PersonalDataSyncOperation first = Assert.Single(await context.Queue.GetPendingAsync());
        DateTimeOffset serverTime = context.Now.AddMinutes(10);
        SyncedAssignmentResponse server = CreateServerAssignment(original, serverTime);
        await context.Queue.MarkConflictAsync(first.MutationId, "server_conflict", JsonSerializer.Serialize(server));
        await context.Queue.EnqueueAssignmentUpsertAsync(latest);

        await context.Service.KeepLocalAsync(first.MutationId);

        PersonalDataSyncOperation replacement = Assert.Single(await context.Queue.GetPendingAsync());
        Assert.Equal(PersonalDataSyncOperationState.Pending, replacement.State);
        Assert.Equal(PersonalDataSyncMutationKind.Upsert, replacement.MutationKind);
        Assert.NotEqual(first.MutationId, replacement.MutationId);
        Assert.Equal("Последняя локальная версия", replacement.Assignment?.Text);
        Assert.True(replacement.OccurredAtUtc > serverTime);
        PersonalAssignment stored = Assert.Single(await context.AssignmentStore.GetAllAsync());
        Assert.Equal(replacement.OccurredAtUtc, stored.UpdatedAtUtc);
    }

    [Fact]
    public async Task KeepServerAsync_AppliesServerDeletion()
    {
        TestContext context = CreateContext();
        Guid noteId = Guid.NewGuid();
        var local = new PersonalNote(noteId, null, "Останется?", context.Now, context.Now);
        await context.NoteStore.ReplaceFromSynchronizationAsync(noteId, local);
        await context.Queue.EnqueueNoteUpsertAsync(local);
        PersonalDataSyncOperation operation = Assert.Single(await context.Queue.GetPendingAsync());
        SyncedNoteResponse tombstone = CreateServerNote(local, string.Empty, context.Now.AddMinutes(2)) with
        {
            DeletedAtUtc = context.Now.AddMinutes(2),
        };
        await context.Queue.MarkConflictAsync(
            operation.MutationId,
            "server_conflict",
            JsonSerializer.Serialize(tombstone));

        await context.Service.KeepServerAsync(operation.MutationId);

        Assert.Empty(await context.NoteStore.GetAllAsync());
        Assert.Empty(await context.Queue.GetPendingAsync());
    }

    [Fact]
    public async Task InvalidServerState_DisablesServerChoiceAndPreservesLocalData()
    {
        TestContext context = CreateContext();
        Guid noteId = Guid.NewGuid();
        var local = new PersonalNote(noteId, null, "Локально", context.Now, context.Now);
        await context.NoteStore.ReplaceFromSynchronizationAsync(noteId, local);
        await context.Queue.EnqueueNoteUpsertAsync(local);
        PersonalDataSyncOperation operation = Assert.Single(await context.Queue.GetPendingAsync());
        await context.Queue.MarkConflictAsync(
            operation.MutationId,
            "server_conflict",
            "{\"title\":\"problem details instead of an entity\"}");

        PersonalDataConflictItem item = Assert.Single(await context.Service.GetConflictsAsync());
        Assert.False(item.CanKeepServer);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Service.KeepServerAsync(operation.MutationId));

        Assert.Single(await context.NoteStore.GetAllAsync());
        Assert.Single(await context.Queue.GetPendingAsync());
    }

    private static TestContext CreateContext()
    {
        DateTimeOffset now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        var dataStore = new InMemoryLocalDataStore();
        var timeProvider = new FixedTimeProvider(now);
        var queue = new PersonalDataSyncQueue(dataStore, timeProvider);
        var noteStore = new PersonalNoteStore(dataStore, timeProvider);
        var assignmentStore = new PersonalAssignmentStore(dataStore, timeProvider);
        var service = new PersonalDataConflictResolutionService(
            queue,
            noteStore,
            assignmentStore,
            timeProvider);
        return new TestContext(now, queue, noteStore, assignmentStore, service);
    }

    private static SyncedNoteResponse CreateServerNote(
        PersonalNote note,
        string text,
        DateTimeOffset updatedAtUtc) =>
        new(
            note.Id,
            note.LessonId,
            text,
            "Серверная заметка",
            note.Subject,
            false,
            note.CreatedAtUtc,
            updatedAtUtc,
            updatedAtUtc,
            null,
            2,
            false,
            SyncMutationDisposition.Conflict);

    private static SyncedAssignmentResponse CreateServerAssignment(
        PersonalAssignment assignment,
        DateTimeOffset updatedAtUtc) =>
        new(
            assignment.Id,
            assignment.LessonId,
            assignment.Subject,
            "Серверная версия",
            assignment.DeadlineUtc,
            AssignmentSyncStatus.New,
            assignment.CreatedAtUtc,
            updatedAtUtc,
            updatedAtUtc,
            null,
            3,
            false,
            SyncMutationDisposition.Conflict);

    private sealed record TestContext(
        DateTimeOffset Now,
        PersonalDataSyncQueue Queue,
        PersonalNoteStore NoteStore,
        PersonalAssignmentStore AssignmentStore,
        PersonalDataConflictResolutionService Service);

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
