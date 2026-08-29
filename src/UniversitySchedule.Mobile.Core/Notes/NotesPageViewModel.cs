using System.Collections.ObjectModel;
using UniversitySchedule.Mobile.Core.Presentation;

namespace UniversitySchedule.Mobile.Core.Notes;

public sealed record NoteListItem(Guid Id, string Text, string UpdatedText);

public sealed class NotesPageViewModel(PersonalNoteStore store) : ObservableObject
{
    private bool _isLoading;

    public ObservableCollection<NoteListItem> Notes { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool HasNotes => Notes.Count > 0;

    public bool HasNoNotes => !HasNotes;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            PersonalNote[] notes = (await store.GetAllAsync(cancellationToken))
                .OrderByDescending(note => note.UpdatedAtUtc)
                .ToArray();
            Notes.Clear();
            foreach (PersonalNote note in notes)
            {
                Notes.Add(ToListItem(note));
            }

            NotifyCollectionState();
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task AddAsync(string text, CancellationToken cancellationToken = default)
    {
        PersonalNote note = await store.AddAsync(text, cancellationToken: cancellationToken);
        Notes.Insert(0, ToListItem(note));
        NotifyCollectionState();
    }

    private static NoteListItem ToListItem(PersonalNote note) => new(
        note.Id,
        note.Text,
        $"Изменено {note.UpdatedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm}");

    private void NotifyCollectionState()
    {
        OnPropertyChanged(nameof(HasNotes));
        OnPropertyChanged(nameof(HasNoNotes));
    }
}
