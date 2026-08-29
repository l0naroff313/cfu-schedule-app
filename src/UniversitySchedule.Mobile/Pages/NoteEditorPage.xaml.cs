using UniversitySchedule.Mobile.Core.Notes;
using UniversitySchedule.Mobile.Core.Scheduling;

namespace UniversitySchedule.Mobile.Pages;

public partial class NoteEditorPage : ContentPage
{
    private readonly PersonalNoteStore _store;
    private readonly ScheduleSession _scheduleSession;
    private Guid? _noteId;
    private Guid? _preselectedLessonId;
    private string? _lastLessonSubject;
    private bool _loaded;

    public NoteEditorPage(PersonalNoteStore store, ScheduleSession scheduleSession)
    {
        _store = store;
        _scheduleSession = scheduleSession;
        InitializeComponent();
    }

    public void Configure(Guid? noteId = null, Guid? lessonId = null)
    {
        _noteId = noteId;
        _preselectedLessonId = lessonId;
        _loaded = false;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            await _scheduleSession.InitializeAsync();
        }
        catch (InvalidOperationException)
        {
            // Notes must remain editable offline even when no schedule was cached yet.
        }

        List<LessonPickerItem> lessons = LessonPickerItem.FromSession(_scheduleSession).ToList();
        PersonalNote? note = _noteId is Guid id ? await _store.GetAsync(id) : null;
        if (note is not null)
        {
            TitleEntry.Text = note.Title;
            SubjectEntry.Text = note.Subject;
            TextEditor.Text = note.Text;
            PinnedSwitch.IsToggled = note.IsPinned;
            _preselectedLessonId = note.LessonId;
            DeleteButton.IsVisible = true;
            PreserveUnavailableLesson(lessons, note.LessonId, note.Subject);
        }

        LessonPicker.ItemsSource = lessons;
        LessonPicker.SelectedItem = lessons.FirstOrDefault(item => item.LessonId == _preselectedLessonId)
            ?? lessons[0];
        _lastLessonSubject = (LessonPicker.SelectedItem as LessonPickerItem)?.Subject;
    }

    private static void PreserveUnavailableLesson(
        List<LessonPickerItem> lessons,
        Guid? lessonId,
        string? subject)
    {
        if (lessonId is not Guid id || lessons.Any(item => item.LessonId == id))
        {
            return;
        }

        string name = string.IsNullOrWhiteSpace(subject) ? "Ранее выбранная пара" : subject;
        lessons.Insert(1, new LessonPickerItem(id, $"Ранее выбранная пара • {name}", name));
    }

    private void OnLessonSelected(object? sender, EventArgs e)
    {
        if (LessonPicker.SelectedItem is not LessonPickerItem lesson || string.IsNullOrWhiteSpace(lesson.Subject))
        {
            _lastLessonSubject = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(SubjectEntry.Text) ||
            string.Equals(SubjectEntry.Text, _lastLessonSubject, StringComparison.CurrentCulture))
        {
            SubjectEntry.Text = lesson.Subject;
        }

        _lastLessonSubject = lesson.Subject;
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TextEditor.Text))
        {
            await DisplayAlertAsync("Нужен текст", "Введите текст заметки.", "Хорошо");
            return;
        }

        LessonPickerItem? lesson = LessonPicker.SelectedItem as LessonPickerItem;
        if (_noteId is Guid id)
        {
            await _store.UpdateAsync(
                id,
                TextEditor.Text,
                lesson?.LessonId,
                TitleEntry.Text,
                SubjectEntry.Text,
                PinnedSwitch.IsToggled);
        }
        else
        {
            await _store.AddAsync(
                TextEditor.Text,
                lesson?.LessonId,
                TitleEntry.Text,
                SubjectEntry.Text,
                PinnedSwitch.IsToggled);
        }

        await Navigation.PopModalAsync();
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (_noteId is not Guid id ||
            !await DisplayAlertAsync("Удалить заметку?", "Это действие нельзя отменить.", "Удалить", "Отмена"))
        {
            return;
        }

        await _store.DeleteAsync(id);
        await Navigation.PopModalAsync();
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
