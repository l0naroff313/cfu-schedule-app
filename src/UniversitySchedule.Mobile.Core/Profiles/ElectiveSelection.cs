using System.Collections.ObjectModel;
using System.Text.Json;
using UniversitySchedule.Mobile.Core.Cfu;
using UniversitySchedule.Mobile.Core.Presentation;

namespace UniversitySchedule.Mobile.Core.Profiles;

public sealed class ElectiveSelection(CfuScheduleRepository repository) : ObservableObject
{
    private CfuElectiveDocument? _document;
    private int _course;
    private string? _discipline;
    private CfuElectiveGroup? _group;
    private int? _module;
    private bool _busy;
    private bool _unresolvedSelection;
    private string _status = "Выберите свою дисциплину и группу ЦК/ДРПК. Можно настроить позже.";
    public ObservableCollection<string> Disciplines { get; } = [];
    public ObservableCollection<CfuElectiveGroup> Groups { get; } = [];
    public ObservableCollection<int> Modules { get; } = [];
    public bool IsEligible => CfuElectiveDocument.IsEligible(_course);
    public bool IsBusy { get => _busy; private set => SetProperty(ref _busy, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string? Discipline { get => _discipline; set {
        if (!SetProperty(ref _discipline, value)) return;
        Groups.Clear();
        foreach (var g in (_document?.Groups ?? []).Where(g => g.Course == _course && g.Discipline == value).OrderBy(g => g.Code)) Groups.Add(g);
        Group = null;
    } }
    public CfuElectiveGroup? Group { get => _group; set {
        if (!SetProperty(ref _group, value)) return;
        if (value is not null) _unresolvedSelection = false;
        Modules.Clear();
        foreach (int m in (_document?.Lessons ?? []).Where(l => l.GroupCode == value?.Code && l.Module.HasValue)
                     .Select(l => l.Module!.Value).Distinct().Order()) Modules.Add(m);
        Module = null;
        OnPropertyChanged(nameof(HasModules));
        OnPropertyChanged(nameof(CanSave));
    } }
    public bool HasModules => Modules.Count > 0;
    public int? Module { get => _module; set { SetProperty(ref _module, value); OnPropertyChanged(nameof(CanSave)); } }
    public bool CanSave => !IsBusy && !_unresolvedSelection && (Discipline is null || Group is not null && (!HasModules || Modules.Contains(Module ?? -1)));

    public void SetCourse(int course)
    {
        if (_course == course) return;
        _course = course;
        Clear();
        Populate();
        OnPropertyChanged(nameof(IsEligible));
    }
    public void Clear() { _unresolvedSelection = false; Discipline = null; Group = null; Module = null; OnPropertyChanged(nameof(CanSave)); }
    public void Restore(AcademicProfile? profile)
    {
        if (profile is null) return;
        SetCourse(profile.CourseNumber);
        var g = _document?.Groups.FirstOrDefault(g => g.Code == profile.ElectiveGroupCode && g.Course == _course);
        if (g is null) {
            _unresolvedSelection = profile.ElectiveGroupCode is not null;
            OnPropertyChanged(nameof(CanSave));
            return;
        }
        _unresolvedSelection = false;
        Discipline = g.Discipline; Group = g; Module = profile.ElectiveModule;
    }
    public async Task LoadAsync(AcademicProfile? profile = null, CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true; OnPropertyChanged(nameof(CanSave));
        var selected = Group; int? module = Module;
        try {
            _document = await repository.LoadElectivesAsync(cancellationToken);
            Clear();
            Populate();
            if (selected is not null && selected.Course == _course) {
                var current = _document.Groups.FirstOrDefault(g => g.Code == selected.Code && g.Course == _course);
                if (current is not null) { Discipline = current.Discipline; Group = Groups.First(g => g.Code == current.Code); Module = module; }
                else _unresolvedSelection = true;
            }
            else if (profile?.CourseNumber == _course) Restore(profile);
            Status = _unresolvedSelection ? "Сохранённая группа электива не найдена. Повторите загрузку или явно отмените выбор электива."
                : "Занятия добавятся к основной группе по дням и времени КФУ. Если расписание не опубликовано, сообщим об этом.";
        }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException or InvalidDataException or JsonException) {
            if (profile?.ElectiveGroupCode is not null && Group is null && profile.CourseNumber == _course) _unresolvedSelection = true;
            Status = "Не удалось загрузить элективы. Повторите загрузку с интернетом. Основную группу можно сохранить без электива.";
        }
        finally { IsBusy = false; OnPropertyChanged(nameof(CanSave)); }
    }
    private void Populate()
    {
        Disciplines.Clear();
        foreach (string d in (_document?.Groups ?? []).Where(g => g.Course == _course).Select(g => g.Discipline).Distinct().Order()) Disciplines.Add(d);
    }
}
