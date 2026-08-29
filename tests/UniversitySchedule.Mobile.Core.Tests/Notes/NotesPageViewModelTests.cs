using UniversitySchedule.Mobile.Core.Notes;
using UniversitySchedule.Mobile.Core.Storage;

namespace UniversitySchedule.Mobile.Core.Tests.Notes;

public sealed class NotesPageViewModelTests
{
    [Fact]
    public async Task LoadAndSearch_PutPinnedNotesFirstAndFilterAllFields()
    {
        var dataStore = new InMemoryLocalDataStore();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero));
        var store = new PersonalNoteStore(dataStore, clock);
        await store.AddAsync("Обычная заметка", subject: "Математика");
        PersonalNote pinned = await store.AddAsync(
            "Шаблоны проектирования",
            title: "ООП",
            subject: "Программирование",
            isPinned: true);
        var viewModel = new NotesPageViewModel(store);

        await viewModel.LoadAsync();

        Assert.Equal(pinned.Id, viewModel.Notes[0].Id);
        Assert.Equal("Закреплённые", viewModel.Notes[0].SectionLabel);
        Assert.Equal("Недавние", viewModel.Notes[1].SectionLabel);

        viewModel.Query = "программ";
        Assert.Equal(pinned.Id, Assert.Single(viewModel.Notes).Id);
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
