using UniversitySchedule.Contracts.Schedule;
using UniversitySchedule.Mobile.Core.Presentation;

namespace UniversitySchedule.Mobile.Core.Scheduling;

public sealed record TodayLessonCard(
    string Caption,
    string PairText,
    string TimeText,
    string Subject,
    string Details,
    string Location,
    string Teachers);

public sealed class TodayPageViewModel : ObservableObject
{
    private static readonly TimeSpan UniversityUtcOffset = TimeSpan.FromHours(3);
    private readonly TimeProvider _timeProvider;
    private readonly ScheduleSession _scheduleSession;
    private TodayLessonCard? _currentLesson;
    private TodayLessonCard? _nextLesson;
    private string _groupText = "Учебный профиль не выбран";
    private string _statusText = "Выберите группу, чтобы загрузить расписание.";
    private bool _isLoading;

    public TodayPageViewModel(TimeProvider timeProvider, ScheduleSession scheduleSession)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _scheduleSession = scheduleSession ?? throw new ArgumentNullException(nameof(scheduleSession));
        _scheduleSession.Changed += OnScheduleChanged;
    }

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
            Refresh();
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Refresh()
    {
        if (_scheduleSession.Profile is null || _scheduleSession.Snapshot is null)
        {
            GroupText = "Учебный профиль не выбран";
            StatusText = "Выберите институт, направление, курс, группу и подгруппу.";
            CurrentLesson = null;
            NextLesson = null;
            return;
        }

        GroupText = _scheduleSession.Profile.SubgroupName is null
            ? _scheduleSession.Profile.GroupName
            : $"{_scheduleSession.Profile.GroupName} • {_scheduleSession.Profile.SubgroupName}";
        DateTimeOffset now = _timeProvider.GetUtcNow();
        ScheduleLesson[] upcoming = _scheduleSession.Snapshot.Lessons
            .Where(lesson => lesson.EndsAtUtc > now &&
                             !string.Equals(lesson.Status, "отменено", StringComparison.OrdinalIgnoreCase))
            .OrderBy(lesson => lesson.StartsAtUtc)
            .ThenBy(lesson => lesson.PairNumber)
            .ToArray();
        ScheduleLesson? current = upcoming.FirstOrDefault(lesson => lesson.StartsAtUtc <= now);
        ScheduleLesson? next = upcoming.FirstOrDefault(lesson => lesson.StartsAtUtc > now);

        DateOnly today = DateOnly.FromDateTime(now.ToOffset(UniversityUtcOffset).DateTime);
        CurrentLesson = current is null ? null : CreateCard("Сейчас", current, today);
        NextLesson = next is null ? null : CreateCard(
            current is null ? "Ближайшая пара" : "Следующая пара",
            next,
            today);
        StatusText = _scheduleSession.UpdatedAtUtc is DateTimeOffset updatedAt
            ? $"{(_scheduleSession.IsFromCache ? "Офлайн-копия" : "Обновлено")} {updatedAt.ToLocalTime():dd.MM.yyyy HH:mm}"
            : "Расписание загружено.";
    }

    private static TodayLessonCard CreateCard(
        string caption,
        ScheduleLesson lesson,
        DateOnly today)
    {
        DateTimeOffset startsAt = lesson.StartsAtUtc.ToOffset(UniversityUtcOffset);
        DateTimeOffset endsAt = lesson.EndsAtUtc.ToOffset(UniversityUtcOffset);
        string datePrefix = DateOnly.FromDateTime(startsAt.DateTime) == today
            ? string.Empty
            : $"{startsAt:dd.MM} • ";
        string location = string.Join(
            " • ",
            new[] { lesson.Building, lesson.Classroom }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .Distinct(StringComparer.CurrentCultureIgnoreCase));

        return new TodayLessonCard(
            caption,
            $"{lesson.PairNumber} пара",
            $"{datePrefix}{startsAt:HH:mm}–{endsAt:HH:mm}",
            lesson.Subject,
            string.Join(" • ", new[] { lesson.LessonType, lesson.Status }
                .Where(value => !string.IsNullOrWhiteSpace(value))),
            string.IsNullOrWhiteSpace(location) ? "Аудитория не указана" : location,
            string.Join(", ", lesson.Teachers.Select(teacher => teacher.DisplayName)));
    }

    private void OnScheduleChanged(object? sender, EventArgs e)
    {
        Refresh();
    }
}
