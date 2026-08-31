using System.Globalization;
using UniversitySchedule.Contracts.Catalog;

namespace UniversitySchedule.Mobile.Core.Scheduling;

public enum AcademicWeekParity
{
    Unknown = 0,
    Odd = 1,
    Even = 2,
}

public static class AcademicWeekParityResolver
{
    public static AcademicWeekParity Resolve(
        DateOnly date,
        ReferenceScheduleCalendar? calendar)
    {
        if (calendar is null)
        {
            return AcademicWeekParity.Unknown;
        }

        DateOnly monday = StartOfWeek(date);
        WeekAnchor[] anchors = ParseAnchors(calendar).ToArray();
        WeekAnchor[] exactAnchors = anchors
            .Where(anchor => anchor.Monday == monday)
            .ToArray();

        if (exactAnchors.Length > 0)
        {
            AcademicWeekParity exactParity = exactAnchors[0].Parity;
            return exactAnchors.All(anchor => anchor.Parity == exactParity)
                ? exactParity
                : AcademicWeekParity.Unknown;
        }

        WeekAnchor? nearestAnchor = anchors
            .OrderBy(anchor => Math.Abs(anchor.Monday.DayNumber - monday.DayNumber))
            .FirstOrDefault();
        if (nearestAnchor is null)
        {
            return AcademicWeekParity.Unknown;
        }

        int weekDistance = Math.Abs(monday.DayNumber - nearestAnchor.Monday.DayNumber) / 7;
        return weekDistance % 2 == 0
            ? nearestAnchor.Parity
            : Opposite(nearestAnchor.Parity);
    }

    public static string Format(AcademicWeekParity parity) => parity switch
    {
        AcademicWeekParity.Even => "Чётная неделя",
        AcademicWeekParity.Odd => "Нечётная неделя",
        _ => string.Empty,
    };

    private static IEnumerable<WeekAnchor> ParseAnchors(ReferenceScheduleCalendar calendar)
    {
        foreach (string value in calendar.EvenWeekMondays)
        {
            if (TryParseDate(value, out DateOnly monday))
            {
                yield return new WeekAnchor(monday, AcademicWeekParity.Even);
            }
        }

        foreach (string value in calendar.OddWeekMondays)
        {
            if (TryParseDate(value, out DateOnly monday))
            {
                yield return new WeekAnchor(monday, AcademicWeekParity.Odd);
            }
        }
    }

    private static bool TryParseDate(string value, out DateOnly date) =>
        DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);

    private static DateOnly StartOfWeek(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static AcademicWeekParity Opposite(AcademicWeekParity parity) => parity switch
    {
        AcademicWeekParity.Even => AcademicWeekParity.Odd,
        AcademicWeekParity.Odd => AcademicWeekParity.Even,
        _ => AcademicWeekParity.Unknown,
    };

    private sealed record WeekAnchor(DateOnly Monday, AcademicWeekParity Parity);
}
