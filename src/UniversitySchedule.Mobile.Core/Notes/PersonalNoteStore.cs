using System.Text.Json;
using UniversitySchedule.Mobile.Core.Storage;
using UniversitySchedule.Mobile.Core.Sync;

namespace UniversitySchedule.Mobile.Core.Notes;

public sealed class PersonalNoteStore
{
    private const string StorageKey = "personal-notes:v1";
    private readonly ILocalDataStore _localDataStore;
    private readonly TimeProvider _timeProvider;
    private readonly IPersonalDataChangeSink _changeSink;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public PersonalNoteStore(ILocalDataStore localDataStore, TimeProvider timeProvider)
        : this(localDataStore, timeProvider, NullPersonalDataChangeSink.Instance)
    {
    }

    public PersonalNoteStore(
        ILocalDataStore localDataStore,
        TimeProvider timeProvider,
        IPersonalDataChangeSink changeSink)
    {
        _localDataStore = localDataStore;
        _timeProvider = timeProvider;
        _changeSink = changeSink;
    }

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
        string? title = null,
        string? subject = null,
        bool isPinned = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            List<PersonalNote> notes = (await GetAllAsync(cancellationToken)).ToList();
            DateTimeOffset now = _timeProvider.GetUtcNow();
            var note = new PersonalNote(
                Guid.NewGuid(),
                lessonId,
                text.Trim(),
                now,
                now,
                NormalizeOptional(title),
                NormalizeOptional(subject),
                isPinned);
            notes.Add(note);
            await SaveAllAsync(notes, now, cancellationToken);
            await _changeSink.NoteUpsertedAsync(note, cancellationToken);
            return note;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<PersonalNote?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return (await GetAllAsync(cancellationToken)).FirstOrDefault(note => note.Id == id);
    }

    public async Task<PersonalNote> UpdateAsync(
        Guid id,
        string text,
        Guid? lessonId,
        string? title,
        string? subject,
        bool isPinned,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            List<PersonalNote> notes = (await GetAllAsync(cancellationToken)).ToList();
            int index = notes.FindIndex(note => note.Id == id);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Note '{id}' was not found.");
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            PersonalNote updated = notes[index] with
            {
                Text = text.Trim(),
                LessonId = lessonId,
                Title = NormalizeOptional(title),
                Subject = NormalizeOptional(subject),
                IsPinned = isPinned,
                UpdatedAtUtc = now,
            };
            notes[index] = updated;
            await SaveAllAsync(notes, now, cancellationToken);
            await _changeSink.NoteUpsertedAsync(updated, cancellationToken);
            return updated;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            List<PersonalNote> notes = (await GetAllAsync(cancellationToken)).ToList();
            int removed = notes.RemoveAll(note => note.Id == id);
            if (removed == 0)
            {
                return false;
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            await SaveAllAsync(notes, now, cancellationToken);
            await _changeSink.NoteDeletedAsync(id, now, cancellationToken);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<PersonalNote> SetPinnedAsync(
        Guid id,
        bool isPinned,
        CancellationToken cancellationToken = default)
    {
        PersonalNote note = await GetAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Note '{id}' was not found.");
        return await UpdateAsync(
            note.Id,
            note.Text,
            note.LessonId,
            note.Title,
            note.Subject,
            isPinned,
            cancellationToken);
    }

    public async Task ReplaceFromSynchronizationAsync(
        Guid id,
        PersonalNote? note,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        if (note is not null && note.Id != id)
        {
            throw new ArgumentException("The synchronized note has another identifier.", nameof(note));
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            List<PersonalNote> notes = (await GetAllAsync(cancellationToken)).ToList();
            notes.RemoveAll(item => item.Id == id);
            if (note is not null)
            {
                notes.Add(note);
            }

            await SaveAllAsync(notes, _timeProvider.GetUtcNow(), cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private Task SaveAllAsync(
        IReadOnlyCollection<PersonalNote> notes,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken)
    {
        return _localDataStore.SaveAsync(
            new LocalDocument(StorageKey, JsonSerializer.Serialize(notes), updatedAtUtc),
            cancellationToken);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
