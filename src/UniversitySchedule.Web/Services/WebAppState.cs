using System.Globalization;
using System.Text.Json;
using Microsoft.JSInterop;
using UniversitySchedule.Contracts.Catalog;
using UniversitySchedule.Contracts.Schedule;
using UniversitySchedule.Mobile.Core.Assignments;
using UniversitySchedule.Mobile.Core.Catalog;
using UniversitySchedule.Mobile.Core.Cfu;
using UniversitySchedule.Mobile.Core.Identity;
using UniversitySchedule.Mobile.Core.Notes;
using UniversitySchedule.Mobile.Core.Profiles;
using UniversitySchedule.Mobile.Core.Scheduling;
using UniversitySchedule.Mobile.Core.Sync;

namespace UniversitySchedule.Web.Services;

public enum WebTab
{
    Today,
    Schedule,
    Assignments,
    Notes,
    Profile,
}

public sealed class WebAppState(
    ScheduleSession scheduleSession,
    CfuScheduleRepository scheduleRepository,
    PersonalNoteStore noteStore,
    PersonalAssignmentStore assignmentStore,
    InstallationIdentityService installationIdentity,
    IReferenceCatalogProvider referenceCatalogProvider,
    BrowserThemeService themeService,
    WebOfflineShellService offlineShellService,
    PersonalDataSyncQueue syncQueue,
    PersonalDataSynchronizer synchronizer,
    PersonalDataSnapshotRestorer snapshotRestorer,
    PersonalDataSyncCoordinator syncCoordinator,
    UniversityScheduleApiOptions apiOptions,
    TimeProvider timeProvider,
    DailyScheduleRefreshService dailyScheduleRefresh)
{
    private static readonly TimeSpan UniversityUtcOffset = TimeSpan.FromHours(3);
    private const string DefaultGroupName = "ПИ-б-о-252";
    private bool _initialized;

    public event EventHandler? Changed;

    public WebTab ActiveTab { get; private set; } = WebTab.Today;

    public bool IsBusy { get; private set; }

    public bool IsInitialized { get; private set; }

    public string? ErrorText { get; private set; }

    public string Theme { get; private set; } = "light";

    public bool IsStandalone { get; private set; }

    public string InstallationId { get; private set; } = string.Empty;

    public bool IsProfileEditorOpen { get; private set; }

    public CfuScheduleCatalog? Catalog { get; private set; }

    public ReferenceCatalogSnapshot? ReferenceCatalog { get; private set; }

    public Guid SelectedInstituteId { get; private set; }

    public Guid SelectedDirectionId { get; private set; }

    public int SelectedCourseNumber { get; private set; }

    public Guid SelectedGroupId { get; private set; }

    public int SelectedSubgroupNumber { get; private set; } = 1;

    public DateOnly SelectedDate { get; private set; } = TodayAtUniversity(TimeProvider.System);

    public bool IsWeekMode { get; private set; }

    public bool IsTeacherMode { get; private set; }

    public string TeacherQuery { get; set; } = string.Empty;

    public IReadOnlyList<TeacherReference> TeacherResults { get; private set; } = [];

    public TeacherReference? SelectedTeacher { get; private set; }

    public IReadOnlyList<ScheduleLesson> TeacherLessons { get; private set; } = [];

    public IReadOnlyList<PersonalNote> Notes { get; private set; } = [];

    public IReadOnlyList<PersonalAssignment> Assignments { get; private set; } = [];

    public string SyncStatusText { get; private set; } = "Локальные данные";

    public bool IsPreparingOffline { get; private set; }

    public bool IsOfflineReady { get; private set; }

    public string OfflineStatusText { get; private set; } = "Офлайн-версия ещё не проверена";

    public int OfflineLessonCount { get; private set; }

    public AcademicProfile? Profile => scheduleSession.Profile;

    public ScheduleSnapshot? Schedule => scheduleSession.Snapshot;

    public bool IsUsingCachedSchedule => scheduleSession.IsFromCache;

    public bool NeedsProfile => Profile is null || IsProfileEditorOpen;

    public IReadOnlyList<InstituteSummary> Institutes => Catalog?.Institutes ?? [];

    public IReadOnlyList<DirectionSummary> Directions => Catalog?.Directions
        .Where(direction => direction.InstituteId == SelectedInstituteId)
        .OrderBy(direction => direction.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToArray() ?? [];

    public IReadOnlyList<int> Courses => Catalog?.GetCourses(SelectedDirectionId) ?? [];

    public IReadOnlyList<StudyGroupSummary> Groups => Catalog?.Groups
        .Where(group => group.DirectionId == SelectedDirectionId &&
                        group.CourseNumber == SelectedCourseNumber)
        .OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToArray() ?? [];

    public IReadOnlyList<DateOnly> WeekDates
    {
        get
        {
            DateOnly monday = StartOfWeek(SelectedDate);
            return Enumerable.Range(0, 7).Select(monday.AddDays).ToArray();
        }
    }

    public IReadOnlyList<ScheduleLesson> VisibleLessons
    {
        get
        {
            IEnumerable<ScheduleLesson> lessons = IsTeacherMode ? TeacherLessons : Schedule?.Lessons ?? [];
            if (IsWeekMode)
            {
                DateOnly monday = StartOfWeek(SelectedDate);
                DateOnly sunday = monday.AddDays(6);
                return lessons.Where(lesson => lesson.Date >= monday && lesson.Date <= sunday)
                    .OrderBy(lesson => lesson.Date)
                    .ThenBy(lesson => lesson.PairNumber)
                    .ToArray();
            }

            return lessons.Where(lesson => lesson.Date == SelectedDate)
                .OrderBy(lesson => lesson.PairNumber)
                .ToArray();
        }
    }

    public IReadOnlyList<ScheduleLesson> TodayLessons
    {
        get
        {
            DateOnly today = TodayAtUniversity(timeProvider);
            return (Schedule?.Lessons ?? [])
                .Where(lesson => lesson.Date == today)
                .OrderBy(lesson => lesson.PairNumber)
                .ToArray();
        }
    }

    public ScheduleLesson? CurrentLesson
    {
        get
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            return (Schedule?.Lessons ?? []).FirstOrDefault(lesson =>
                lesson.StartsAtUtc <= now && lesson.EndsAtUtc > now);
        }
    }

    public ScheduleLesson? NextLesson
    {
        get
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            return (Schedule?.Lessons ?? [])
                .Where(lesson => lesson.StartsAtUtc > now)
                .OrderBy(lesson => lesson.StartsAtUtc)
                .FirstOrDefault();
        }
    }

    public int CompletedAssignmentCount => Assignments.Count(assignment =>
        assignment.Status == PersonalAssignmentStatus.Completed);

    public int AssignmentCompletionPercent => Assignments.Count == 0
        ? 0
        : (int)Math.Round(CompletedAssignmentCount * 100d / Assignments.Count);

    public string WeekParityText => AcademicWeekParityResolver.Format(
        AcademicWeekParityResolver.Resolve(SelectedDate, ReferenceCatalog?.Calendar));

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        IsBusy = true;
        NotifyChanged();
        try
        {
            Theme = await themeService.GetAsync(cancellationToken);
            await themeService.ApplyAsync(Theme, cancellationToken);
            IsStandalone = await themeService.IsStandaloneAsync(cancellationToken);
            InstallationIdentity identity = await installationIdentity.GetOrCreateAsync(cancellationToken);
            InstallationId = identity.DisplayId;

            try
            {
                await scheduleSession.InitializeAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
            {
                ErrorText = "Расписание КФУ пока недоступно. Повторите обновление при подключении к сети.";
            }

            await ReloadPersonalDataAsync(cancellationToken);
            if (Profile is null)
            {
                IsProfileEditorOpen = true;
                await LoadCatalogAsync(cancellationToken);
            }

            await RefreshSyncStatusAsync(cancellationToken);
            await RefreshOfflineReadinessAsync(cancellationToken);
            syncCoordinator.StartBackgroundSynchronization();
            dailyScheduleRefresh.RefreshAttempted += OnDailyScheduleRefreshAttempted;
            dailyScheduleRefresh.Start();
            _initialized = true;
        }
        finally
        {
            IsInitialized = true;
            IsBusy = false;
            NotifyChanged();
        }
    }

    public async Task SetTabAsync(WebTab tab, CancellationToken cancellationToken = default)
    {
        ActiveTab = tab;
        NotifyChanged();
        if (tab == WebTab.Profile)
        {
            await RefreshOfflineReadinessAsync(cancellationToken);
            NotifyChanged();
        }
        if (tab == WebTab.Schedule && ReferenceCatalog is null)
        {
            await EnsureReferenceCatalogAsync(cancellationToken);
            NotifyChanged();
        }
    }

    public async Task RefreshScheduleAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        ErrorText = null;
        NotifyChanged();
        try
        {
            await scheduleSession.RefreshAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            ErrorText = Schedule is null
                ? "Не удалось загрузить расписание КФУ."
                : "Показана сохранённая копия расписания.";
        }
        finally
        {
            IsBusy = false;
            NotifyChanged();
        }
    }

    public async Task OpenProfileEditorAsync(CancellationToken cancellationToken = default)
    {
        IsProfileEditorOpen = true;
        if (Catalog is null)
        {
            await LoadCatalogAsync(cancellationToken);
        }

        ApplyProfileSelection();
        NotifyChanged();
    }

    public async Task RetryCatalogAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        ErrorText = null;
        NotifyChanged();
        try
        {
            await LoadCatalogAsync(cancellationToken);
        }
        finally
        {
            IsBusy = false;
            NotifyChanged();
        }
    }

    public void CloseProfileEditor()
    {
        if (Profile is null)
        {
            return;
        }

        IsProfileEditorOpen = false;
        NotifyChanged();
    }

    public void SelectInstitute(Guid instituteId)
    {
        SelectedInstituteId = instituteId;
        SelectedDirectionId = Directions.FirstOrDefault()?.Id ?? Guid.Empty;
        SelectedCourseNumber = Courses.FirstOrDefault();
        SelectedGroupId = Groups.FirstOrDefault()?.Id ?? Guid.Empty;
        NotifyChanged();
    }

    public void SelectDirection(Guid directionId)
    {
        SelectedDirectionId = directionId;
        SelectedCourseNumber = Courses.FirstOrDefault();
        SelectedGroupId = Groups.FirstOrDefault()?.Id ?? Guid.Empty;
        NotifyChanged();
    }

    public void SelectCourse(int courseNumber)
    {
        SelectedCourseNumber = courseNumber;
        SelectedGroupId = Groups.FirstOrDefault()?.Id ?? Guid.Empty;
        NotifyChanged();
    }

    public void SelectGroup(Guid groupId)
    {
        SelectedGroupId = groupId;
        NotifyChanged();
    }

    public void SelectSubgroup(int subgroupNumber)
    {
        SelectedSubgroupNumber = subgroupNumber is 1 or 2 ? subgroupNumber : 1;
        NotifyChanged();
    }

    public async Task SaveProfileAsync(CancellationToken cancellationToken = default)
    {
        StudyGroupSummary group = Groups.FirstOrDefault(item => item.Id == SelectedGroupId)
            ?? throw new InvalidOperationException("Выберите учебную группу.");
        DirectionSummary direction = Directions.First(item => item.Id == group.DirectionId);
        InstituteSummary institute = Institutes.First(item => item.Id == direction.InstituteId);
        int? subgroupNumber = group.SubgroupPolicy == SubgroupSelectionPolicy.NotAvailable
            ? null
            : SelectedSubgroupNumber;
        Guid? subgroupId = subgroupNumber is null
            ? null
            : CfuStableId.Create("subgroup", group.Name, subgroupNumber.Value.ToString(CultureInfo.InvariantCulture));
        var profile = new AcademicProfile(
            institute.Id,
            institute.Name,
            direction.Id,
            direction.Name,
            group.Id,
            group.Name,
            group.CourseNumber,
            subgroupId,
            subgroupNumber is null ? null : $"Подгруппа {subgroupNumber}");

        IsBusy = true;
        ErrorText = null;
        NotifyChanged();
        try
        {
            await scheduleSession.SetProfileAsync(profile, cancellationToken);
            IsProfileEditorOpen = false;
            ActiveTab = WebTab.Today;
            SelectedDate = TodayAtUniversity(timeProvider);
            await RefreshOfflineReadinessAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            ErrorText = "Не удалось получить расписание выбранной группы. Проверьте подключение к сети.";
        }
        finally
        {
            IsBusy = false;
            NotifyChanged();
        }
    }

    public void SetScheduleAudience(bool teacherMode)
    {
        IsTeacherMode = teacherMode;
        NotifyChanged();
    }

    public void SetScheduleRange(bool weekMode)
    {
        IsWeekMode = weekMode;
        NotifyChanged();
    }

    public void SelectDate(DateOnly date)
    {
        SelectedDate = date;
        IsWeekMode = false;
        NotifyChanged();
    }

    public void MoveDate(int dayCount)
    {
        SelectedDate = SelectedDate.AddDays(dayCount);
        NotifyChanged();
    }

    public void GoToday()
    {
        SelectedDate = TodayAtUniversity(timeProvider);
        NotifyChanged();
    }

    public async Task SearchTeachersAsync(CancellationToken cancellationToken = default)
    {
        string query = NormalizeSearch(TeacherQuery);
        if (query.Length < 2)
        {
            TeacherResults = [];
            NotifyChanged();
            return;
        }

        await EnsureReferenceCatalogAsync(cancellationToken);
        TeacherResults = (ReferenceCatalog?.Teachers ?? [])
            .Where(teacher => SearchableTeacherText(teacher).Contains(query, StringComparison.Ordinal))
            .OrderByDescending(teacher => NormalizeSearch(teacher.Surname).StartsWith(query, StringComparison.Ordinal))
            .ThenBy(teacher => teacher.FullName, StringComparer.CurrentCultureIgnoreCase)
            .Take(30)
            .ToArray();
        NotifyChanged();
    }

    public void SelectTeacher(TeacherReference teacher)
    {
        ArgumentNullException.ThrowIfNull(teacher);
        SelectedTeacher = teacher;
        TeacherLessons = ReferenceCatalog is null
            ? []
            : ReferenceTeacherScheduleMapper.Map(ReferenceCatalog, teacher);
        TeacherResults = [];
        TeacherQuery = teacher.FullName;
        IsTeacherMode = true;
        NotifyChanged();
    }

    public ScheduleLesson? CurrentTeacherLesson()
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        return TeacherLessons.FirstOrDefault(lesson =>
            lesson.StartsAtUtc <= now && lesson.EndsAtUtc > now);
    }

    public async Task SaveNoteAsync(
        Guid? id,
        string text,
        Guid? lessonId,
        string? title,
        string? subject,
        bool isPinned,
        CancellationToken cancellationToken = default)
    {
        if (id is Guid noteId)
        {
            await noteStore.UpdateAsync(noteId, text, lessonId, title, subject, isPinned, cancellationToken);
        }
        else
        {
            await noteStore.AddAsync(text, lessonId, title, subject, isPinned, cancellationToken);
        }

        await ReloadPersonalDataAsync(cancellationToken);
        await RefreshSyncStatusAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task DeleteNoteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await noteStore.DeleteAsync(id, cancellationToken);
        await ReloadPersonalDataAsync(cancellationToken);
        await RefreshSyncStatusAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task SaveAssignmentAsync(
        Guid? id,
        string subject,
        string text,
        Guid? lessonId,
        DateTimeOffset? deadlineUtc,
        PersonalAssignmentStatus status,
        CancellationToken cancellationToken = default)
    {
        if (id is Guid assignmentId)
        {
            await assignmentStore.UpdateAsync(
                assignmentId,
                subject,
                text,
                lessonId,
                deadlineUtc,
                status,
                cancellationToken);
        }
        else
        {
            await assignmentStore.AddAsync(subject, text, lessonId, deadlineUtc, status, cancellationToken);
        }

        await ReloadPersonalDataAsync(cancellationToken);
        await RefreshSyncStatusAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task SetAssignmentStatusAsync(
        Guid id,
        PersonalAssignmentStatus status,
        CancellationToken cancellationToken = default)
    {
        await assignmentStore.SetStatusAsync(id, status, cancellationToken);
        await ReloadPersonalDataAsync(cancellationToken);
        await RefreshSyncStatusAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task DeleteAssignmentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await assignmentStore.DeleteAsync(id, cancellationToken);
        await ReloadPersonalDataAsync(cancellationToken);
        await RefreshSyncStatusAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ToggleThemeAsync(CancellationToken cancellationToken = default)
    {
        Theme = Theme == "dark" ? "light" : "dark";
        await themeService.ApplyAsync(Theme, cancellationToken);
        NotifyChanged();
    }

    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        NotifyChanged();
        try
        {
            PersonalDataSyncRunResult result = await synchronizer.SynchronizeAsync(cancellationToken);
            if (result.CanDownloadSnapshot)
            {
                await snapshotRestorer.RestoreAsync(cancellationToken);
                await ReloadPersonalDataAsync(cancellationToken);
            }

            await RefreshSyncStatusAsync(cancellationToken);
        }
        finally
        {
            IsBusy = false;
            NotifyChanged();
        }
    }

    public async Task PrepareOfflineAsync(CancellationToken cancellationToken = default)
    {
        if (Profile is null || IsPreparingOffline)
        {
            return;
        }

        IsPreparingOffline = true;
        IsOfflineReady = false;
        IsBusy = true;
        ErrorText = null;
        OfflineStatusText = "Сохраняем расписание и файлы приложения…";
        NotifyChanged();
        try
        {
            OfflineSchedulePreparationResult preparation =
                await scheduleSession.PrepareOfflineAsync(cancellationToken);
            WebOfflineShellStatus shell = await offlineShellService.PrepareAsync(cancellationToken);
            ApplyOfflineReadiness(preparation.Readiness, shell);
            if (!IsOfflineReady)
            {
                ErrorText = shell.Error ?? "Не удалось полностью подготовить автономную версию.";
            }
            else if (!preparation.DownloadedFromNetwork)
            {
                OfflineStatusText += " • используется последняя сохранённая копия";
            }
        }
        catch (Exception exception) when (
            exception is HttpRequestException or InvalidOperationException or JSException)
        {
            await RefreshOfflineReadinessAsync(cancellationToken);
            ErrorText = IsOfflineReady
                ? "Свежие данные недоступны, но сохранённая офлайн-версия готова."
                : "Не удалось скачать данные для офлайн-режима. Проверьте интернет и повторите.";
        }
        finally
        {
            IsPreparingOffline = false;
            IsBusy = false;
            NotifyChanged();
        }
    }

    public ScheduleLesson? FindLesson(Guid? lessonId) => lessonId is null
        ? null
        : (Schedule?.Lessons ?? []).FirstOrDefault(lesson => lesson.Id == lessonId);

    public bool IsCurrent(ScheduleLesson lesson)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        return lesson.StartsAtUtc <= now && lesson.EndsAtUtc > now;
    }

    private async Task LoadCatalogAsync(CancellationToken cancellationToken)
    {
        try
        {
            Catalog = (await scheduleRepository.LoadCatalogAsync(cancellationToken)).Catalog;
            ErrorText = null;
            ApplyProfileSelection();
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            ErrorText = "Не удалось загрузить список институтов и групп.";
        }
    }

    private void ApplyProfileSelection()
    {
        if (Catalog is null)
        {
            return;
        }

        StudyGroupSummary? targetGroup = Profile is null
            ? Catalog.Groups.FirstOrDefault(group => string.Equals(
                group.Name,
                DefaultGroupName,
                StringComparison.OrdinalIgnoreCase))
            : Catalog.Groups.FirstOrDefault(group => group.Id == Profile.GroupId);
        targetGroup ??= Catalog.Groups.FirstOrDefault();
        if (targetGroup is null)
        {
            return;
        }

        DirectionSummary direction = Catalog.Directions.First(item => item.Id == targetGroup.DirectionId);
        SelectedInstituteId = direction.InstituteId;
        SelectedDirectionId = direction.Id;
        SelectedCourseNumber = targetGroup.CourseNumber;
        SelectedGroupId = targetGroup.Id;
        SelectedSubgroupNumber = Profile?.SubgroupName is { } subgroupName &&
                                 subgroupName.Contains("2", StringComparison.Ordinal)
            ? 2
            : 1;
    }

    private async Task EnsureReferenceCatalogAsync(CancellationToken cancellationToken)
    {
        if (ReferenceCatalog is not null)
        {
            return;
        }

        try
        {
            ReferenceCatalog = await referenceCatalogProvider.LoadAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidDataException)
        {
            ErrorText = "Справочник преподавателей пока недоступен.";
        }
    }

    private async Task ReloadPersonalDataAsync(CancellationToken cancellationToken)
    {
        Notes = (await noteStore.GetAllAsync(cancellationToken))
            .OrderByDescending(note => note.IsPinned)
            .ThenByDescending(note => note.UpdatedAtUtc)
            .ToArray();
        Assignments = (await assignmentStore.GetAllAsync(cancellationToken))
            .OrderBy(assignment => assignment.Status == PersonalAssignmentStatus.Completed)
            .ThenBy(assignment => assignment.DeadlineUtc ?? DateTimeOffset.MaxValue)
            .ThenByDescending(assignment => assignment.UpdatedAtUtc)
            .ToArray();
    }

    private async Task RefreshSyncStatusAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<PersonalDataSyncOperation> operations = await syncQueue.GetPendingAsync(cancellationToken);
        int conflicts = operations.Count(operation => operation.State == PersonalDataSyncOperationState.Conflict);
        int pending = operations.Count(operation => operation.State == PersonalDataSyncOperationState.Pending);
        SyncStatusText = !apiOptions.IsEnabled
            ? pending == 0
                ? "Данные хранятся на этом устройстве"
                : $"Локальная очередь: {pending}"
            : conflicts > 0
                ? $"Конфликты синхронизации: {conflicts}"
                : pending > 0
                    ? $"Ожидают синхронизации: {pending}"
                    : "Синхронизировано";
    }

    private async Task RefreshOfflineReadinessAsync(CancellationToken cancellationToken)
    {
        OfflineScheduleReadiness schedule;
        try
        {
            schedule = await scheduleSession.CheckOfflineReadinessAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            schedule = new OfflineScheduleReadiness(false, Profile?.GroupName, 0, null);
        }

        WebOfflineShellStatus shell;
        try
        {
            shell = await offlineShellService.GetStatusAsync(cancellationToken);
        }
        catch (JSException exception)
        {
            shell = new WebOfflineShellStatus(false, false, 0, 0, exception.Message);
        }

        ApplyOfflineReadiness(schedule, shell);
    }

    private void ApplyOfflineReadiness(
        OfflineScheduleReadiness schedule,
        WebOfflineShellStatus shell)
    {
        OfflineLessonCount = schedule.LessonCount;
        IsOfflineReady = schedule.IsReady && shell.IsReady;
        OfflineStatusText = IsOfflineReady
            ? $"Готово • {schedule.LessonCount} занятий • данные от {schedule.UpdatedAtUtc?.ToLocalTime():dd.MM.yyyy HH:mm}"
            : !schedule.IsReady
                ? "Расписание выбранной группы ещё не сохранено"
                : !shell.IsSupported
                    ? "Браузер не поддерживает автономную установку"
                    : shell.Error ?? "Расписание сохранено, файлы приложения ещё не готовы";
    }

    private static string SearchableTeacherText(TeacherReference teacher) => NormalizeSearch(
        string.Join(' ', teacher.FullName, teacher.ScheduleDisplayName, teacher.Surname));

    private static string NormalizeSearch(string value) => string.Join(
            ' ',
            value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .ToLowerInvariant()
        .Replace('ё', 'е');

    private static DateOnly TodayAtUniversity(TimeProvider provider) =>
        DateOnly.FromDateTime(provider.GetUtcNow().ToOffset(UniversityUtcOffset).DateTime);

    private static DateOnly StartOfWeek(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private void OnDailyScheduleRefreshAttempted(object? sender, EventArgs eventArgs) => NotifyChanged();
}
