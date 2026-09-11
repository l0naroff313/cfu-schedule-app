using UniversitySchedule.Contracts.Catalog;
using UniversitySchedule.Contracts.Schedule;
using UniversitySchedule.Mobile.Core.Notes;
using UniversitySchedule.Mobile.Core.Profiles;
using UniversitySchedule.Mobile.Core.Widgets;

namespace UniversitySchedule.Mobile.Core.Tests.Widgets;

public sealed class ScheduleWidgetStateBuilderTests
{
    private static readonly AcademicProfile Profile = new(
        Guid.NewGuid(), "ФТИ", Guid.NewGuid(), "Программная инженерия",
        Guid.NewGuid(), "ПИ-б-о-252", 2, Guid.NewGuid(), "1 подгруппа");

    [Fact]
    public void CurrentLesson_ShowsProgressAndNoteIndicator()
    {
        DateOnly day = new(2026, 9, 11);
        ScheduleLesson lesson = Lesson(day, 2, 10, 11, "Компьютерные сети", "302А");
        DateTimeOffset now = new(2026, 9, 11, 7, 30, 0, TimeSpan.Zero);
        PersonalNote note = new(Guid.NewGuid(), lesson.Id, "Взять конспект", now, now);

        ScheduleWidgetState state = BuildWithNotes(now, [lesson], [note]);

        Assert.Equal("Сейчас", state.HeaderTitle);
        Assert.Equal("Компьютерные сети", state.HeaderSubtitle);
        Assert.InRange(state.Progress, 0.49, 0.51);
        Assert.True(Assert.Single(state.Lessons).HasNote);
        Assert.Equal("302А", state.HeaderClassroom);
    }

    [Fact]
    public void Break_ShowsNextLessonAndInterval()
    {
        DateOnly day = new(2026, 9, 11);
        ScheduleLesson previous = Lesson(day, 1, 7, 8, "Алгоритмы", "201");
        ScheduleLesson next = Lesson(day, 2, 10, 11, "Компьютерные сети", "302А");

        ScheduleWidgetState state = Build(new DateTimeOffset(2026, 9, 11, 6, 30, 0, TimeSpan.Zero), previous, next);

        Assert.Equal("Перемена", state.HeaderTitle);
        Assert.Contains("Компьютерные сети", state.HeaderSubtitle);
        Assert.Equal("08:00–10:00", state.HeaderTime);
        Assert.Equal("302А", state.HeaderClassroom);
    }

    [Fact]
    public void EndOfDay_ShowsTomorrowList()
    {
        ScheduleLesson tomorrow = Lesson(new DateOnly(2026, 9, 12), 1, 5, 6, "Философия", "209А");
        ScheduleWidgetState state = Build(new DateTimeOffset(2026, 9, 11, 15, 0, 0, TimeSpan.Zero), tomorrow);

        Assert.Equal("Учебный день окончен", state.HeaderTitle);
        Assert.Equal("завтра", state.DayLabel);
        Assert.Equal("Философия", Assert.Single(state.Lessons).Subject);
    }

    private static ScheduleWidgetState Build(DateTimeOffset now, params ScheduleLesson[] lessons)
    {
        var snapshot = new ScheduleSnapshot(
            new(ScheduleScopeKind.Group, Guid.NewGuid(), "ПИ-б-о-252"),
            "test", now, new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 20), lessons);
        return ScheduleWidgetStateBuilder.Build(snapshot, Profile, [], now);
    }

    private static ScheduleWidgetState Build(DateTimeOffset now, ScheduleLesson first, ScheduleLesson second, PersonalNote note)
    {
        var snapshot = new ScheduleSnapshot(
            new(ScheduleScopeKind.Group, Guid.NewGuid(), "ПИ-б-о-252"),
            "test", now, first.Date, second.Date, [first, second]);
        return ScheduleWidgetStateBuilder.Build(snapshot, Profile, [note], now);
    }

    private static ScheduleWidgetState BuildWithNotes(
        DateTimeOffset now,
        IReadOnlyList<ScheduleLesson> lessons,
        IReadOnlyList<PersonalNote> notes)
    {
        var snapshot = new ScheduleSnapshot(
            new(ScheduleScopeKind.Group, Guid.NewGuid(), "ПИ-б-о-252"),
            "test", now, lessons.Min(lesson => lesson.Date), lessons.Max(lesson => lesson.Date), lessons);
        return ScheduleWidgetStateBuilder.Build(snapshot, Profile, notes, now);
    }

    private static ScheduleLesson Lesson(DateOnly date, int pair, int start, int end, string subject, string classroom) =>
        new(Guid.NewGuid(), date, pair,
            new DateTimeOffset(date.ToDateTime(new TimeOnly(start, 0)), TimeSpan.FromHours(3)).ToUniversalTime(),
            new DateTimeOffset(date.ToDateTime(new TimeOnly(end, 0)), TimeSpan.FromHours(3)).ToUniversalTime(),
            subject, "ЛК", [new TeacherSummary(Guid.NewGuid(), "Иванов И.И.", "Иванов")],
            [], classroom, "пр. Вернадского, 4", classroom, "обычное", null);
}
