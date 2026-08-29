using System.Collections.ObjectModel;
using System.Globalization;
using UniversitySchedule.Contracts.Schedule;
using UniversitySchedule.Mobile.Core.Assignments;
using UniversitySchedule.Mobile.Core.Presentation;

namespace UniversitySchedule.Mobile.Core.Scheduling;

public sealed record TodayLessonCard(
    Guid Id,
    string Caption,
    string PairText,
    string TimeText,
    string Subject,
    string Details,
    string Location,
    string Teachers,
    double Progress,
    string ProgressText);

public sealed record TodayScheduleRow(
    Guid Id,
    string PairText,
    string TimeText,
    string Subject,
    string Details,
    bool IsCurrent);

public sealed record TodayAssignmentRow(
    Guid Id,
    string Text,
    string Subject,
    string DeadlineText);

public sealed class TodayPageViewModel : ObservableObject
{
    private static readonly TimeSpan UniversityUtcOffset = TimeSpan.FromHours(3);
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");
    private readonly TimeProvider _timeProvider;
    private readonly ScheduleSession _scheduleSession;
    private readonly PersonalAssignmentStore _assignmentStore;
    private IReadOnlyList<PersonalAssignment> _assignments = [];
    private TodayLessonCard? _currentLesson;
    private TodayLessonCard? _nextLesson;
    private string _dateText = string.Empty;
    private string _groupText = "Учебный профиль не выбран";
    private string _statusText = "Выберите группу, чтобы загрузить расписание.";
    private bool _isLoading;

    public TodayPageViewModel(
        TimeProvider timeProvider,
        ScheduleSession scheduleSession,
        PersonalAssignmentStore assignmentStore)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _scheduleSession = scheduleSession ?? throw new ArgumentNullException(nameof(scheduleSession));
        _assignmentStore = assignmentStore ?? throw new ArgumentNullException(nameof(assignmentStore));
        _scheduleSession.Changed += OnScheduleChanged;
    }

    public ObservableCollection<TodayScheduleRow> TodayLessons { get; } = [];

    public ObservableCollection<TodayAssignmentRow> TodayAssignments { get; } = [];

    public TodayLessonCard? CurrentLesson
    {
        get => _currentLesson;
        private set
        {
            if (SetProperty(ref _currentLesson, value))
            {
                OnPropertyChanged(nameof(HasCurrentLesson));
            }
        }
    }

    public TodayLessonCard? NextLesson
    {
        get => _nextLesson;
        private set
        {
            if (SetProperty(ref _nextLesson, value))
            {
                OnPropertyChanged(nameof(HasNextLesson));
            }
        }
    }

    public bool HasCurrentLesson => CurrentLesson is not null;

    public bool HasNextLesson => NextLesson is not null;

    public bool HasTodayLessons => TodayLessons.Count > 0;

    public bool HasTodayAssignments => TodayAssignments.Count > 0;

    public string DateText
    {
        get => _dateText;
        private set => SetProperty(ref _dateText, value);
    }

    public string GroupText
    {
        get => _groupText;
        private set => SetProperty(ref _groupText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            await _scheduleSession.InitializeAsync(cancellationToken);
            _assignments = await _assignmentStore.GetAllAsync(cancellationToken);
            Refresh();
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Refresh()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateTimeOffset universityNow = now.ToOffset(UniversityUtcOffset);
        DateOnly today = DateOnly.FromDateTime(universityNow.DateTime);
        DateText = Capitalize(universityNow.ToString("dddd, d MMMM", RussianCulture));

        if (_scheduleSession.Profile is null || _scheduleSession.Snapshot is null)
        {
            GroupText = "Учебный профиль не выбран";
            StatusText = "Выберите институт, направление, курс, группу и подгруппу.";
            CurrentLesson = null;
            NextLesson = null;
            TodayLessons.Clear();
            TodayAssignments.Clear();
            NotifyCollectionState();
            return;
        }

        GroupText = _scheduleSession.Profile.SubgroupName is null
            ? _scheduleSession.Profile.GroupName
            : $"{_scheduleSession.Profile.GroupName} • {_scheduleSession.Profile.SubgroupName}";
        ScheduleLesson[] activeLessons = _scheduleSession.Snapshot.Lessons
            .Where(lesson => !string.Equals(lesson.Status, "отменено", StringComparison.OrdinalIgnoreCase))
            .OrderBy(lesson => lesson.StartsAtUtc)
            .ThenBy(lesson => lesson.PairNumber)
            .ToArray();
        ScheduleLesson[] upcoming = activeLessons
            .Where(lesson => lesson.EndsAtUtc > now)
            .ToArray();
        ScheduleLesson? current = upcoming.FirstOrDefault(lesson => lesson.StartsAtUtc <= now);
        ScheduleLesson? next = upcoming.FirstOrDefault(lesson => lesson.StartsAtUtc > now);

        CurrentLesson = current is null ? null : CreateCard("Сейчас", current, today, now);
        NextLesson = next is null ? null : CreateCard(
            current is null ? "Ближайшая пара" : "Следующая пара",
            next,
            today,
            now);

        TodayLessons.Clear();
        foreach (ScheduleLesson lesson in activeLessons.Where(lesson => lesson.Date == today))
        {
            TodayLessons.Add(new TodayScheduleRow(
                lesson.Id,
                $"{lesson.PairNumber}",
                $"{lesson.StartsAtUtc.ToOffset(UniversityUtcOffset):HH:mm}–{lesson.EndsAtUtc.ToOffset(UniversityUtcOffset):HH:mm}",
                lesson.Subject,
                JoinOptional(
                    string.Join(", ", lesson.Teachers.Select(teacher => teacher.DisplayName)),
                    FormatLocation(lesson.Building, lesson.Classroom)),
                lesson.StartsAtUtc <= now && now < lesson.EndsAtUtc));
        }

        TodayAssignments.Clear();
        foreach (PersonalAssignment assignment in _assignments
                     .Where(item => item.Status != PersonalAssignmentStatus.Completed)
                     .OrderBy(item => item.DeadlineUtc ?? DateTimeOffset.MaxValue)
                     .ThenByDescending(item => item.UpdatedAtUtc)
                     .Take(3))
        {
            TodayAssignments.Add(new TodayAssignmentRow(
                assignment.Id,
                assignment.Text,
                assignment.Subject,
                assignment.DeadlineUtc is DateTimeOffset deadline
                    ? $"До {deadline.ToLocalTime():dd.MM, HH:mm}"
                    : "Без дедлайна"));
        }

        NotifyCollectionState();
        StatusText = _scheduleSession.UpdatedAtUtc is DateTimeOffset updatedAt
            ? $"{(_scheduleSession.IsFromCache ? "Офлайн-копия" : "Обновлено")} {updatedAt.ToLocalTime():dd.MM.yyyy HH:mm}"
            : "Расписание загружено.";
    }

    private static TodayLessonCard CreateCard(
        string caption,
        ScheduleLesson lesson,
        DateOnly today,
        DateTimeOffset now)
    {
        DateTimeOffset startsAt = lesson.StartsAtUtc.ToOffset(UniversityUtcOffset);
        DateTimeOffset endsAt = lesson.EndsAtUtc.ToOffset(UniversityUtcOffset);
        string datePrefix = DateOnly.FromDateTime(startsAt.DateTime) == today
            ? string.Empty
            : $"{startsAt:dd.MM} • ";
        double progress = now <= lesson.StartsAtUtc
            ? 0
            : Math.Clamp(
                (now - lesson.StartsAtUtc).TotalSeconds / (lesson.EndsAtUtc - lesson.StartsAtUtc).TotalSeconds,
                0,
                1);
        string progressText = now < lesson.StartsAtUtc
            ? $"Начнётся через {FormatDuration(lesson.StartsAtUtc - now)}"
            : $"Осталось {FormatDuration(lesson.EndsAtUtc - now)}";

        return new TodayLessonCard(
            lesson.Id,
            caption,
            $"{lesson.PairNumber} пара",
            $"{datePrefix}{startsAt:HH:mm}–{endsAt:HH:mm}",
            lesson.Subject,
            JoinOptional(lesson.LessonType, lesson.Status),
            FormatLocation(lesson.Building, lesson.Classroom),
            string.Join(", ", lesson.Teachers.Select(teacher => teacher.DisplayName)),
            progress,
            progressText);
    }

    private static string FormatLocation(string? building, string? classroom)
    {
        string location = JoinOptional(building, classroom);
        return location.Length == 0 ? "Аудитория не указана" : location;
    }

    private static string JoinOptional(params string?[] values) => string.Join(
        " • ",
        values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()));

    private static string FormatDuration(TimeSpan duration)
    {
        int minutes = Math.Max(0, (int)Math.Ceiling(duration.TotalMinutes));
        return minutes >= 60 ? $"{minutes / 60} ч {minutes % 60} мин" : $"{minutes} мин";
    }

    private static string Capitalize(string value) => value.Length == 0
        ? value
        : $"{char.ToUpper(value[0], RussianCulture)}{value[1..]}";

    private void NotifyCollectionState()
    {
        OnPropertyChanged(nameof(HasTodayLessons));
        OnPropertyChanged(nameof(HasTodayAssignments));
    }

    private void OnScheduleChanged(object? sender, EventArgs e) => Refresh();
}
