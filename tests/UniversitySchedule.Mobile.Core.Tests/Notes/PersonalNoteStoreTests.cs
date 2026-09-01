using UniversitySchedule.Mobile.Core.Notes;
using UniversitySchedule.Mobile.Core.Storage;

namespace UniversitySchedule.Mobile.Core.Tests.Notes;

public sealed class PersonalNoteStoreTests
{
    [Fact]
    public async Task AddAsync_PersistsNoteForOfflineRead()
    {
        var dataStore = new InMemoryLocalDataStore();
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero));
        var store = new PersonalNoteStore(dataStore, timeProvider);

        PersonalNote created = await store.AddAsync("Подготовить конспект");
        IReadOnlyList<PersonalNote> loaded = await store.GetAllAsync();

        PersonalNote note = Assert.Single(loaded);
        Assert.Equal(created.Id, note.Id);
        Assert.Equal("Подготовить конспект", note.Text);
    }

    [Fact]
    public async Task UpdateAndDeleteAsync_KeepLessonLinkAndPersistChanges()
    {
        var dataStore = new InMemoryLocalDataStore();
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero));
        var store = new PersonalNoteStore(dataStore, timeProvider);
        Guid lessonId = Guid.NewGuid();
        PersonalNote created = await store.AddAsync(
            "Первый текст",
            lessonId,
            "Теорема",
            "Математика",
            true);

        PersonalNote updated = await store.UpdateAsync(
            created.Id,
            "Исправленный текст",
            lessonId,
            "Новая теорема",
            "Математика",
            false);

        Assert.Equal(lessonId, updated.LessonId);
        Assert.Equal("Исправленный текст", updated.Text);
        Assert.Equal("Новая теорема", updated.Title);
        Assert.False(updated.IsPinned);
        Assert.True(await store.DeleteAsync(created.Id));
        Assert.Empty(await store.GetAllAsync());
    }

    [Fact]
    public async Task GetAllAsync_ReadsNotesCreatedBeforeTitlesAndPinningWereAdded()
    {
        var dataStore = new InMemoryLocalDataStore();
        DateTimeOffset now = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
        Guid id = Guid.NewGuid();
        await dataStore.SaveAsync(new LocalDocument(
            "personal-notes:v1",
            $$"""[{"Id":"{{id}}","LessonId":null,"Text":"Старая заметка","CreatedAtUtc":"{{now:O}}","UpdatedAtUtc":"{{now:O}}"}]""",
            now));
        var store = new PersonalNoteStore(dataStore, new FixedTimeProvider(now));

        PersonalNote note = Assert.Single(await store.GetAllAsync());

        Assert.Equal("Старая заметка", note.Text);
        Assert.Null(note.Title);
        Assert.Null(note.Subject);
        Assert.False(note.IsPinned);
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

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
