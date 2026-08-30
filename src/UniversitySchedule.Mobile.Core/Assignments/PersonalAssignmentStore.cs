using System.Text.Json;
using UniversitySchedule.Mobile.Core.Storage;
using UniversitySchedule.Mobile.Core.Sync;

namespace UniversitySchedule.Mobile.Core.Assignments;

public sealed class PersonalAssignmentStore
{
    private const string StorageKey = "personal-assignments:v1";
    private readonly ILocalDataStore _localDataStore;
    private readonly TimeProvider _timeProvider;
    private readonly IPersonalDataChangeSink _changeSink;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public PersonalAssignmentStore(ILocalDataStore localDataStore, TimeProvider timeProvider)
        : this(localDataStore, timeProvider, NullPersonalDataChangeSink.Instance)
    {
    }

    public PersonalAssignmentStore(
        ILocalDataStore localDataStore,
        TimeProvider timeProvider,
        IPersonalDataChangeSink changeSink)
    {
        _localDataStore = localDataStore;
        _timeProvider = timeProvider;
        _changeSink = changeSink;
    }

    public async Task<IReadOnlyList<PersonalAssignment>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        LocalDocument? document = await _localDataStore.GetAsync(StorageKey, cancellationToken);
        return document is null
            ? []
            : JsonSerializer.Deserialize<IReadOnlyList<PersonalAssignment>>(document.Content) ?? [];
    }

    public async Task<PersonalAssignment?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        (await GetAllAsync(cancellationToken)).FirstOrDefault(item => item.Id == id);

    public async Task<PersonalAssignment> AddAsync(
        string subject,
        string text,
        Guid? lessonId = null,
        DateTimeOffset? deadlineUtc = null,
        PersonalAssignmentStatus status = PersonalAssignmentStatus.New,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            List<PersonalAssignment> assignments = (await GetAllAsync(cancellationToken)).ToList();
            DateTimeOffset now = _timeProvider.GetUtcNow();
            var assignment = new PersonalAssignment(
                Guid.NewGuid(),
                lessonId,
                NormalizeSubject(subject),
                text.Trim(),
                deadlineUtc,
                status,
                now,
                now);
            assignments.Add(assignment);
            await SaveAllAsync(assignments, now, cancellationToken);
            await _changeSink.AssignmentUpsertedAsync(assignment, cancellationToken);
            return assignment;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<PersonalAssignment> UpdateAsync(
        Guid id,
        string subject,
        string text,
        Guid? lessonId,
        DateTimeOffset? deadlineUtc,
        PersonalAssignmentStatus status,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            List<PersonalAssignment> assignments = (await GetAllAsync(cancellationToken)).ToList();
            int index = assignments.FindIndex(item => item.Id == id);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Assignment '{id}' was not found.");
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            PersonalAssignment updated = assignments[index] with
            {
                LessonId = lessonId,
                Subject = NormalizeSubject(subject),
                Text = text.Trim(),
                DeadlineUtc = deadlineUtc,
                Status = status,
                UpdatedAtUtc = now,
            };
            assignments[index] = updated;
            await SaveAllAsync(assignments, now, cancellationToken);
            await _changeSink.AssignmentUpsertedAsync(updated, cancellationToken);
            return updated;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            List<PersonalAssignment> assignments = (await GetAllAsync(cancellationToken)).ToList();
            int removed = assignments.RemoveAll(item => item.Id == id);
            if (removed == 0)
            {
                return false;
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            await SaveAllAsync(assignments, now, cancellationToken);
            await _changeSink.AssignmentDeletedAsync(id, now, cancellationToken);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<PersonalAssignment> SetStatusAsync(
        Guid id,
        PersonalAssignmentStatus status,
        CancellationToken cancellationToken = default)
    {
        PersonalAssignment assignment = await GetAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Assignment '{id}' was not found.");
        return await UpdateAsync(
            assignment.Id,
            assignment.Subject,
            assignment.Text,
            assignment.LessonId,
            assignment.DeadlineUtc,
            status,
            cancellationToken);
    }

    private Task SaveAllAsync(
        IReadOnlyCollection<PersonalAssignment> assignments,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken) =>
        _localDataStore.SaveAsync(
            new LocalDocument(StorageKey, JsonSerializer.Serialize(assignments), updatedAtUtc),
            cancellationToken);

    private static string NormalizeSubject(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Без предмета" : value.Trim();
}
