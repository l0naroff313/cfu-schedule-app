using System.Globalization;
using UniversitySchedule.Contracts.Schedule;
using UniversitySchedule.Mobile.Core.Scheduling;

namespace UniversitySchedule.Mobile.Pages;

public sealed record LessonPickerItem(
    Guid? LessonId,
    string DisplayName,
    string Subject)
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

    public override string ToString() => DisplayName;

    public static IReadOnlyList<LessonPickerItem> FromSession(ScheduleSession session)
    {
        var result = new List<LessonPickerItem>
        {
            new(null, "Без привязки к паре", string.Empty),
        };

        if (session.Snapshot is null)
        {
            return result;
        }

        result.AddRange(session.Snapshot.Lessons
            .GroupBy(lesson => lesson.Id)
            .Select(group => group.First())
            .OrderBy(lesson => lesson.Date)
            .ThenBy(lesson => lesson.PairNumber)
            .Select(ToPickerItem));
        return result;
    }

    private static LessonPickerItem ToPickerItem(ScheduleLesson lesson) => new(
        lesson.Id,
        $"{lesson.Date.ToString("ddd, d MMM", RussianCulture)} • {lesson.PairNumber} пара • {lesson.Subject}",
        lesson.Subject);
}
