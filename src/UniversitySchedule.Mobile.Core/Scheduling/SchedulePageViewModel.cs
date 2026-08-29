using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using UniversitySchedule.Contracts.Catalog;
using UniversitySchedule.Contracts.Schedule;
using UniversitySchedule.Domain.Scheduling;
using UniversitySchedule.Mobile.Core.Cfu;
using UniversitySchedule.Mobile.Core.Presentation;

namespace UniversitySchedule.Mobile.Core.Scheduling;

public sealed record ScheduleLessonListItem(
    Guid Id,
    string DateText,
    string PairText,
    string TimeText,
    string Subject,
    string Details,
    string Location,
    string Teachers,
    bool IsCurrent);

public sealed class SchedulePageViewModel : ObservableObject
{
    private static readonly TimeSpan UniversityUtcOffset = TimeSpan.FromHours(3);
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

    private readonly TimeProvider _timeProvider;
    private readonly ScheduleSession? _scheduleSession;
    private readonly CfuScheduleRepository? _scheduleRepository;
    private readonly Dictionary<Guid, IReadOnlyList<ScheduleLesson>> _teacherLessons = [];
    private IReadOnlyList<TeacherSummary> _allTeachers = [];
    private IReadOnlyList<ScheduleLesson> _scheduleLessons = [];
    private TeacherScheduleCoverage? _teacherSchedule;
    private ScheduleAudience _audience = ScheduleAudience.Group;
    private ScheduleRange _range = ScheduleRange.Day;
    private string _teacherQuery = string.Empty;
    private TeacherSummary? _selectedTeacher;
    private string _teacherStatusTitle = "Выберите преподавателя";
    private string _teacherSubjectText = string.Empty;
    private string _teacherLocationText = "Расписание появится после синхронизации.";
    private string _syncStatusText = "Расписание ещё не загружено.";
    private bool _isLoading;

    public SchedulePageViewModel(TimeProvider timeProvider)
        : this(timeProvider, null, null)
    {
    }

    public SchedulePageViewModel(
        TimeProvider timeProvider,
        ScheduleSession? scheduleSession,
        CfuScheduleRepository? scheduleRepository)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _scheduleSession = scheduleSession;
        _scheduleRepository = scheduleRepository;

        SelectGroupCommand = new RelayCommand(() => Audience = ScheduleAudience.Group);
        SelectTeacherCommand = new RelayCommand(() => Audience = ScheduleAudience.Teacher);
        SelectDayCommand = new RelayCommand(() => Range = ScheduleRange.Day);
        SelectWeekCommand = new RelayCommand(() => Range = ScheduleRange.Week);

        if (_scheduleSession is not null)
        {
            _scheduleSession.Changed += OnScheduleSessionChanged;
        }
    }

    public ObservableCollection<TeacherSummary> TeacherOptions { get; } = [];

    public ObservableCollection<ScheduleLessonListItem> Lessons { get; } = [];

    public ICommand SelectGroupCommand { get; }

    public ICommand SelectTeacherCommand { get; }

    public ICommand SelectDayCommand { get; }

    public ICommand SelectWeekCommand { get; }

    public ScheduleAudience Audience
    {
        get => _audience;
        private set
        {
            if (!SetProperty(ref _audience, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsGroupMode));
            OnPropertyChanged(nameof(IsTeacherMode));
            RefreshVisibleLessons();
        }
    }

    public ScheduleRange Range
    {
        get => _range;
        private set
        {
            if (!SetProperty(ref _range, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsDayMode));
            OnPropertyChanged(nameof(IsWeekMode));
            RefreshVisibleLessons();
        }
    }

    public string TeacherQuery
    {
        get => _teacherQuery;
        set
        {
            if (SetProperty(ref _teacherQuery, value ?? string.Empty))
            {
                ApplyTeacherFilter();
            }
        }
    }

    public TeacherSummary? SelectedTeacher
    {
        get => _selectedTeacher;
        set
        {
            if (!SetProperty(ref _selectedTeacher, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasSelectedTeacher));
            RefreshTeacherLocation();
            RefreshVisibleLessons();
            if (value is not null && _scheduleRepository is not null)
            {
                _ = LoadSelectedTeacherScheduleAsync(value);
            }
        }
    }

    public bool IsGroupMode => Audience == ScheduleAudience.Group;

    public bool IsTeacherMode => Audience == ScheduleAudience.Teacher;

    public bool IsDayMode => Range == ScheduleRange.Day;

    public bool IsWeekMode => Range == ScheduleRange.Week;

    public bool HasTeacherOptions => TeacherOptions.Count > 0;

    public bool HasNoTeacherOptions => !HasTeacherOptions;

    public bool HasSelectedTeacher => SelectedTeacher is not null;

    public bool HasLessons => Lessons.Count > 0;

    public bool HasNoLessons => !HasLessons;

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string SyncStatusText
    {
        get => _syncStatusText;
        private set => SetProperty(ref _syncStatusText, value);
    }

    public string TeacherEmptyText => _allTeachers.Count == 0
        ? "Список преподавателей появится после синхронизации расписания."
        : "Преподаватель не найден.";

    public string TeacherStatusTitle
    {
        get => _teacherStatusTitle;
        private set => SetProperty(ref _teacherStatusTitle, value);
    }

    public string TeacherSubjectText
    {
        get => _teacherSubjectText;
        private set => SetProperty(ref _teacherSubjectText, value);
    }

    public string TeacherLocationText
    {
        get => _teacherLocationText;
        private set => SetProperty(ref _teacherLocationText, value);
    }

    public string ScheduleEmptyText
    {
        get
        {
            if (_scheduleSession?.Profile is null && _scheduleLessons.Count == 0)
            {
                return "Сначала выберите учебную группу в профиле.";
            }

            if (IsTeacherMode && SelectedTeacher is null)
            {
                return "Выберите преподавателя, чтобы открыть его расписание.";
            }

            return Range == ScheduleRange.Day
                ? "На сегодня занятий нет."
                : "На этой неделе занятий нет.";
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_scheduleSession is null)
        {
            return;
        }

        IsLoading = true;
        try
        {
            await _scheduleSession.InitializeAsync(cancellationToken);
            ApplySession();
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task SearchTeachersAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (_scheduleRepository is null || string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
        {
            return;
        }

        IsLoading = true;
        try
        {
            CfuTeacherSearchLoadResult result = await _scheduleRepository.SearchTeachersAsync(
                query,
                cancellationToken);
            _teacherLessons.Clear();
            foreach (TeacherSummary teacher in result.Search.Teachers)
            {
                _teacherLessons[teacher.Id] = result.Search.Lessons
                    .Where(lesson => lesson.Teachers.Any(item => item.Id == teacher.Id))
                    .ToArray();
            }

            SetTeachers(result.Search.Teachers);
            SyncStatusText = FormatSyncStatus(result.UpdatedAtUtc, result.IsFromCache);
        }
        catch (InvalidOperationException)
        {
            SyncStatusText = "Поиск недоступен: нет сети и сохранённой копии.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void SetTeachers(IEnumerable<TeacherSummary> teachers)
    {
        ArgumentNullException.ThrowIfNull(teachers);

        Guid? selectedTeacherId = SelectedTeacher?.Id;
        _allTeachers = teachers
            .Where(teacher => teacher.Id != Guid.Empty)
            .GroupBy(teacher => teacher.Id)
            .Select(group => group.First())
            .OrderBy(teacher => teacher.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        SelectedTeacher = selectedTeacherId is null
            ? null
            : _allTeachers.FirstOrDefault(teacher => teacher.Id == selectedTeacherId);
        ApplyTeacherFilter();
    }

    public void SetTeacherSchedule(TeacherScheduleCoverage teacherSchedule)
    {
        _teacherSchedule = teacherSchedule ?? throw new ArgumentNullException(nameof(teacherSchedule));
        RefreshTeacherLocation();
    }

    public void RefreshTeacherLocation()
    {
        if (SelectedTeacher is null)
        {
            SetTeacherStatus("Выберите преподавателя", string.Empty, "Расписание появится после синхронизации.");
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (_teacherSchedule is null || !_teacherSchedule.Covers(SelectedTeacher.Id, now))
        {
            SetTeacherStatus("Расписание ещё не загружено", string.Empty, "Аудитория появится после синхронизации.");
            return;
        }

        TeacherTeachingState state = TeacherScheduleTimeline.ResolveCurrent(
            _teacherSchedule.Lessons,
            SelectedTeacher.Id,
            now);

        switch (state.Status)
        {
            case TeacherTeachingStatus.NotTeaching:
                SetTeacherStatus("Сейчас нет пары", string.Empty, "Активное занятие не найдено.");
                break;
            case TeacherTeachingStatus.ConflictingScheduleData:
                SetTeacherStatus("Данные расписания расходятся", state.SubjectName ?? string.Empty, "Указано несколько аудиторий.");
                break;
            case TeacherTeachingStatus.Teaching:
                SetTeacherStatus("Сейчас преподаёт", state.SubjectName ?? string.Empty, FormatLocation(state.Building, state.Classroom));
                break;
            default:
                throw new InvalidOperationException($"Unknown teacher status: {state.Status}.");
        }
    }

    private async Task LoadSelectedTeacherScheduleAsync(TeacherSummary teacher)
    {
        if (_teacherLessons.TryGetValue(teacher.Id, out IReadOnlyList<ScheduleLesson>? cachedLessons))
        {
            ApplyTeacherCoverage(teacher, cachedLessons);
            return;
        }

        try
        {
            CfuTeacherSearchLoadResult result = await _scheduleRepository!.SearchTeachersAsync(teacher.DisplayName);
            TeacherSummary? exactTeacher = result.Search.Teachers.FirstOrDefault(item => item.Id == teacher.Id);
            if (exactTeacher is null || SelectedTeacher?.Id != teacher.Id)
            {
                return;
            }

            IReadOnlyList<ScheduleLesson> lessons = result.Search.Lessons
                .Where(lesson => lesson.Teachers.Any(item => item.Id == teacher.Id))
                .ToArray();
            _teacherLessons[teacher.Id] = lessons;
            ApplyTeacherCoverage(teacher, lessons, result.Search.From, result.Search.To);
            RefreshVisibleLessons();
            SyncStatusText = FormatSyncStatus(result.UpdatedAtUtc, result.IsFromCache);
        }
        catch (InvalidOperationException)
        {
            // Keep the group-derived list when no full teacher schedule is available.
        }
    }

    private void ApplySession()
    {
        if (_scheduleSession?.Snapshot is null)
        {
            _scheduleLessons = [];
            SyncStatusText = "Расписание ещё не загружено.";
            SetTeachers([]);
            RefreshVisibleLessons();
            return;
        }

        _scheduleLessons = _scheduleSession.Snapshot.Lessons;
        SetTeachers(_scheduleLessons
            .SelectMany(lesson => lesson.Teachers)
            .GroupBy(teacher => teacher.Id)
            .Select(group => group.First()));
        if (_scheduleSession.UpdatedAtUtc is DateTimeOffset updatedAt)
        {
            SyncStatusText = FormatSyncStatus(updatedAt, _scheduleSession.IsFromCache);
        }

        RefreshVisibleLessons();
    }

    private void OnScheduleSessionChanged(object? sender, EventArgs e)
    {
        ApplySession();
    }

    private void ApplyTeacherCoverage(
        TeacherSummary teacher,
        IReadOnlyList<ScheduleLesson> lessons,
        DateOnly? from = null,
        DateOnly? to = null)
    {
        IReadOnlyList<LessonOccurrence> occurrences = lessons
            .Select(lesson => new LessonOccurrence(
                lesson.Id,
                lesson.Subject,
                lesson.PairNumber,
                lesson.StartsAtUtc,
                lesson.EndsAtUtc,
                teacherIds: lesson.Teachers.Select(item => item.Id),
                classroom: lesson.Classroom,
                building: lesson.Building))
            .ToArray();
        DateOnly coverageFrom = from ?? _scheduleSession?.Snapshot?.From ?? TodayAtUniversity();
        DateOnly coverageTo = to ?? _scheduleSession?.Snapshot?.To ?? TodayAtUniversity();
        DateTimeOffset startsAt = new(coverageFrom, TimeOnly.MinValue, UniversityUtcOffset);
        DateTimeOffset endsAt = new(coverageTo.AddDays(1), TimeOnly.MinValue, UniversityUtcOffset);
        SetTeacherSchedule(new TeacherScheduleCoverage(teacher.Id, startsAt, endsAt, occurrences));
    }

    private void RefreshVisibleLessons()
    {
        DateOnly today = TodayAtUniversity();
        DateOnly monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        DateOnly end = Range == ScheduleRange.Day ? today : monday.AddDays(6);
        DateOnly start = Range == ScheduleRange.Day ? today : monday;
        IEnumerable<ScheduleLesson> source = IsTeacherMode && SelectedTeacher is not null
            ? _teacherLessons.GetValueOrDefault(SelectedTeacher.Id, _scheduleLessons)
                .Where(lesson => lesson.Teachers.Any(teacher => teacher.Id == SelectedTeacher.Id))
            : _scheduleLessons;
        DateTimeOffset now = _timeProvider.GetUtcNow();
        ScheduleLessonListItem[] visible = source
            .Where(lesson => lesson.Date >= start && lesson.Date <= end)
            .OrderBy(lesson => lesson.Date)
            .ThenBy(lesson => lesson.PairNumber)
            .Select(lesson => new ScheduleLessonListItem(
                lesson.Id,
                lesson.Date.ToString("dddd, d MMMM", RussianCulture),
                $"{lesson.PairNumber} пара",
                $"{lesson.StartsAtUtc.ToOffset(UniversityUtcOffset):HH:mm}–{lesson.EndsAtUtc.ToOffset(UniversityUtcOffset):HH:mm}",
                lesson.Subject,
                JoinOptional(lesson.LessonType, lesson.Status),
                FormatLocation(lesson.Building, lesson.Classroom),
                string.Join(", ", lesson.Teachers.Select(teacher => teacher.DisplayName)),
                lesson.StartsAtUtc <= now && now < lesson.EndsAtUtc))
            .ToArray();

        Lessons.Clear();
        foreach (ScheduleLessonListItem item in visible)
        {
            Lessons.Add(item);
        }

        OnPropertyChanged(nameof(HasLessons));
        OnPropertyChanged(nameof(HasNoLessons));
        OnPropertyChanged(nameof(ScheduleEmptyText));
    }

    private void ApplyTeacherFilter()
    {
        string normalizedQuery = NormalizeForSearch(TeacherQuery);
        IEnumerable<TeacherSummary> matches = _allTeachers;

        if (normalizedQuery.Length > 0)
        {
            matches = matches.Where(teacher =>
                NormalizeForSearch(teacher.DisplayName).Contains(normalizedQuery, StringComparison.Ordinal));
        }

        TeacherOptions.Clear();
        foreach (TeacherSummary teacher in matches.Take(50))
        {
            TeacherOptions.Add(teacher);
        }

        OnPropertyChanged(nameof(HasTeacherOptions));
        OnPropertyChanged(nameof(HasNoTeacherOptions));
        OnPropertyChanged(nameof(TeacherEmptyText));
    }

    private void SetTeacherStatus(string title, string subject, string location)
    {
        TeacherStatusTitle = title;
        TeacherSubjectText = subject;
        TeacherLocationText = location;
    }

    private static string FormatLocation(string? building, string? classroom)
    {
        string[] parts = new[] { building, classroom }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        return parts.Length == 0 ? "Аудитория не указана" : string.Join(" • ", parts);
    }

    private static string JoinOptional(params string?[] values)
    {
        return string.Join(" • ", values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim()));
    }

    private static string NormalizeForSearch(string value)
    {
        return string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant()
            .Replace('ё', 'е');
    }

    private static string FormatSyncStatus(DateTimeOffset updatedAtUtc, bool isFromCache)
    {
        string prefix = isFromCache ? "Офлайн-копия" : "Обновлено";
        return $"{prefix} {updatedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm}";
    }

    private DateOnly TodayAtUniversity()
    {
        return DateOnly.FromDateTime(_timeProvider.GetUtcNow().ToOffset(UniversityUtcOffset).DateTime);
    }
}

public enum ScheduleAudience
{
    Group = 0,
    Teacher = 1,
}

public enum ScheduleRange
{
    Day = 0,
    Week = 1,
}
