using UniversitySchedule.Contracts.Catalog;
using UniversitySchedule.Domain.Scheduling;
using UniversitySchedule.Mobile.Core.Scheduling;

namespace UniversitySchedule.Mobile.Core.Tests.Scheduling;

public sealed class SchedulePageViewModelTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 1, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SelectorCommands_ChangeAudienceAndRange()
    {
        SchedulePageViewModel viewModel = CreateViewModel();

        viewModel.SelectTeacherCommand.Execute(null);
        viewModel.SelectWeekCommand.Execute(null);

        Assert.True(viewModel.IsTeacherMode);
        Assert.False(viewModel.IsGroupMode);
        Assert.True(viewModel.IsWeekMode);
        Assert.False(viewModel.IsDayMode);

        viewModel.SelectGroupCommand.Execute(null);
        viewModel.SelectDayCommand.Execute(null);

        Assert.True(viewModel.IsGroupMode);
        Assert.True(viewModel.IsDayMode);
    }

    [Fact]
    public void DateCommands_NavigateAndReturnToUniversityToday()
    {
        SchedulePageViewModel viewModel = CreateViewModel();
        DateOnly today = viewModel.SelectedDate;

        viewModel.NextPeriodCommand.Execute(null);
        Assert.Equal(today.AddDays(1), viewModel.SelectedDate);

        viewModel.SelectWeekCommand.Execute(null);
        viewModel.NextPeriodCommand.Execute(null);
        Assert.Equal(today.AddDays(8), viewModel.SelectedDate);

        viewModel.GoTodayCommand.Execute(null);
        Assert.Equal(today, viewModel.SelectedDate);
        Assert.Single(viewModel.WeekDates, item => item.IsSelected);
    }

    [Fact]
    public void SelectedDateValue_AllowsPickingAnArbitraryCalendarDate()
    {
        SchedulePageViewModel viewModel = CreateViewModel();

        viewModel.SelectedDateValue = new DateTime(2026, 10, 14);

        Assert.Equal(new DateOnly(2026, 10, 14), viewModel.SelectedDate);
        Assert.Contains("14 октября", viewModel.PeriodText);
        Assert.Single(viewModel.WeekDates, item => item.IsSelected && item.Date == viewModel.SelectedDate);
    }

    [Fact]
    public void TeacherQuery_FiltersNamesIgnoringCaseWhitespaceAndYo()
    {
        SchedulePageViewModel viewModel = CreateViewModel();
        var matching = new TeacherSummary(Guid.NewGuid(), "Алёна Сергеевна Иванова");
        var other = new TeacherSummary(Guid.NewGuid(), "Пётр Петрович Сидоров");
        viewModel.SetTeachers([matching, other]);

        viewModel.TeacherQuery = "  АЛЕНА   СЕРГЕЕВНА ";

        TeacherSummary result = Assert.Single(viewModel.TeacherOptions);
        Assert.Equal(matching.Id, result.Id);
    }

    [Fact]
    public void TeacherSearch_SelectsResultWithoutASeparatePicker()
    {
        SchedulePageViewModel viewModel = CreateViewModel();
        var teacher = new TeacherSummary(Guid.NewGuid(), "Иванова Елена Сергеевна", "доцент");
        viewModel.SetTeachers(
        [
            teacher,
            new TeacherSummary(Guid.NewGuid(), "Петров Пётр Петрович"),
        ]);

        Assert.Empty(viewModel.TeacherOptions);
        Assert.False(viewModel.HasTeacherOptions);
        Assert.False(viewModel.HasSelectedTeacher);
        Assert.Empty(viewModel.SelectedTeacherProfile.FullName);

        viewModel.TeacherQuery = "Иванова";
        Assert.True(viewModel.HasTeacherOptions);

        viewModel.ChooseTeacher(Assert.Single(viewModel.TeacherOptions));

        Assert.Equal(teacher.Id, viewModel.SelectedTeacher?.Id);
        Assert.Equal(teacher.DisplayName, viewModel.TeacherQuery);
        Assert.False(viewModel.HasTeacherOptions);
        Assert.True(viewModel.HasSelectedTeacher);
        Assert.Equal(teacher.DisplayName, viewModel.SelectedTeacherProfile.FullName);
    }

    [Fact]
    public void CurrentTeacherLesson_ShowsPublishedBuildingAndClassroom()
    {
        SchedulePageViewModel viewModel = CreateViewModel();
        TeacherSummary teacher = SelectTeacher(viewModel);
        LessonOccurrence lesson = CreateLesson(
            teacher.Id,
            classroom: "ауд. 305",
            building: "корпус А");

        viewModel.SetTeacherSchedule(CreateCoverage(teacher.Id, [lesson]));

        Assert.Equal("Сейчас преподаёт", viewModel.TeacherStatusTitle);
        Assert.Equal("Математика", viewModel.TeacherSubjectText);
        Assert.Equal("корпус А • ауд. 305", viewModel.TeacherLocationText);
    }

    [Fact]
    public void CurrentTeacherLesson_DoesNotInventMissingClassroom()
    {
        SchedulePageViewModel viewModel = CreateViewModel();
        TeacherSummary teacher = SelectTeacher(viewModel);

        viewModel.SetTeacherSchedule(CreateCoverage(
            teacher.Id,
            [CreateLesson(teacher.Id)]));

        Assert.Equal("Аудитория не указана", viewModel.TeacherLocationText);
    }

    [Fact]
    public void MissingCurrentCoverage_IsNotReportedAsFreePeriod()
    {
        SchedulePageViewModel viewModel = CreateViewModel();
        SelectTeacher(viewModel);

        Assert.Equal("Расписание ещё не загружено", viewModel.TeacherStatusTitle);
        Assert.Equal("Аудитория появится после синхронизации.", viewModel.TeacherLocationText);
    }

    [Fact]
    public void ConflictingLocations_AreReportedWithoutChoosingOne()
    {
        SchedulePageViewModel viewModel = CreateViewModel();
        TeacherSummary teacher = SelectTeacher(viewModel);

        viewModel.SetTeacherSchedule(CreateCoverage(
            teacher.Id,
            [
                CreateLesson(teacher.Id, classroom: "305"),
                CreateLesson(teacher.Id, classroom: "410"),
            ]));

        Assert.Equal("Данные расписания расходятся", viewModel.TeacherStatusTitle);
        Assert.Equal("Указано несколько аудиторий.", viewModel.TeacherLocationText);
    }

    [Fact]
    public void EmptyTeacherCatalog_ExplainsThatSynchronizationIsRequired()
    {
        SchedulePageViewModel viewModel = CreateViewModel();

        viewModel.SetTeachers([]);

        Assert.True(viewModel.HasNoTeacherOptions);
        Assert.Equal(
            "Список преподавателей появится после синхронизации расписания.",
            viewModel.TeacherEmptyText);
    }

    [Fact]
    public void TeacherCatalogSummary_ShowsCatalogAndFilteredResultCounts()
    {
        SchedulePageViewModel viewModel = CreateViewModel();
        viewModel.SetTeachers(
        [
            new TeacherSummary(Guid.NewGuid(), "Иванов И.И."),
            new TeacherSummary(Guid.NewGuid(), "Петров П.П."),
        ]);

        Assert.Equal("2 преподавателей • введите фамилию для поиска", viewModel.TeacherCatalogSummaryText);

        viewModel.TeacherQuery = "Иванов";

        Assert.Equal("Найдено: 1", viewModel.TeacherCatalogSummaryText);
    }

    [Fact]
    public void SelectingAnotherTeacher_DoesNotReusePreviousCoverage()
    {
        SchedulePageViewModel viewModel = CreateViewModel();
        var first = new TeacherSummary(Guid.NewGuid(), "Иванова Елена Сергеевна");
        var second = new TeacherSummary(Guid.NewGuid(), "Петров Пётр Петрович");
        viewModel.SetTeachers([first, second]);
        viewModel.SelectedTeacher = first;
        viewModel.SetTeacherSchedule(CreateCoverage(
            first.Id,
            [CreateLesson(first.Id, classroom: "305")]));

        viewModel.SelectedTeacher = second;

        Assert.Equal("Расписание ещё не загружено", viewModel.TeacherStatusTitle);
        Assert.Equal("Аудитория появится после синхронизации.", viewModel.TeacherLocationText);
    }

    [Fact]
    public void ExpiredCoverage_IsNotReportedAsFreePeriod()
    {
        SchedulePageViewModel viewModel = CreateViewModel();
        TeacherSummary teacher = SelectTeacher(viewModel);
        viewModel.SetTeacherSchedule(new TeacherScheduleCoverage(
            teacher.Id,
            Now.AddDays(-2),
            Now.AddDays(-1),
            []));

        Assert.Equal("Расписание ещё не загружено", viewModel.TeacherStatusTitle);
    }

    [Fact]
    public void RefreshTeacherLocation_UpdatesAfterLessonEnds()
    {
        var timeProvider = new MutableTimeProvider(Now);
        var viewModel = new SchedulePageViewModel(timeProvider);
        TeacherSummary teacher = SelectTeacher(viewModel);
        viewModel.SetTeacherSchedule(CreateCoverage(
            teacher.Id,
            [CreateLesson(teacher.Id, classroom: "305")]));
        Assert.Equal("Сейчас преподаёт", viewModel.TeacherStatusTitle);

        timeProvider.Current = Now.AddMinutes(61);
        viewModel.RefreshTeacherLocation();

        Assert.Equal("Сейчас нет пары", viewModel.TeacherStatusTitle);
    }

    private static SchedulePageViewModel CreateViewModel()
    {
        return new SchedulePageViewModel(new FixedTimeProvider(Now));
    }

    private static TeacherSummary SelectTeacher(SchedulePageViewModel viewModel)
    {
        var teacher = new TeacherSummary(
            Guid.Parse("447cfa82-b48a-4dfd-8da8-ec524036e5ae"),
            "Иванова Елена Сергеевна");
        viewModel.SetTeachers([teacher]);
        viewModel.SelectedTeacher = teacher;
        return teacher;
    }

    private static LessonOccurrence CreateLesson(
        Guid teacherId,
        string? classroom = null,
        string? building = null)
    {
        return new LessonOccurrence(
            Guid.NewGuid(),
            "Математика",
            1,
            Now.AddMinutes(-30),
            Now.AddMinutes(60),
            teacherIds: [teacherId],
            classroom: classroom,
            building: building);
    }

    private static TeacherScheduleCoverage CreateCoverage(
        Guid teacherId,
        IEnumerable<LessonOccurrence> lessons)
    {
        return new TeacherScheduleCoverage(
            teacherId,
            Now.AddHours(-2),
            Now.AddHours(3),
            lessons);
    }

    private sealed class FixedTimeProvider(DateTimeOffset current) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => current;
    }

    private sealed class MutableTimeProvider(DateTimeOffset current) : TimeProvider
    {
        public DateTimeOffset Current { get; set; } = current;

        public override DateTimeOffset GetUtcNow() => Current;
    }
}
