using System.Collections.ObjectModel;
using UniversitySchedule.Contracts.Catalog;
using UniversitySchedule.Mobile.Core.Cfu;
using UniversitySchedule.Mobile.Core.Presentation;
using UniversitySchedule.Mobile.Core.Scheduling;

namespace UniversitySchedule.Mobile.Core.Profiles;

public sealed record SubgroupChoice(int? Number, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed class ProfileSetupViewModel(
    CfuScheduleRepository scheduleRepository,
    ScheduleSession scheduleSession) : ObservableObject
{
    private const string DefaultGroupCode = "ПИ-б-о-252";

    private readonly CfuScheduleRepository _scheduleRepository = scheduleRepository
        ?? throw new ArgumentNullException(nameof(scheduleRepository));
    private readonly ScheduleSession _scheduleSession = scheduleSession
        ?? throw new ArgumentNullException(nameof(scheduleSession));
    private CfuScheduleCatalog? _catalog;
    private InstituteSummary? _selectedInstitute;
    private DirectionSummary? _selectedDirection;
    private int? _selectedCourse;
    private StudyGroupSummary? _selectedGroup;
    private SubgroupChoice? _selectedSubgroup;
    private bool _isBusy;
    private string _statusText = "Загружаем каталог КФУ…";
    private string? _errorText;

    public ObservableCollection<InstituteSummary> Institutes { get; } = [];

    public ObservableCollection<DirectionSummary> Directions { get; } = [];

    public ObservableCollection<int> Courses { get; } = [];

    public ObservableCollection<StudyGroupSummary> Groups { get; } = [];

    public ObservableCollection<SubgroupChoice> Subgroups { get; } =
    [
        new(null, "Вся группа"),
        new(1, "1 подгруппа"),
        new(2, "2 подгруппа"),
    ];

    public InstituteSummary? SelectedInstitute
    {
        get => _selectedInstitute;
        set
        {
            if (!SetProperty(ref _selectedInstitute, value))
            {
                return;
            }

            PopulateDirections();
            OnSelectionChanged();
        }
    }

    public DirectionSummary? SelectedDirection
    {
        get => _selectedDirection;
        set
        {
            if (!SetProperty(ref _selectedDirection, value))
            {
                return;
            }

            PopulateCourses();
            OnSelectionChanged();
        }
    }

    public int? SelectedCourse
    {
        get => _selectedCourse;
        set
        {
            if (!SetProperty(ref _selectedCourse, value))
            {
                return;
            }

            PopulateGroups();
            OnSelectionChanged();
        }
    }

    public StudyGroupSummary? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value))
            {
                OnSelectionChanged();
            }
        }
    }

    public SubgroupChoice? SelectedSubgroup
    {
        get => _selectedSubgroup;
        set
        {
            if (SetProperty(ref _selectedSubgroup, value))
            {
                OnSelectionChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool CanSave => !IsBusy &&
                           SelectedInstitute is not null &&
                           SelectedDirection is not null &&
                           SelectedCourse is not null &&
                           SelectedGroup is not null &&
                           SelectedSubgroup is not null;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string? ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetProperty(ref _errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        ErrorText = null;

        try
        {
            await _scheduleSession.InitializeAsync(cancellationToken);
            CfuCatalogLoadResult result = await _scheduleRepository.LoadCatalogAsync(cancellationToken);
            _catalog = result.Catalog;
            StatusText = result.IsFromCache
                ? $"Офлайн-каталог • обновлён {result.UpdatedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm}"
                : $"Каталог КФУ обновлён {result.UpdatedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm}";

            Replace(Institutes, _catalog.Institutes);
            RestoreSelection(_scheduleSession.Profile);
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            ErrorText = "Каталог КФУ пока недоступен. Проверьте интернет и повторите.";
            StatusText = "Не удалось загрузить каталог.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> SaveAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSave)
        {
            return false;
        }

        IsBusy = true;
        ErrorText = null;
        StatusText = "Загружаем расписание выбранной группы…";

        try
        {
            Guid? subgroupId = SelectedSubgroup!.Number is int number
                ? CfuStableId.Create("subgroup", SelectedGroup!.Name, number.ToString())
                : null;
            var profile = new AcademicProfile(
                SelectedInstitute!.Id,
                SelectedInstitute.Name,
                SelectedDirection!.Id,
                SelectedDirection.Name,
                SelectedGroup!.Id,
                SelectedGroup.Name,
                SelectedCourse!.Value,
                subgroupId,
                SelectedSubgroup.Number is int selectedNumber
                    ? $"{selectedNumber} подгруппа"
                    : null);
            await _scheduleSession.SetProfileAsync(profile, cancellationToken);
            StatusText = "Расписание сохранено на устройстве.";
            return true;
        }
        catch (InvalidOperationException)
        {
            ErrorText = "Не удалось загрузить расписание этой группы. Проверьте интернет и повторите.";
            StatusText = "Профиль не изменён.";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RestoreSelection(AcademicProfile? profile)
    {
        if (profile is null)
        {
            SelectDefaultGroup();
            return;
        }

        SelectedInstitute = Institutes.FirstOrDefault(item => item.Id == profile.InstituteId);
        SelectedDirection = Directions.FirstOrDefault(item => item.Id == profile.DirectionId);
        SelectedCourse = profile.CourseNumber;
        SelectedGroup = Groups.FirstOrDefault(item => item.Id == profile.GroupId);
        SelectedSubgroup = profile.SubgroupName is null
            ? Subgroups[0]
            : Subgroups.FirstOrDefault(item =>
                item.Number is not null && profile.SubgroupName.StartsWith(item.Number.Value.ToString()))
              ?? Subgroups[0];
    }

    private void SelectDefaultGroup()
    {
        StudyGroupSummary? group = _catalog?.Groups.FirstOrDefault(item =>
            string.Equals(item.Name, DefaultGroupCode, StringComparison.OrdinalIgnoreCase));
        DirectionSummary? direction = group is null
            ? null
            : _catalog!.Directions.FirstOrDefault(item => item.Id == group.DirectionId);
        InstituteSummary? institute = direction is null
            ? null
            : _catalog!.Institutes.FirstOrDefault(item => item.Id == direction.InstituteId);

        if (group is null || direction is null || institute is null)
        {
            SelectedSubgroup = Subgroups[0];
            return;
        }

        SelectedInstitute = institute;
        SelectedDirection = Directions.FirstOrDefault(item => item.Id == direction.Id);
        SelectedCourse = group.CourseNumber;
        SelectedGroup = Groups.FirstOrDefault(item => item.Id == group.Id);
        SelectedSubgroup = Subgroups[0];
        StatusText = $"Группа {DefaultGroupCode} выбрана по умолчанию.";
    }

    private void PopulateDirections()
    {
        IEnumerable<DirectionSummary> items = SelectedInstitute is null || _catalog is null
            ? []
            : _catalog.Directions.Where(direction => direction.InstituteId == SelectedInstitute.Id);
        Replace(Directions, items);
        SelectedDirection = null;
    }

    private void PopulateCourses()
    {
        IEnumerable<int> items = SelectedDirection is null || _catalog is null
            ? []
            : _catalog.GetCourses(SelectedDirection.Id);
        Replace(Courses, items);
        SelectedCourse = null;
    }

    private void PopulateGroups()
    {
        IEnumerable<StudyGroupSummary> items =
            SelectedDirection is null || SelectedCourse is null || _catalog is null
                ? []
                : _catalog.Groups
                    .Where(group => group.DirectionId == SelectedDirection.Id &&
                                    group.CourseNumber == SelectedCourse)
                    .OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase);
        Replace(Groups, items);
        SelectedGroup = null;
        SelectedSubgroup = Subgroups[0];
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(CanSave));
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (T item in source)
        {
            target.Add(item);
        }
    }
}
