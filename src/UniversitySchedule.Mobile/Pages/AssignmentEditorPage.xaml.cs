using UniversitySchedule.Mobile.Core.Assignments;
using UniversitySchedule.Mobile.Core.Scheduling;

namespace UniversitySchedule.Mobile.Pages;

public partial class AssignmentEditorPage : ContentPage
{
    private readonly PersonalAssignmentStore _store;
    private readonly ScheduleSession _scheduleSession;
    private Guid? _assignmentId;
    private Guid? _preselectedLessonId;
    private string? _lastLessonSubject;
    private bool _loaded;

    public AssignmentEditorPage(PersonalAssignmentStore store, ScheduleSession scheduleSession)
    {
        _store = store;
        _scheduleSession = scheduleSession;
        InitializeComponent();
        StatusPicker.ItemsSource = AssignmentStatusOption.All.ToList();
        StatusPicker.SelectedIndex = 0;
        DeadlineDatePicker.Date = DateTime.Today.AddDays(1);
        DeadlineTimePicker.Time = new TimeSpan(23, 59, 0);
    }

    public void Configure(Guid? assignmentId = null, Guid? lessonId = null)
    {
        _assignmentId = assignmentId;
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
            // Assignments must remain editable offline even when no schedule was cached yet.
        }

        List<LessonPickerItem> lessons = LessonPickerItem.FromSession(_scheduleSession).ToList();
        PersonalAssignment? assignment = _assignmentId is Guid id ? await _store.GetAsync(id) : null;
        if (assignment is not null)
        {
            SubjectEntry.Text = assignment.Subject;
            TextEditor.Text = assignment.Text;
            StatusPicker.SelectedItem = AssignmentStatusOption.All.First(option => option.Value == assignment.Status);
            _preselectedLessonId = assignment.LessonId;
            DeleteButton.IsVisible = true;
            PreserveUnavailableLesson(lessons, assignment.LessonId, assignment.Subject);
            if (assignment.DeadlineUtc is DateTimeOffset deadlineUtc)
            {
                DateTimeOffset local = deadlineUtc.ToLocalTime();
                UseDeadlineSwitch.IsToggled = true;
                DeadlineDatePicker.Date = local.Date;
                DeadlineTimePicker.Time = local.TimeOfDay;
            }
        }

        LessonPicker.ItemsSource = lessons;
        LessonPicker.SelectedItem = lessons.FirstOrDefault(item => item.LessonId == _preselectedLessonId)
            ?? lessons[0];
        _lastLessonSubject = (LessonPicker.SelectedItem as LessonPickerItem)?.Subject;
        RefreshDeadlineControls();
    }

    private static void PreserveUnavailableLesson(
        List<LessonPickerItem> lessons,
        Guid? lessonId,
        string subject)
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

    private void OnUseDeadlineToggled(object? sender, ToggledEventArgs e) => RefreshDeadlineControls();

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TextEditor.Text))
        {
            await DisplayAlertAsync("Нужно задание", "Опишите, что необходимо сделать.", "Хорошо");
            return;
        }

        LessonPickerItem? lesson = LessonPicker.SelectedItem as LessonPickerItem;
        AssignmentStatusOption status = StatusPicker.SelectedItem as AssignmentStatusOption
            ?? AssignmentStatusOption.All[0];
        DateTimeOffset? deadlineUtc = BuildDeadlineUtc();
        if (_assignmentId is Guid id)
        {
            await _store.UpdateAsync(
                id,
                SubjectEntry.Text,
                TextEditor.Text,
                lesson?.LessonId,
                deadlineUtc,
                status.Value);
        }
        else
        {
            await _store.AddAsync(
                SubjectEntry.Text,
                TextEditor.Text,
                lesson?.LessonId,
                deadlineUtc,
                status.Value);
        }

        await Navigation.PopModalAsync();
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (_assignmentId is not Guid id ||
            !await DisplayAlertAsync("Удалить задание?", "Это действие нельзя отменить.", "Удалить", "Отмена"))
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

    private void RefreshDeadlineControls()
    {
        DeadlineDatePicker.IsEnabled = UseDeadlineSwitch.IsToggled;
        DeadlineTimePicker.IsEnabled = UseDeadlineSwitch.IsToggled;
    }

    private DateTimeOffset? BuildDeadlineUtc()
    {
        if (!UseDeadlineSwitch.IsToggled)
        {
            return null;
        }

        DateTime local = DateTime.SpecifyKind(
            (DeadlineDatePicker.Date ?? DateTime.Today).Date + (DeadlineTimePicker.Time ?? TimeSpan.Zero),
            DateTimeKind.Unspecified);
        TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    private sealed record AssignmentStatusOption(string Name, PersonalAssignmentStatus Value)
    {
        public static IReadOnlyList<AssignmentStatusOption> All { get; } =
        [
            new("Новое", PersonalAssignmentStatus.New),
            new("В работе", PersonalAssignmentStatus.InProgress),
            new("Выполнено", PersonalAssignmentStatus.Completed),
        ];

        public override string ToString() => Name;
    }
}
