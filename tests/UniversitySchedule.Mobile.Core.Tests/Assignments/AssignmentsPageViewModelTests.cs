using UniversitySchedule.Mobile.Core.Assignments;
using UniversitySchedule.Mobile.Core.Storage;

namespace UniversitySchedule.Mobile.Core.Tests.Assignments;

public sealed class AssignmentsPageViewModelTests
{
    [Fact]
    public async Task LoadAndFilters_ExposeProgressAndRelevantDeadlines()
    {
        DateTimeOffset now = new(2026, 8, 29, 9, 0, 0, TimeSpan.Zero);
        var store = new PersonalAssignmentStore(new InMemoryLocalDataStore(), new FixedTimeProvider(now));
        PersonalAssignment today = await store.AddAsync(
            "Математика",
            "Сегодня",
            deadlineUtc: new DateTimeOffset(2026, 8, 29, 18, 0, 0, TimeSpan.FromHours(3)).ToUniversalTime());
        await store.AddAsync(
            "Физика",
            "На неделе",
            deadlineUtc: new DateTimeOffset(2026, 9, 1, 18, 0, 0, TimeSpan.FromHours(3)).ToUniversalTime());
        await store.AddAsync(
            "История",
            "Готово",
            status: PersonalAssignmentStatus.Completed);
        var viewModel = new AssignmentsPageViewModel(store, new FixedTimeProvider(now));

        await viewModel.LoadAsync();

        Assert.Equal(3, viewModel.TotalCount);
        Assert.Equal(1, viewModel.CompletedCount);
        Assert.Equal(3, viewModel.Assignments.Count);

        viewModel.ShowTodayCommand.Execute(null);
        Assert.Equal(today.Id, Assert.Single(viewModel.Assignments).Id);

        viewModel.ShowCompletedCommand.Execute(null);
        Assert.True(Assert.Single(viewModel.Assignments).IsCompleted);
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
