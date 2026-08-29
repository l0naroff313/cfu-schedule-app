using System.Globalization;
using UniversitySchedule.Contracts.Catalog;
using UniversitySchedule.Contracts.Schedule;

namespace UniversitySchedule.Mobile.Core.Cfu;

public sealed record CfuTeacherScheduleSearch(
    IReadOnlyList<TeacherSummary> Teachers,
    IReadOnlyList<ScheduleLesson> Lessons,
    DateOnly From,
    DateOnly To);

public static class CfuScheduleMapper
{
    private static readonly TimeSpan UniversityUtcOffset = TimeSpan.FromHours(3);
    private static readonly string[] SupportedDateFormats = ["yyyy-MM-dd", "dd.MM.yyyy"];

    public static ScheduleSnapshot MapGroup(
        CfuScheduleIndexDocument index,
        CfuGroupScheduleDocument schedule,
        int? subgroup = null)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentException.ThrowIfNullOrWhiteSpace(schedule.Code);

        IReadOnlyList<ScheduleLesson> lessons = ExpandLessons(
            index,
            schedule.Lessons.Where(lesson => IncludesSubgroup(lesson.Subgroup, subgroup)),
            schedule.FacultyLessons,
            schedule.Code);
        (DateOnly from, DateOnly to) = ResolveCoverage(index, lessons);

        return new ScheduleSnapshot(
            new ScheduleScope(
                ScheduleScopeKind.Group,
                CfuStableId.Create("group", schedule.Code),
                schedule.Code.Trim()),
            CreateVersion(lessons),
            DateTimeOffset.UtcNow,
            from,
            to,
            lessons);
    }

    public static CfuTeacherScheduleSearch MapTeacherSearch(
        CfuScheduleIndexDocument index,
        IReadOnlyList<CfuLessonDocument> sourceLessons)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(sourceLessons);

        IReadOnlyList<ScheduleLesson> lessons = ExpandLessons(
            index,
            sourceLessons,
            [],
            fallbackGroupCode: null);
        TeacherSummary[] teachers = sourceLessons
            .SelectMany(lesson => lesson.Teachers)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .Select(name => new TeacherSummary(CfuStableId.Create("teacher", name), name))
            .ToArray();
        (DateOnly from, DateOnly to) = ResolveCoverage(index, lessons);

        return new CfuTeacherScheduleSearch(teachers, lessons, from, to);
    }

    private static IReadOnlyList<ScheduleLesson> ExpandLessons(
        CfuScheduleIndexDocument index,
        IEnumerable<CfuLessonDocument> sourceLessons,
        IEnumerable<CfuFacultyLessonDocument> facultyLessons,
        string? fallbackGroupCode)
    {
        IReadOnlyDictionary<int, (TimeOnly Start, TimeOnly End)> bells = index.Bells
            .Where(bell => bell.PairNumber > 0 &&
                           TryParseTime(bell.StartsAt, out _) &&
                           TryParseTime(bell.EndsAt, out _))
            .GroupBy(bell => bell.PairNumber)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    CfuBellDocument bell = group.First();
                    _ = TryParseTime(bell.StartsAt, out TimeOnly start);
                    _ = TryParseTime(bell.EndsAt, out TimeOnly end);
                    return (start, end);
                });
        DateOnly[] evenMondays = ParseDates(index.Weeks.EvenWeekMondays);
        DateOnly[] oddMondays = ParseDates(index.Weeks.OddWeekMondays);
        var result = new Dictionary<Guid, ScheduleLesson>();

        foreach (CfuLessonDocument lesson in sourceLessons)
        {
            if (!IsValidLesson(lesson, bells))
            {
                continue;
            }

            IEnumerable<DateOnly> dates = ResolveLessonDates(lesson, evenMondays, oddMondays);
            foreach (DateOnly date in dates)
            {
                string groupCode = string.IsNullOrWhiteSpace(lesson.GroupCode)
                    ? fallbackGroupCode ?? string.Empty
                    : lesson.GroupCode;
                ScheduleLesson occurrence = CreateLesson(
                    date,
                    lesson.PairNumber,
                    lesson.Subject,
                    lesson.LessonType,
                    lesson.Teachers,
                    groupCode,
                    lesson.Subgroup,
                    lesson.Classroom,
                    lesson.Building,
                    lesson.Note,
                    lesson.Online,
                    lesson.Parity,
                    bells[lesson.PairNumber]);
                result.TryAdd(occurrence.Id, occurrence);
            }
        }

        foreach (CfuFacultyLessonDocument lesson in facultyLessons)
        {
            if (lesson.Day is < 1 or > 7 ||
                string.IsNullOrWhiteSpace(lesson.Subject) ||
                !bells.TryGetValue(lesson.PairNumber, out (TimeOnly Start, TimeOnly End) bell) ||
                !TryParsePeriodStart(lesson.Period, out DateOnly monday))
            {
                continue;
            }

            DateOnly date = monday.AddDays(lesson.Day - 1);
            ScheduleLesson occurrence = CreateLesson(
                date,
                lesson.PairNumber,
                lesson.Subject,
                lesson.LessonType,
                lesson.Teachers,
                lesson.GroupCode ?? fallbackGroupCode ?? string.Empty,
                subgroup: 0,
                lesson.Classroom,
                lesson.Building,
                sourceNote: null,
                online: null,
                parity: lesson.Period,
                bell);
            result.TryAdd(occurrence.Id, occurrence);
        }

        return result.Values
            .OrderBy(lesson => lesson.Date)
            .ThenBy(lesson => lesson.PairNumber)
            .ThenBy(lesson => lesson.Subject, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static ScheduleLesson CreateLesson(
        DateOnly date,
        int pairNumber,
        string subject,
        string? lessonType,
        IEnumerable<string> teacherNames,
        string groupCode,
        int subgroup,
        string? classroom,
        string? building,
        string? sourceNote,
        string? online,
        string parity,
        (TimeOnly Start, TimeOnly End) bell)
    {
        string normalizedSubject = subject.Trim();
        TeacherSummary[] teachers = teacherNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Select(name => new TeacherSummary(CfuStableId.Create("teacher", name), name))
            .ToArray();
        string normalizedGroup = groupCode.Trim();
        ScheduleGroupReference[] groups = string.IsNullOrWhiteSpace(normalizedGroup)
            ? []
            : [new ScheduleGroupReference(
                CfuStableId.Create("group", normalizedGroup),
                subgroup > 0 ? $"{normalizedGroup}, подгруппа {subgroup}" : normalizedGroup,
                subgroup > 0 ? CfuStableId.Create("subgroup", normalizedGroup, subgroup.ToString(CultureInfo.InvariantCulture)) : null)];
        DateTimeOffset startsAt = new(date, bell.Start, UniversityUtcOffset);
        DateTimeOffset endsAt = new(date, bell.End, UniversityUtcOffset);
        string? note = JoinOptional(sourceNote, online);
        Guid id = CfuStableId.Create(
            "lesson",
            normalizedGroup,
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            pairNumber.ToString(CultureInfo.InvariantCulture),
            subgroup.ToString(CultureInfo.InvariantCulture),
            normalizedSubject,
            lessonType,
            string.Join('|', teachers.Select(teacher => teacher.DisplayName)),
            classroom,
            building,
            parity,
            note);

        return new ScheduleLesson(
            id,
            date,
            pairNumber,
            startsAt.ToUniversalTime(),
            endsAt.ToUniversalTime(),
            normalizedSubject,
            NormalizeOptional(lessonType),
            teachers,
            groups,
            NormalizeOptional(classroom),
            NormalizeOptional(building),
            JoinOptional(building, classroom),
            "обычное",
            note);
    }

    private static IEnumerable<DateOnly> ResolveLessonDates(
        CfuLessonDocument lesson,
        IReadOnlyList<DateOnly> evenMondays,
        IReadOnlyList<DateOnly> oddMondays)
    {
        if (TryParseDate(lesson.Date, out DateOnly explicitDate))
        {
            return [explicitDate];
        }

        string parity = lesson.Parity.Trim().ToLowerInvariant().Replace('ё', 'е');
        IEnumerable<DateOnly> mondays = parity switch
        {
            "чет" or "четная" or "четная неделя" => evenMondays,
            "нечет" or "нечетная" or "нечетная неделя" => oddMondays,
            "обе" or "все" or "еженедельно" => evenMondays.Concat(oddMondays),
            _ => [],
        };

        return mondays
            .Select(monday => monday.AddDays(lesson.Day - 1))
            .Distinct();
    }

    private static bool IncludesSubgroup(int sourceSubgroup, int? selectedSubgroup)
    {
        return selectedSubgroup is null || sourceSubgroup == 0 || sourceSubgroup == selectedSubgroup;
    }

    private static bool IsValidLesson(
        CfuLessonDocument lesson,
        IReadOnlyDictionary<int, (TimeOnly Start, TimeOnly End)> bells)
    {
        return lesson.Day is >= 1 and <= 7 &&
               lesson.PairNumber > 0 &&
               bells.ContainsKey(lesson.PairNumber) &&
               !string.IsNullOrWhiteSpace(lesson.Subject);
    }

    private static (DateOnly From, DateOnly To) ResolveCoverage(
        CfuScheduleIndexDocument index,
        IReadOnlyList<ScheduleLesson> lessons)
    {
        DateOnly[] semesterDates = ParseDates(index.Weeks.EvenWeekMondays)
            .Concat(ParseDates(index.Weeks.OddWeekMondays))
            .OrderBy(date => date)
            .ToArray();
        if (semesterDates.Length > 0)
        {
            return (semesterDates[0], semesterDates[^1].AddDays(6));
        }

        if (lessons.Count > 0)
        {
            return (lessons.Min(lesson => lesson.Date), lessons.Max(lesson => lesson.Date));
        }

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow.Add(UniversityUtcOffset));
        return (today, today);
    }

    private static string CreateVersion(IEnumerable<ScheduleLesson> lessons)
    {
        return CfuStableId.Create(
                "snapshot",
                string.Join('|', lessons.Select(lesson => lesson.Id.ToString("N"))))
            .ToString("N");
    }

    private static DateOnly[] ParseDates(IEnumerable<string> values)
    {
        return values
            .Select(value => TryParseDate(value, out DateOnly date) ? date : (DateOnly?)null)
            .Where(date => date.HasValue)
            .Select(date => date!.Value)
            .Distinct()
            .OrderBy(date => date)
            .ToArray();
    }

    private static bool TryParsePeriodStart(string value, out DateOnly result)
    {
        string start = value.Split(['–', '—'], 2, StringSplitOptions.TrimEntries)[0];
        return TryParseDate(start, out result);
    }

    private static bool TryParseDate(string? value, out DateOnly result)
    {
        return DateOnly.TryParseExact(
            value,
            SupportedDateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out result);
    }

    private static bool TryParseTime(string? value, out TimeOnly result)
    {
        return TimeOnly.TryParseExact(
            value,
            "HH:mm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out result);
    }

    private static string? JoinOptional(params string?[] values)
    {
        string[] normalized = values
            .Select(NormalizeOptional)
            .Where(value => value is not null)
            .Cast<string>()
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        return normalized.Length == 0 ? null : string.Join(" • ", normalized);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
