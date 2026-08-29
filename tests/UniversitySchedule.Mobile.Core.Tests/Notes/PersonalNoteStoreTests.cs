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
