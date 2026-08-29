using Microsoft.Extensions.DependencyInjection;
using UniversitySchedule.Mobile.Core.Notes;

namespace UniversitySchedule.Mobile.Pages;

public partial class NotesPage : ContentPage
{
    private readonly NotesPageViewModel _viewModel;
    private readonly IServiceProvider _services;

    public NotesPage(NotesPageViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _services = services;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async void OnAddNoteClicked(object? sender, EventArgs e)
    {
        await OpenEditorAsync();
    }

    private async void OnNoteSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is NoteListItem note)
        {
            NotesCollection.SelectedItem = null;
            await OpenEditorAsync(note.Id);
        }
    }

    private async void OnPinNoteInvoked(object? sender, EventArgs e)
    {
        if (sender is SwipeItem { CommandParameter: Guid id })
        {
            await _viewModel.TogglePinnedAsync(id);
        }
    }

    private async void OnDeleteNoteInvoked(object? sender, EventArgs e)
    {
        if (sender is not SwipeItem { CommandParameter: Guid id } ||
            !await DisplayAlertAsync("Удалить заметку?", "Это действие нельзя отменить.", "Удалить", "Отмена"))
        {
            return;
        }

        await _viewModel.DeleteAsync(id);
    }

    public async Task OpenEditorAsync(Guid? noteId = null, Guid? lessonId = null)
    {
        var editor = _services.GetRequiredService<NoteEditorPage>();
        editor.Configure(noteId, lessonId);
        await Navigation.PushModalAsync(editor);
    }
}
