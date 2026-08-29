using System.Text.Json;
using UniversitySchedule.Mobile.Core.Storage;

namespace UniversitySchedule.Mobile.Core.Notes;

public sealed class PersonalNoteStore(ILocalDataStore localDataStore, TimeProvider timeProvider)
{
    private const string StorageKey = "personal-notes:v1";
    private readonly ILocalDataStore _localDataStore = localDataStore;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<IReadOnlyList<PersonalNote>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        LocalDocument? document = await _localDataStore.GetAsync(StorageKey, cancellationToken);
        return document is null
            ? []
            : JsonSerializer.Deserialize<IReadOnlyList<PersonalNote>>(document.Content) ?? [];
    }

    public async Task<PersonalNote> AddAsync(
        string text,
        Guid? lessonId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            List<PersonalNote> notes = (await GetAllAsync(cancellationToken)).ToList();
            DateTimeOffset now = _timeProvider.GetUtcNow();
            var note = new PersonalNote(Guid.NewGuid(), lessonId, text.Trim(), now, now);
            notes.Add(note);
            await _localDataStore.SaveAsync(
                new LocalDocument(StorageKey, JsonSerializer.Serialize(notes), now),
                cancellationToken);
            return note;
        }
        finally
        {
            _lock.Release();
        }
    }
}
