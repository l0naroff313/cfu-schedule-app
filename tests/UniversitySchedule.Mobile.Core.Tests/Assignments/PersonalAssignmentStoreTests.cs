using UniversitySchedule.Mobile.Core.Assignments;
using UniversitySchedule.Mobile.Core.Storage;

namespace UniversitySchedule.Mobile.Core.Tests.Assignments;

public sealed class PersonalAssignmentStoreTests
{
    [Fact]
    public async Task Crud_PersistsLessonDeadlineAndStatus()
    {
        var dataStore = new InMemoryLocalDataStore();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero));
        var store = new PersonalAssignmentStore(dataStore, clock);
        Guid lessonId = Guid.NewGuid();
        DateTimeOffset deadline = clock.GetUtcNow().AddDays(2);

        PersonalAssignment created = await store.AddAsync(
            "Математика",
            "Решить задачи 1–4",
            lessonId,
            deadline);
        PersonalAssignment updated = await store.UpdateAsync(
            created.Id,
            "Математический анализ",
            "Решить задачи 1–6",
            lessonId,
            deadline.AddHours(1),
            PersonalAssignmentStatus.InProgress);
        await store.SetStatusAsync(created.Id, PersonalAssignmentStatus.Completed);

        PersonalAssignment saved = Assert.Single(await store.GetAllAsync());
        Assert.Equal(updated.Id, saved.Id);
        Assert.Equal(lessonId, saved.LessonId);
        Assert.Equal("Решить задачи 1–6", saved.Text);
        Assert.Equal(PersonalAssignmentStatus.Completed, saved.Status);
        Assert.True(await store.DeleteAsync(created.Id));
        Assert.Empty(await store.GetAllAsync());
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
