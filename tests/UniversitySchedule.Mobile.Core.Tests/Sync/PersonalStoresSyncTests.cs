using UniversitySchedule.Mobile.Core.Assignments;
using UniversitySchedule.Mobile.Core.Notes;
using UniversitySchedule.Mobile.Core.Storage;
using UniversitySchedule.Mobile.Core.Sync;

namespace UniversitySchedule.Mobile.Core.Tests.Sync;

public sealed class PersonalStoresSyncTests
{
    [Fact]
    public async Task Notes_CreateEditDelete_EmitDurableSyncEvents()
    {
        DateTimeOffset now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        var sink = new RecordingChangeSink();
        var store = new PersonalNoteStore(new InMemoryLocalDataStore(), new FixedTimeProvider(now), sink);

        PersonalNote created = await store.AddAsync("Черновик");
        PersonalNote updated = await store.UpdateAsync(
            created.Id,
            "Готово",
            null,
            null,
            null,
            false);
        await store.DeleteAsync(created.Id);

        Assert.Equal([created, updated], sink.NoteUpserts);
        Assert.Equal([(created.Id, now)], sink.NoteDeletes);
    }

    [Fact]
    public async Task Assignments_CreateEditDelete_EmitDurableSyncEvents()
    {
        DateTimeOffset now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        var sink = new RecordingChangeSink();
        var store = new PersonalAssignmentStore(
            new InMemoryLocalDataStore(),
            new FixedTimeProvider(now),
            sink);

        PersonalAssignment created = await store.AddAsync("Предмет", "Задание");
        PersonalAssignment updated = await store.UpdateAsync(
            created.Id,
            "Предмет",
            "Исправленное задание",
            null,
            null,
            PersonalAssignmentStatus.InProgress);
        await store.DeleteAsync(created.Id);

        Assert.Equal([created, updated], sink.AssignmentUpserts);
        Assert.Equal([(created.Id, now)], sink.AssignmentDeletes);
    }

    private sealed class RecordingChangeSink : IPersonalDataChangeSink
    {
        public List<PersonalNote> NoteUpserts { get; } = [];

        public List<(Guid Id, DateTimeOffset DeletedAtUtc)> NoteDeletes { get; } = [];

        public List<PersonalAssignment> AssignmentUpserts { get; } = [];

        public List<(Guid Id, DateTimeOffset DeletedAtUtc)> AssignmentDeletes { get; } = [];

        public Task NoteUpsertedAsync(PersonalNote note, CancellationToken cancellationToken = default)
        {
            NoteUpserts.Add(note);
            return Task.CompletedTask;
        }

        public Task NoteDeletedAsync(
            Guid noteId,
            DateTimeOffset deletedAtUtc,
            CancellationToken cancellationToken = default)
        {
            NoteDeletes.Add((noteId, deletedAtUtc));
            return Task.CompletedTask;
        }

        public Task AssignmentUpsertedAsync(
            PersonalAssignment assignment,
            CancellationToken cancellationToken = default)
        {
            AssignmentUpserts.Add(assignment);
            return Task.CompletedTask;
        }

        public Task AssignmentDeletedAsync(
            Guid assignmentId,
            DateTimeOffset deletedAtUtc,
            CancellationToken cancellationToken = default)
        {
            AssignmentDeletes.Add((assignmentId, deletedAtUtc));
            return Task.CompletedTask;
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
