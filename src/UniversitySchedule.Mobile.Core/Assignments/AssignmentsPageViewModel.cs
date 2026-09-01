using System.Collections.ObjectModel;
using System.Windows.Input;
using UniversitySchedule.Mobile.Core.Presentation;

namespace UniversitySchedule.Mobile.Core.Assignments;

public enum AssignmentFilter
{
    All = 0,
    Today = 1,
    Week = 2,
    Completed = 3,
}

public sealed record AssignmentListItem(
    Guid Id,
    Guid? LessonId,
    string Text,
    string Subject,
    string DeadlineText,
    string StatusText,
    string CompletionGlyph,
    bool IsCompleted,
    bool IsOverdue);

public sealed class AssignmentsPageViewModel : ObservableObject
{
    private readonly PersonalAssignmentStore _store;
    private readonly TimeProvider _timeProvider;
    private IReadOnlyList<PersonalAssignment> _allAssignments = [];
    private AssignmentFilter _filter;
    private bool _isLoading;

    public AssignmentsPageViewModel(PersonalAssignmentStore store, TimeProvider timeProvider)
    {
        _store = store;
        _timeProvider = timeProvider;
        ShowAllCommand = new RelayCommand(() => Filter = AssignmentFilter.All);
        ShowTodayCommand = new RelayCommand(() => Filter = AssignmentFilter.Today);
        ShowWeekCommand = new RelayCommand(() => Filter = AssignmentFilter.Week);
        ShowCompletedCommand = new RelayCommand(() => Filter = AssignmentFilter.Completed);
    }

    public ObservableCollection<AssignmentListItem> Assignments { get; } = [];

    public ICommand ShowAllCommand { get; }

    public ICommand ShowTodayCommand { get; }

    public ICommand ShowWeekCommand { get; }

    public ICommand ShowCompletedCommand { get; }

    public AssignmentFilter Filter
    {
        get => _filter;
        private set
        {
            if (SetProperty(ref _filter, value))
            {
                NotifyFilterState();
                ApplyFilter();
            }
        }
    }

    public bool IsAllFilter => Filter == AssignmentFilter.All;

    public bool IsTodayFilter => Filter == AssignmentFilter.Today;

    public bool IsWeekFilter => Filter == AssignmentFilter.Week;

    public bool IsCompletedFilter => Filter == AssignmentFilter.Completed;

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool HasAssignments => Assignments.Count > 0;

    public bool HasNoAssignments => !HasAssignments;

    public int TotalCount => _allAssignments.Count;

    public int CompletedCount => _allAssignments.Count(item => item.Status == PersonalAssignmentStatus.Completed);

    public double Progress => TotalCount == 0 ? 0 : (double)CompletedCount / TotalCount;

    public string ProgressText => $"{CompletedCount} из {TotalCount} выполнено";

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            _allAssignments = await _store.GetAllAsync(cancellationToken);
            ApplyFilter();
            NotifyProgress();
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task ToggleCompletedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        PersonalAssignment? assignment = _allAssignments.FirstOrDefault(item => item.Id == id);
        if (assignment is null)
        {
            return;
        }

        PersonalAssignmentStatus status = assignment.Status == PersonalAssignmentStatus.Completed
            ? PersonalAssignmentStatus.InProgress
            : PersonalAssignmentStatus.Completed;
        await _store.SetStatusAsync(id, status, cancellationToken);
        await LoadAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await _store.DeleteAsync(id, cancellationToken))
        {
            await LoadAsync(cancellationToken);
        }
    }

    private void ApplyFilter()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow().ToLocalTime();
        DateOnly today = DateOnly.FromDateTime(now.DateTime);
        DateOnly weekEnd = today.AddDays(7);
        IEnumerable<PersonalAssignment> filtered = Filter switch
        {
            AssignmentFilter.Today => _allAssignments.Where(item =>
                item.DeadlineUtc is DateTimeOffset deadline &&
                DateOnly.FromDateTime(deadline.ToLocalTime().DateTime) == today),
            AssignmentFilter.Week => _allAssignments.Where(item =>
                item.DeadlineUtc is DateTimeOffset deadline &&
                DateOnly.FromDateTime(deadline.ToLocalTime().DateTime) >= today &&
                DateOnly.FromDateTime(deadline.ToLocalTime().DateTime) <= weekEnd),
            AssignmentFilter.Completed => _allAssignments.Where(item => item.Status == PersonalAssignmentStatus.Completed),
            _ => _allAssignments,
        };

        Assignments.Clear();
        foreach (PersonalAssignment assignment in filtered
                     .OrderBy(item => item.Status == PersonalAssignmentStatus.Completed)
                     .ThenBy(item => item.DeadlineUtc ?? DateTimeOffset.MaxValue)
                     .ThenByDescending(item => item.UpdatedAtUtc))
        {
            Assignments.Add(ToListItem(assignment, now));
        }

        OnPropertyChanged(nameof(HasAssignments));
        OnPropertyChanged(nameof(HasNoAssignments));
    }

    private static AssignmentListItem ToListItem(PersonalAssignment assignment, DateTimeOffset now)
    {
        bool completed = assignment.Status == PersonalAssignmentStatus.Completed;
        bool overdue = !completed && assignment.DeadlineUtc is DateTimeOffset deadline && deadline < now.ToUniversalTime();
        return new AssignmentListItem(
            assignment.Id,
            assignment.LessonId,
            assignment.Text,
            assignment.Subject,
            FormatDeadline(assignment.DeadlineUtc),
            assignment.Status switch
            {
                PersonalAssignmentStatus.New => "Новое",
                PersonalAssignmentStatus.InProgress => "В работе",
                PersonalAssignmentStatus.Completed => "Выполнено",
                _ => string.Empty,
            },
            completed ? "✓" : "○",
            completed,
            overdue);
    }

    private static string FormatDeadline(DateTimeOffset? deadlineUtc)
    {
        if (deadlineUtc is null)
        {
            return "Без дедлайна";
        }

        DateTimeOffset local = deadlineUtc.Value.ToLocalTime();
        return $"До {local:dd.MM, HH:mm}";
    }

    private void NotifyFilterState()
    {
        OnPropertyChanged(nameof(IsAllFilter));
        OnPropertyChanged(nameof(IsTodayFilter));
        OnPropertyChanged(nameof(IsWeekFilter));
        OnPropertyChanged(nameof(IsCompletedFilter));
    }

    private void NotifyProgress()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(ProgressText));
    }
}
