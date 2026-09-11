using System.Globalization;
using UniversitySchedule.Contracts.Schedule;
using UniversitySchedule.Mobile.Core.Notes;
using UniversitySchedule.Mobile.Core.Profiles;

namespace UniversitySchedule.Mobile.Core.Widgets;

public sealed record ScheduleWidgetLesson(
    int PairNumber,
    string Subject,
    string TimeText,
    string Classroom,
    bool HasNote);

public sealed record ScheduleWidgetState(
    string GroupText,
    string HeaderTitle,
    string HeaderSubtitle,
    string HeaderTime,
    string HeaderClassroom,
    double Progress,
    string DayLabel,
    IReadOnlyList<ScheduleWidgetLesson> Lessons,
    DateTimeOffset GeneratedAtUtc)
{
    public static ScheduleWidgetState Empty(DateTimeOffset nowUtc) => new(
        "Учебная группа не выбрана",
        "Откройте приложение",
        "Выберите группу для расписания",
        string.Empty,
        string.Empty,
        0,
        "сегодня",
        [],
        nowUtc);
}

public static class ScheduleWidgetStateBuilder
{
    private static readonly TimeSpan MoscowOffset = TimeSpan.FromHours(3);
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    public static ScheduleWidgetState Build(
        ScheduleSnapshot? snapshot,
        AcademicProfile? profile,
        IReadOnlyCollection<PersonalNote> notes,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(notes);
        DateTimeOffset now = nowUtc.ToOffset(MoscowOffset);
        if (snapshot is null || profile is null)
        {
            return ScheduleWidgetState.Empty(nowUtc);
        }

        string group = profile.SubgroupName is null
            ? profile.GroupName
            : $"{profile.GroupName} • {profile.SubgroupName}";
        DateOnly today = DateOnly.FromDateTime(now.DateTime);
        ScheduleLesson[] todayLessons = snapshot.Lessons
            .Where(lesson => lesson.Date == today && !IsCancelled(lesson))
            .OrderBy(lesson => lesson.StartsAtUtc)
            .ThenBy(lesson => lesson.PairNumber)
            .ToArray();
        ScheduleLesson[] tomorrowLessons = snapshot.Lessons
            .Where(lesson => lesson.Date > today && !IsCancelled(lesson))
            .OrderBy(lesson => lesson.Date)
            .ThenBy(lesson => lesson.StartsAtUtc)
            .ThenBy(lesson => lesson.PairNumber)
            .ToArray();
        ScheduleLesson? current = todayLessons.FirstOrDefault(lesson =>
            lesson.StartsAtUtc <= nowUtc && nowUtc < lesson.EndsAtUtc);
        ScheduleLesson? next = todayLessons.FirstOrDefault(lesson => lesson.StartsAtUtc > nowUtc);
        ScheduleLesson? previous = todayLessons.LastOrDefault(lesson => lesson.EndsAtUtc <= nowUtc);

        if (current is not null)
        {
            return CreateLessonState(group, "Сейчас", current, todayLessons, notes, nowUtc, today);
        }

        if (next is not null && previous is not null && previous.EndsAtUtc < nowUtc && nowUtc < next.StartsAtUtc)
        {
            double progress = (nowUtc - previous.EndsAtUtc).TotalSeconds /
                Math.Max(1, (next.StartsAtUtc - previous.EndsAtUtc).TotalSeconds);
            return new ScheduleWidgetState(
                group,
                "Перемена",
                $"следующая: {next.Subject}",
                $"{FormatTime(previous.EndsAtUtc)}–{FormatTime(next.StartsAtUtc)}",
                FormatLocation(next),
                Math.Clamp(progress, 0, 1),
                "сегодня",
                MapLessons(todayLessons, notes),
                nowUtc);
        }

        if (next is not null)
        {
            return CreateLessonState(group, "Следующая пара", next, todayLessons, notes, nowUtc, today);
        }

        DateOnly? tomorrow = tomorrowLessons.FirstOrDefault()?.Date;
        ScheduleLesson[] following = tomorrow is null
            ? []
            : tomorrowLessons.Where(lesson => lesson.Date == tomorrow.Value).ToArray();
        ScheduleLesson? tomorrowFirst = following.FirstOrDefault();
        return tomorrowFirst is null
            ? new ScheduleWidgetState(group, "Учебный день окончен", "Занятий больше нет", string.Empty,
                string.Empty, 1, "завтра", [], nowUtc)
            : new ScheduleWidgetState(
                group,
                "Учебный день окончен",
                $"завтра: {tomorrowFirst.Subject}",
                FormatTimeRange(tomorrowFirst),
                FormatLocation(tomorrowFirst),
                1,
                "завтра",
                MapLessons(following, notes),
                nowUtc);
    }

    private static ScheduleWidgetState CreateLessonState(
        string group,
        string title,
        ScheduleLesson selected,
        IReadOnlyList<ScheduleLesson> list,
        IReadOnlyCollection<PersonalNote> notes,
        DateTimeOffset nowUtc,
        DateOnly day)
    {
        double progress = nowUtc <= selected.StartsAtUtc
            ? 0
            : Math.Clamp((nowUtc - selected.StartsAtUtc).TotalSeconds /
                Math.Max(1, (selected.EndsAtUtc - selected.StartsAtUtc).TotalSeconds), 0, 1);
        return new ScheduleWidgetState(
            group,
            title,
            selected.Subject,
            FormatTimeRange(selected),
            FormatLocation(selected),
            progress,
            day == DateOnly.FromDateTime(nowUtc.ToOffset(MoscowOffset).DateTime) ? "сегодня" : "завтра",
            MapLessons(list, notes),
            nowUtc);
    }

    private static IReadOnlyList<ScheduleWidgetLesson> MapLessons(
        IEnumerable<ScheduleLesson> lessons,
        IReadOnlyCollection<PersonalNote> notes) => lessons
        .Take(12)
        .Select(lesson => new ScheduleWidgetLesson(
            lesson.PairNumber,
            lesson.Subject,
            FormatTimeRange(lesson),
            FormatLocation(lesson),
            notes.Any(note => note.LessonId == lesson.Id)))
        .ToArray();

    private static string FormatTimeRange(ScheduleLesson lesson) =>
        $"{FormatTime(lesson.StartsAtUtc)}–{FormatTime(lesson.EndsAtUtc)}";

    private static string FormatTime(DateTimeOffset time) => time.ToOffset(MoscowOffset).ToString("HH:mm", Russian);

    private static string FormatLocation(ScheduleLesson lesson) =>
        string.IsNullOrWhiteSpace(lesson.Classroom) ? "Кабинет не указан" : lesson.Classroom.Trim();

    private static bool IsCancelled(ScheduleLesson lesson) =>
        lesson.Status.Contains("отмен", StringComparison.CurrentCultureIgnoreCase);
}
