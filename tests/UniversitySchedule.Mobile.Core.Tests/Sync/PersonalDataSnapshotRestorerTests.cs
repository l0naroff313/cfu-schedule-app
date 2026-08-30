using System.Net;
using System.Net.Http.Json;
using UniversitySchedule.Contracts.Identity;
using UniversitySchedule.Contracts.PersonalData;
using UniversitySchedule.Mobile.Core.Assignments;
using UniversitySchedule.Mobile.Core.Identity;
using UniversitySchedule.Mobile.Core.Notes;
using UniversitySchedule.Mobile.Core.Storage;
using UniversitySchedule.Mobile.Core.Sync;

namespace UniversitySchedule.Mobile.Core.Tests.Sync;

public sealed class PersonalDataSnapshotRestorerTests
{
    [Fact]
    public async Task RestoreAsync_AppliesSnapshotAndProtectsQueuedLocalChanges()
    {
        DateTimeOffset now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        Guid replacedNoteId = Guid.NewGuid();
        Guid protectedNoteId = Guid.NewGuid();
        Guid deletedAssignmentId = Guid.NewGuid();
        Guid restoredAssignmentId = Guid.NewGuid();
        var dataStore = new InMemoryLocalDataStore();
        var clock = new FixedTimeProvider(now);
        var queue = new PersonalDataSyncQueue(dataStore, clock);
        var noteStore = new PersonalNoteStore(dataStore, clock);
        var assignmentStore = new PersonalAssignmentStore(dataStore, clock);
        var replacedNote = new PersonalNote(replacedNoteId, null, "Старая версия", now, now);
        var protectedNote = new PersonalNote(protectedNoteId, null, "Офлайн-правка", now, now.AddMinutes(2));
        var deletedAssignment = new PersonalAssignment(
            deletedAssignmentId,
            null,
            "Физика",
            "Удалить локально",
            null,
            PersonalAssignmentStatus.New,
            now,
            now);
        await noteStore.ReplaceFromSynchronizationAsync(replacedNoteId, replacedNote);
        await noteStore.ReplaceFromSynchronizationAsync(protectedNoteId, protectedNote);
        await assignmentStore.ReplaceFromSynchronizationAsync(deletedAssignmentId, deletedAssignment);
        await queue.EnqueueNoteUpsertAsync(protectedNote);
        var snapshot = new PersonalDataSnapshotResponse(
            now.AddMinutes(5),
            [
                CreateNote(replacedNoteId, "Серверная версия", now.AddMinutes(1)),
                CreateNote(protectedNoteId, "Не должна заменить офлайн", now.AddMinutes(3)),
            ],
            [
                CreateAssignment(deletedAssignmentId, "Удалено", now.AddMinutes(1)) with
                {
                    DeletedAtUtc = now.AddMinutes(1),
                },
                CreateAssignment(restoredAssignmentId, "Восстановлено", now.AddMinutes(1)),
            ]);
        var handler = new SnapshotHandler(now, JsonContent.Create(snapshot));
        UniversityScheduleApiClient client = CreateClient(handler, clock);
        var restorer = new PersonalDataSnapshotRestorer(
            client,
            queue,
            noteStore,
            assignmentStore);

        PersonalDataSnapshotRestoreResult result = await restorer.RestoreAsync();

        Assert.Equal(PersonalDataSnapshotDownloadOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, result.RestoredNoteCount);
        Assert.Equal(2, result.RestoredAssignmentCount);
        Assert.Equal(1, result.ProtectedEntityCount);
        Assert.Equal(snapshot.GeneratedAtUtc, result.SnapshotGeneratedAtUtc);
        IReadOnlyList<PersonalNote> notes = await noteStore.GetAllAsync();
        Assert.Contains(notes, note => note.Id == replacedNoteId && note.Text == "Серверная версия");
        Assert.Contains(notes, note => note.Id == protectedNoteId && note.Text == "Офлайн-правка");
        IReadOnlyList<PersonalAssignment> assignments = await assignmentStore.GetAllAsync();
        Assert.DoesNotContain(assignments, assignment => assignment.Id == deletedAssignmentId);
        Assert.Contains(assignments, assignment =>
            assignment.Id == restoredAssignmentId && assignment.Text == "Восстановлено");
        Assert.Single(await queue.GetPendingAsync());
    }

    [Fact]
    public async Task RestoreAsync_InvalidSnapshotPreservesLocalData()
    {
        DateTimeOffset now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        var dataStore = new InMemoryLocalDataStore();
        var clock = new FixedTimeProvider(now);
        var queue = new PersonalDataSyncQueue(dataStore, clock);
        var noteStore = new PersonalNoteStore(dataStore, clock);
        var assignmentStore = new PersonalAssignmentStore(dataStore, clock);
        var local = new PersonalNote(Guid.NewGuid(), null, "Только локально", now, now);
        await noteStore.ReplaceFromSynchronizationAsync(local.Id, local);
        var handler = new SnapshotHandler(now, new StringContent("{}"));
        var restorer = new PersonalDataSnapshotRestorer(
            CreateClient(handler, clock),
            queue,
            noteStore,
            assignmentStore);

        PersonalDataSnapshotRestoreResult result = await restorer.RestoreAsync();

        Assert.Equal(PersonalDataSnapshotDownloadOutcome.PermanentFailure, result.Outcome);
        Assert.Equal("invalid_snapshot", result.ErrorCode);
        Assert.Equal(local, Assert.Single(await noteStore.GetAllAsync()));
    }

    private static UniversityScheduleApiClient CreateClient(
        HttpMessageHandler handler,
        TimeProvider timeProvider)
    {
        var secureStore = new InMemorySecureValueStore();
        var options = new UniversityScheduleApiOptions(
            new Uri("https://api.example.test/"),
            "android",
            "1.0.0");
        return new UniversityScheduleApiClient(
            new HttpClient(handler) { BaseAddress = options.BaseAddress },
            options,
            new InstallationIdentityService(secureStore, timeProvider),
            secureStore,
            timeProvider);
    }

    private static SyncedNoteResponse CreateNote(
        Guid id,
        string text,
        DateTimeOffset timestamp) =>
        new(
            id,
            null,
            text,
            null,
            null,
            false,
            timestamp,
            timestamp,
            timestamp,
            null,
            1,
            false,
            SyncMutationDisposition.AlreadyApplied);

    private static SyncedAssignmentResponse CreateAssignment(
        Guid id,
        string text,
        DateTimeOffset timestamp) =>
        new(
            id,
            null,
            "Алгоритмы",
            text,
            null,
            AssignmentSyncStatus.New,
            timestamp,
            timestamp,
            timestamp,
            null,
            1,
            false,
            SyncMutationDisposition.AlreadyApplied);

    private sealed class SnapshotHandler(DateTimeOffset now, HttpContent snapshotContent) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/installations/register", StringComparison.Ordinal))
            {
                RegisterInstallationRequest? registration = await request.Content!
                    .ReadFromJsonAsync<RegisterInstallationRequest>(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new RegisterInstallationResponse(
                        registration!.InstallationId,
                        "test-access-token",
                        "Bearer",
                        now.AddMinutes(15),
                        true)),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = snapshotContent };
        }
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

    private sealed class InMemorySecureValueStore : ISecureValueStore
    {
        private readonly Dictionary<string, string> _values = [];

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.GetValueOrDefault(key));

        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
