using System.Globalization;
using UniversitySchedule.Contracts.Catalog;
using UniversitySchedule.Contracts.Schedule;
using UniversitySchedule.Mobile.Core.Cfu;

namespace UniversitySchedule.Mobile.Core.Catalog;

public static class ReferenceTeacherScheduleMapper
{
    private static readonly TimeSpan UniversityUtcOffset = TimeSpan.FromHours(3);
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd.MM.yyyy"];

    public static IReadOnlyList<ScheduleLesson> Map(
        ReferenceCatalogSnapshot catalog,
        TeacherReference teacher)
    {
        IReadOnlyDictionary<int, ReferenceBell> bells = catalog.Calendar.Bells
            .GroupBy(bell => bell.PairNumber)
            .ToDictionary(group => group.Key, group => group.First());
        DateOnly[] evenMondays = ParseDates(catalog.Calendar.EvenWeekMondays);
        DateOnly[] oddMondays = ParseDates(catalog.Calendar.OddWeekMondays);
        var result = new Dictionary<Guid, ScheduleLesson>();
        var teacherSummary = new TeacherSummary(teacher.Id, teacher.FullName, teacher.Position);

        foreach (TeacherScheduleEntry entry in teacher.Schedule)
        {
            if (!bells.TryGetValue(entry.PairNumber, out ReferenceBell? bell) ||
                !TimeOnly.TryParseExact(bell.StartsAt, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly start) ||
                !TimeOnly.TryParseExact(bell.EndsAt, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly end))
            {
                continue;
            }

            foreach (DateOnly date in ResolveDates(entry, evenMondays, oddMondays))
            {
                DateTimeOffset startsAt = new(date, start, UniversityUtcOffset);
                DateTimeOffset endsAt = new(date, end, UniversityUtcOffset);
                Guid id = CfuStableId.Create(
                    "reference-teacher-lesson",
                    teacher.IdentityKey,
                    entry.GroupCode,
                    date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    entry.PairNumber.ToString(CultureInfo.InvariantCulture),
                    entry.Subject,
                    entry.Classroom,
                    entry.Building);
                ScheduleGroupReference[] groups = string.IsNullOrWhiteSpace(entry.GroupCode)
                    ? []
                    : [new ScheduleGroupReference(
                        CfuStableId.Create("group", entry.GroupCode),
                        entry.Subgroup > 0 ? $"{entry.GroupCode}, подгруппа {entry.Subgroup}" : entry.GroupCode,
                        entry.Subgroup > 0
                            ? CfuStableId.Create("subgroup", entry.GroupCode, entry.Subgroup.ToString(CultureInfo.InvariantCulture))
                            : null)];
                result.TryAdd(id, new ScheduleLesson(
                    id,
                    date,
                    entry.PairNumber,
                    startsAt.ToUniversalTime(),
                    endsAt.ToUniversalTime(),
                    entry.Subject,
                    entry.LessonType,
                    [teacherSummary],
                    groups,
                    entry.Classroom,
                    entry.Building,
                    JoinOptional(entry.Building, entry.Classroom),
                    "обычное",
                    JoinOptional(entry.Note, entry.Online)));
            }
        }

        return result.Values
            .OrderBy(lesson => lesson.Date)
            .ThenBy(lesson => lesson.PairNumber)
            .ToArray();
    }

    private static IEnumerable<DateOnly> ResolveDates(
        TeacherScheduleEntry entry,
        IReadOnlyList<DateOnly> evenMondays,
        IReadOnlyList<DateOnly> oddMondays)
    {
        if (TryParseDate(entry.Date, out DateOnly explicitDate))
        {
            return [explicitDate];
        }

        if (entry.Day is < 1 or > 7)
        {
            return [];
        }

        string parity = entry.Parity.ToLowerInvariant().Replace('ё', 'е');
        IEnumerable<DateOnly> mondays = parity switch
        {
            string value when value.Contains("нечет", StringComparison.Ordinal) => oddMondays,
            string value when value.Contains("чет", StringComparison.Ordinal) => evenMondays,
            string value when value.Contains("обе", StringComparison.Ordinal) ||
                              value.Contains("все", StringComparison.Ordinal) ||
                              value.Contains("еженед", StringComparison.Ordinal) => evenMondays.Concat(oddMondays),
            _ => TryParsePeriod(entry.Parity, out DateOnly periodMonday) ? [periodMonday] : [],
        };
        return mondays.Select(monday => monday.AddDays(entry.Day - 1)).Distinct();
    }

    private static DateOnly[] ParseDates(IEnumerable<string> values) => values
        .Select(value => TryParseDate(value, out DateOnly date) ? date : (DateOnly?)null)
        .Where(date => date.HasValue)
        .Select(date => date!.Value)
        .Distinct()
        .OrderBy(date => date)
        .ToArray();

    private static bool TryParsePeriod(string value, out DateOnly date)
    {
        string first = value.Split(['–', '—'], 2, StringSplitOptions.TrimEntries)[0];
        return TryParseDate(first, out date);
    }

    private static bool TryParseDate(string? value, out DateOnly date) => DateOnly.TryParseExact(
        value,
        DateFormats,
        CultureInfo.InvariantCulture,
        DateTimeStyles.None,
        out date);

    private static string? JoinOptional(params string?[] values)
    {
        string[] parts = values.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        return parts.Length == 0 ? null : string.Join(" • ", parts);
    }
}
