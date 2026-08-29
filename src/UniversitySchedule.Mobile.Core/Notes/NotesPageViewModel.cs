using System.Collections.ObjectModel;
using UniversitySchedule.Mobile.Core.Presentation;

namespace UniversitySchedule.Mobile.Core.Notes;

public sealed record NoteListItem(
    Guid Id,
    Guid? LessonId,
    string Title,
    string Preview,
    string Subject,
    string UpdatedText,
    bool IsPinned,
    string PinText,
    string SectionLabel,
    bool ShowSection);

public sealed class NotesPageViewModel(PersonalNoteStore store) : ObservableObject
{
    private IReadOnlyList<PersonalNote> _allNotes = [];
    private bool _isLoading;
    private string _query = string.Empty;

    public ObservableCollection<NoteListItem> Notes { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool HasNotes => Notes.Count > 0;

    public bool HasNoNotes => !HasNotes;

    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            _allNotes = (await store.GetAllAsync(cancellationToken))
                .OrderByDescending(note => note.IsPinned)
                .ThenByDescending(note => note.UpdatedAtUtc)
                .ToArray();
            ApplyFilter();
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task AddAsync(string text, CancellationToken cancellationToken = default)
    {
        PersonalNote note = await store.AddAsync(text, cancellationToken: cancellationToken);
        _allNotes = [note, .. _allNotes];
        ApplyFilter();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await store.DeleteAsync(id, cancellationToken))
        {
            _allNotes = _allNotes.Where(note => note.Id != id).ToArray();
            ApplyFilter();
        }
    }

    public async Task TogglePinnedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        PersonalNote? note = _allNotes.FirstOrDefault(item => item.Id == id);
        if (note is null)
        {
            return;
        }

        await store.SetPinnedAsync(id, !note.IsPinned, cancellationToken);
        await LoadAsync(cancellationToken);
    }

    private void ApplyFilter()
    {
        string normalizedQuery = Query.Trim();
        IEnumerable<PersonalNote> notes = _allNotes;
        if (normalizedQuery.Length > 0)
        {
            notes = notes.Where(note =>
                Contains(note.Title, normalizedQuery) ||
                Contains(note.Text, normalizedQuery) ||
                Contains(note.Subject, normalizedQuery));
        }

        PersonalNote[] visible = notes.ToArray();
        Notes.Clear();
        bool pinnedHeaderAdded = false;
        bool recentHeaderAdded = false;
        foreach (PersonalNote note in visible)
        {
            string section = string.Empty;
            if (note.IsPinned && !pinnedHeaderAdded)
            {
                section = "Закреплённые";
                pinnedHeaderAdded = true;
            }
            else if (!note.IsPinned && !recentHeaderAdded)
            {
                section = pinnedHeaderAdded ? "Недавние" : "Заметки";
                recentHeaderAdded = true;
            }

            Notes.Add(ToListItem(note, section));
        }

        NotifyCollectionState();
    }

    private static NoteListItem ToListItem(PersonalNote note, string sectionLabel)
    {
        string title = string.IsNullOrWhiteSpace(note.Title)
            ? FirstMeaningfulLine(note.Text)
            : note.Title;
        string preview = note.Text.Trim();
        if (string.Equals(preview, title, StringComparison.CurrentCulture))
        {
            preview = string.Empty;
        }

        return new NoteListItem(
            note.Id,
            note.LessonId,
            title,
            preview,
            note.Subject ?? "Без привязки к предмету",
            $"Изменено {note.UpdatedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm}",
            note.IsPinned,
            note.IsPinned ? "Открепить" : "Закрепить",
            sectionLabel,
            sectionLabel.Length > 0);
    }

    private static string FirstMeaningfulLine(string text)
    {
        string? line = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .FirstOrDefault(value => value.Length > 0);
        if (string.IsNullOrEmpty(line))
        {
            return "Заметка";
        }

        return line.Length <= 72 ? line : $"{line[..69]}…";
    }

    private static bool Contains(string? value, string query) =>
        value?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true;

    private void NotifyCollectionState()
    {
        OnPropertyChanged(nameof(HasNotes));
        OnPropertyChanged(nameof(HasNoNotes));
    }
}
