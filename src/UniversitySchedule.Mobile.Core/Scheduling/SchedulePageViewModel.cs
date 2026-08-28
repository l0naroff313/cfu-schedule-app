using System.Collections.ObjectModel;
using System.Windows.Input;
using UniversitySchedule.Contracts.Catalog;
using UniversitySchedule.Domain.Scheduling;
using UniversitySchedule.Mobile.Core.Presentation;

namespace UniversitySchedule.Mobile.Core.Scheduling;

public sealed class SchedulePageViewModel : ObservableObject
{
    private readonly TimeProvider _timeProvider;
    private IReadOnlyList<TeacherSummary> _allTeachers = [];
    private TeacherScheduleCoverage? _teacherSchedule;
    private ScheduleAudience _audience = ScheduleAudience.Group;
    private ScheduleRange _range = ScheduleRange.Day;
    private string _teacherQuery = string.Empty;
    private TeacherSummary? _selectedTeacher;
    private string _teacherStatusTitle = "Выберите преподавателя";
    private string _teacherSubjectText = string.Empty;
    private string _teacherLocationText = "Расписание появится после синхронизации.";

    public SchedulePageViewModel(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        SelectGroupCommand = new RelayCommand(() => Audience = ScheduleAudience.Group);
        SelectTeacherCommand = new RelayCommand(() => Audience = ScheduleAudience.Teacher);
        SelectDayCommand = new RelayCommand(() => Range = ScheduleRange.Day);
        SelectWeekCommand = new RelayCommand(() => Range = ScheduleRange.Week);
    }

    public ObservableCollection<TeacherSummary> TeacherOptions { get; } = [];

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
            OnPropertyChanged(nameof(ScheduleEmptyText));
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
            OnPropertyChanged(nameof(ScheduleEmptyText));
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
            OnPropertyChanged(nameof(ScheduleEmptyText));
            RefreshTeacherLocation();
        }
    }

    public bool IsGroupMode => Audience == ScheduleAudience.Group;

    public bool IsTeacherMode => Audience == ScheduleAudience.Teacher;

    public bool IsDayMode => Range == ScheduleRange.Day;

    public bool IsWeekMode => Range == ScheduleRange.Week;

    public bool HasTeacherOptions => TeacherOptions.Count > 0;

    public bool HasNoTeacherOptions => !HasTeacherOptions;

    public bool HasSelectedTeacher => SelectedTeacher is not null;

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
            if (IsGroupMode)
            {
                return Range == ScheduleRange.Day
                    ? "Расписание группы на день появится после первичной настройки."
                    : "Расписание группы на неделю появится после первичной настройки.";
            }

            if (SelectedTeacher is null)
            {
                return "Выберите преподавателя, чтобы открыть его расписание.";
            }

            return Range == ScheduleRange.Day
                ? "Расписание преподавателя на день появится после синхронизации."
                : "Расписание преподавателя на неделю появится после синхронизации.";
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
            SetTeacherStatus(
                "Выберите преподавателя",
                string.Empty,
                "Расписание появится после синхронизации.");
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (_teacherSchedule is null || !_teacherSchedule.Covers(SelectedTeacher.Id, now))
        {
            SetTeacherStatus(
                "Расписание ещё не загружено",
                string.Empty,
                "Аудитория появится после синхронизации.");
            return;
        }

        TeacherTeachingState state = TeacherScheduleTimeline.ResolveCurrent(
            _teacherSchedule.Lessons,
            SelectedTeacher.Id,
            now);

        switch (state.Status)
        {
            case TeacherTeachingStatus.NotTeaching:
                SetTeacherStatus(
                    "Сейчас нет пары",
                    string.Empty,
                    "Активное занятие не найдено.");
                break;
            case TeacherTeachingStatus.ConflictingScheduleData:
                SetTeacherStatus(
                    "Данные расписания расходятся",
                    state.SubjectName ?? string.Empty,
                    "Указано несколько аудиторий.");
                break;
            case TeacherTeachingStatus.Teaching:
                SetTeacherStatus(
                    "Сейчас преподаёт",
                    state.SubjectName ?? string.Empty,
                    FormatLocation(state.Building, state.Classroom));
                break;
            default:
                throw new InvalidOperationException($"Unknown teacher status: {state.Status}.");
        }
    }

    private void ApplyTeacherFilter()
    {
        string normalizedQuery = NormalizeForSearch(TeacherQuery);
        IEnumerable<TeacherSummary> matches = _allTeachers;

        if (normalizedQuery.Length > 0)
        {
            matches = matches.Where(teacher =>
                NormalizeForSearch(teacher.DisplayName).Contains(
                    normalizedQuery,
                    StringComparison.Ordinal));
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
            .ToArray();

        return parts.Length == 0
            ? "Аудитория не указана"
            : string.Join(" • ", parts);
    }

    private static string NormalizeForSearch(string value)
    {
        return string.Join(
                ' ',
                value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant()
            .Replace('ё', 'е');
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
