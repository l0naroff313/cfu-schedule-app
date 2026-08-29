using UniversitySchedule.Mobile.Core.Notes;

namespace UniversitySchedule.Mobile.Pages;

public partial class NotesPage : ContentPage
{
    private readonly NotesPageViewModel _viewModel;

    public NotesPage(NotesPageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async void OnAddNoteClicked(object? sender, EventArgs e)
    {
        string? text = await DisplayPromptAsync(
            "Новая заметка",
            "Введите текст заметки",
            accept: "Сохранить",
            cancel: "Отмена",
            placeholder: "Что важно запомнить?",
            maxLength: 2000,
            keyboard: Keyboard.Text);
        if (!string.IsNullOrWhiteSpace(text))
        {
            await _viewModel.AddAsync(text);
        }
    }
}
