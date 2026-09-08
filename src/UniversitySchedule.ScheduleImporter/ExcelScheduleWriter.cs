using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using UniversitySchedule.Contracts.Catalog;
using UniversitySchedule.ScheduleImporter.Sources;

namespace UniversitySchedule.ScheduleImporter;

public sealed class ExcelScheduleWriter(ImportOptions options)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public async Task WriteAsync(
        ManualScheduleOverrideDocument parsed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        string manualPath = options.ManualScheduleOutputPath
            ?? throw new InvalidOperationException("Manual schedule output path is not configured.");
        Directory.CreateDirectory(Path.GetDirectoryName(manualPath)!);

        ManualScheduleOverrideDocument? previous = await ReadManualScheduleAsync(manualPath, cancellationToken);
        ManualScheduleOverrideDocument effective = MergeManualSchedules(previous, parsed, options.ReplaceExcelGroups);
        ReferenceCatalogSnapshot? catalog = await ReadCatalogAsync(cancellationToken);
        ManualScheduleOverrideDocument output = new()
        {
            SourceFile = effective.SourceFile,
            ImportedAtUtc = effective.ImportedAtUtc,
            Bells = effective.Bells,
            Weeks = effective.Weeks,
            Groups = effective.Groups,
            GroupCourses = effective.GroupCourses,
            Tree = BuildTree(catalog, effective.Groups, effective.GroupCourses, options.AcademicYear),
        };

        await WriteAtomicAsync(manualPath, JsonSerializer.Serialize(output, JsonOptions), cancellationToken);
        if (catalog is not null)
        {
            ReferenceCatalogSnapshot merged = MergeCatalog(catalog, effective);
            await WriteAtomicAsync(
                options.OutputPath,
                JsonSerializer.Serialize(merged, JsonOptions),
                cancellationToken);
            await WriteAtomicAsync(
                Path.Combine(options.ReportsDirectory, "excel-schedule-import.md"),
                BuildReport(effective, merged, parsed.Groups.Count, options.ReplaceExcelGroups),
                cancellationToken);
        }
    }

    private static async Task<ManualScheduleOverrideDocument?> ReadManualScheduleAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ManualScheduleOverrideDocument>(stream, JsonOptions, cancellationToken);
    }

    private static ManualScheduleOverrideDocument MergeManualSchedules(
        ManualScheduleOverrideDocument? previous,
        ManualScheduleOverrideDocument incoming,
        bool replaceGroups)
    {
        if (previous is null || previous.Groups.Count == 0)
        {
            return incoming;
        }

        var groups = previous.Groups
            .ToDictionary(group => ExcelScheduleImporter.NormalizeGroupCode(group.Code), StringComparer.OrdinalIgnoreCase);
        var courses = previous.GroupCourses.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in incoming.GroupCourses) courses[pair.Key] = pair.Value;
        foreach (CfuGroupScheduleDocument incomingGroup in incoming.Groups)
        {
            string code = incomingGroup.Code.Trim();
            if (replaceGroups || !groups.TryGetValue(code, out CfuGroupScheduleDocument? previousGroup))
            {
                groups[code] = incomingGroup;
                continue;
            }

            var lessons = previousGroup.Lessons
                .GroupBy(lesson => LessonSlotKey(code, lesson), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
            foreach (CfuLessonDocument lesson in incomingGroup.Lessons)
            {
                lessons[LessonSlotKey(code, lesson)] = lesson;
            }

            groups[code] = new CfuGroupScheduleDocument
            {
                Code = incomingGroup.Code,
                Lessons = lessons.Values
                    .OrderBy(lesson => lesson.Parity, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(lesson => lesson.Day)
                    .ThenBy(lesson => lesson.PairNumber)
                    .ThenBy(lesson => lesson.Subgroup)
                    .ThenBy(lesson => lesson.Subject, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray(),
            };
        }

        return new ManualScheduleOverrideDocument
        {
            SourceFile = incoming.SourceFile,
            ImportedAtUtc = incoming.ImportedAtUtc,
            Bells = incoming.Bells,
            Weeks = incoming.Weeks,
            GroupCourses = courses,
            Groups = groups.Values
                .OrderBy(group => group.Code, StringComparer.CurrentCultureIgnoreCase)
                .ToArray(),
        };
    }

    private async Task<ReferenceCatalogSnapshot?> ReadCatalogAsync(CancellationToken cancellationToken)
    {
        string path = options.CatalogInputPath ?? options.OutputPath;
        if (!File.Exists(path))
        {
            return null;
        }

        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ReferenceCatalogSnapshot>(stream, JsonOptions, cancellationToken);
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>> BuildTree(
        ReferenceCatalogSnapshot? catalog,
        IReadOnlyList<CfuGroupScheduleDocument> groups,
        IReadOnlyDictionary<string, int> sourceCourses,
        int academicYear)
    {
        var mutable = new Dictionary<string, Dictionary<string, Dictionary<string, HashSet<string>>>>(StringComparer.CurrentCultureIgnoreCase);
        if (catalog is not null)
        {
            foreach (AcademicProgramReference program in catalog.Programs)
            {
                Dictionary<string, Dictionary<string, HashSet<string>>> directions = GetOrAdd(mutable, program.InstituteName);
                Dictionary<string, HashSet<string>> courses = GetOrAdd(directions, program.SourceDirectionName);
                foreach (string group in program.Groups.Where(value => !string.IsNullOrWhiteSpace(value)))
                {
                    GetOrAdd(courses, InferCourse(group, sourceCourses, academicYear)).Add(group.Trim());
                }
            }
        }

        foreach (CfuGroupScheduleDocument group in groups)
        {
            bool alreadyKnown = mutable.Values
                .SelectMany(directions => directions.Values)
                .SelectMany(courses => courses.Values)
                .Any(values => values.Contains(group.Code.Trim()));
            if (alreadyKnown)
            {
                continue;
            }

            // Reuse a unique known program with the same group prefix and study level.
            AcademicProgramReference[] matches = catalog?.Programs.Where(program => program.Groups.Any(known =>
                GroupPrefix(known) == GroupPrefix(group.Code))).ToArray() ?? [];
            AcademicProgramReference? match = matches.Length == 1 ? matches[0] : null;
            Dictionary<string, Dictionary<string, HashSet<string>>> directions = GetOrAdd(mutable, match?.InstituteName ?? "Excel импорт");
            Dictionary<string, HashSet<string>> courses = GetOrAdd(directions, match?.SourceDirectionName ?? "99.99.99 Excel расписание");
            GetOrAdd(courses, InferCourse(group.Code, sourceCourses, academicYear)).Add(group.Code.Trim());
        }

        return mutable.ToDictionary(
            institute => institute.Key,
            institute => (IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>)institute.Value.ToDictionary(
                direction => direction.Key,
                direction => (IReadOnlyDictionary<string, IReadOnlyList<string>>)direction.Value.ToDictionary(
                    course => course.Key,
                    course => (IReadOnlyList<string>)course.Value.OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase).ToArray(),
                    StringComparer.OrdinalIgnoreCase),
                StringComparer.CurrentCultureIgnoreCase),
            StringComparer.CurrentCultureIgnoreCase);
    }

    private static ReferenceCatalogSnapshot MergeCatalog(
        ReferenceCatalogSnapshot catalog,
        ManualScheduleOverrideDocument parsed)
    {
        var overriddenGroups = parsed.Groups.Select(group => ExcelScheduleImporter.NormalizeGroupCode(group.Code)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var additions = new Dictionary<string, List<TeacherScheduleEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (CfuGroupScheduleDocument group in parsed.Groups)
        {
            foreach (CfuLessonDocument lesson in group.Lessons)
            {
                TeacherScheduleEntry entry = new(
                    group.Code.Trim(),
                    lesson.Subgroup,
                    lesson.Day,
                    lesson.PairNumber,
                    lesson.Parity.Trim(),
                    lesson.Date,
                    lesson.Subject.Trim(),
                    NormalizeOptional(lesson.LessonType),
                    NormalizeOptional(lesson.Classroom),
                    NormalizeOptional(lesson.Building),
                    NormalizeOptional(lesson.Note),
                    NormalizeOptional(lesson.Online));
                foreach (string teacherName in lesson.Teachers.Where(value => !string.IsNullOrWhiteSpace(value)))
                {
                    if (!TeacherIdentityParser.TryParse(teacherName, out TeacherIdentity identity))
                    {
                        continue;
                    }

                    if (!additions.TryGetValue(identity.Key, out List<TeacherScheduleEntry>? entries))
                    {
                        entries = [];
                        additions.Add(identity.Key, entries);
                    }

                    if (!entries.Any(existing => EntryKey(existing) == EntryKey(entry)))
                    {
                        entries.Add(entry);
                    }
                }
            }
        }

        var existingByKey = catalog.Teachers
            .GroupBy(teacher => teacher.IdentityKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var mergedTeachers = new List<TeacherReference>();
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (TeacherReference teacher in catalog.Teachers)
        {
            // Replace all entries for overridden groups, including teachers removed by a substitution.
            TeacherScheduleEntry[] retained = teacher.Schedule
                .Where(entry => !overriddenGroups.Contains(ExcelScheduleImporter.NormalizeGroupCode(entry.GroupCode))).ToArray();
            if (!additions.TryGetValue(teacher.IdentityKey, out List<TeacherScheduleEntry>? extra) ||
                existingByKey[teacher.IdentityKey].Length > 1)
            {
                mergedTeachers.Add(teacher with
                {
                    Schedule = retained,
                    MatchStatus = retained.Length == 0 && teacher.MatchStatus != TeacherScheduleMatchStatus.Ambiguous
                        ? TeacherScheduleMatchStatus.NoPublishedSchedule : teacher.MatchStatus,
                });
                continue;
            }

            matched.Add(teacher.IdentityKey);
            TeacherScheduleEntry[] schedule = retained
                .Concat(extra)
                .GroupBy(EntryKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(entry => entry.Day)
                .ThenBy(entry => entry.PairNumber)
                .ThenBy(entry => entry.GroupCode, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            mergedTeachers.Add(teacher with
            {
                Schedule = schedule,
                MatchStatus = schedule.Length > 0 && teacher.MatchStatus != TeacherScheduleMatchStatus.ScheduleOnly
                    ? TeacherScheduleMatchStatus.Exact : teacher.MatchStatus,
            });
        }

        foreach ((string key, List<TeacherScheduleEntry> extra) in additions)
        {
            if (matched.Contains(key) || existingByKey.ContainsKey(key))
            {
                continue;
            }

            TeacherIdentityParser.TryParse(key.Replace('|', ' '), out TeacherIdentity identity);
            mergedTeachers.Add(new TeacherReference(
                CatalogStableId.Create("teacher", key),
                key,
                identity.ScheduleDisplayName,
                identity.ScheduleDisplayName,
                identity.Surname,
                Position: null,
                Disciplines: [],
                Specialties: [],
                Schedule: extra,
                MatchStatus: TeacherScheduleMatchStatus.ScheduleOnly,
                VuzopediaUrl: null));
        }

        TeacherReference[] orderedTeachers = mergedTeachers
            .OrderBy(teacher => teacher.FullName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(teacher => teacher.VuzopediaUrl, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        ReferenceCatalogStatistics statistics = catalog.Statistics with
        {
            TeacherCount = orderedTeachers.Length,
            EnrichedTeacherProfileCount = orderedTeachers.Count(teacher =>
                teacher.Position is not null || teacher.Disciplines.Count > 0 || teacher.Specialties.Count > 0),
            TeachersWithScheduleCount = orderedTeachers.Count(teacher => teacher.Schedule.Count > 0),
            TeachersWithoutScheduleCount = orderedTeachers.Count(teacher => teacher.Schedule.Count == 0),
            AmbiguousTeacherMatchesCount = orderedTeachers.Count(teacher => teacher.MatchStatus == TeacherScheduleMatchStatus.Ambiguous),
            ScheduleOnlyTeacherCount = orderedTeachers.Count(teacher => teacher.MatchStatus == TeacherScheduleMatchStatus.ScheduleOnly),
        };

        return catalog with
        {
            GeneratedAtUtc = parsed.ImportedAtUtc,
            Calendar = new ReferenceScheduleCalendar(
                parsed.Bells.Select(bell => new ReferenceBell(bell.PairNumber, bell.StartsAt, bell.EndsAt)).ToArray(),
                parsed.Weeks.EvenWeekMondays,
                parsed.Weeks.OddWeekMondays),
            Teachers = orderedTeachers,
            Statistics = statistics,
        };
    }

    private static Dictionary<string, Dictionary<string, HashSet<string>>> GetOrAdd(
        IDictionary<string, Dictionary<string, Dictionary<string, HashSet<string>>>> dictionary,
        string key)
    {
        if (!dictionary.TryGetValue(key, out Dictionary<string, Dictionary<string, HashSet<string>>>? value))
        {
            value = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.CurrentCultureIgnoreCase);
            dictionary.Add(key, value);
        }

        return value;
    }

    private static Dictionary<string, HashSet<string>> GetOrAdd(
        IDictionary<string, Dictionary<string, HashSet<string>>> dictionary,
        string key)
    {
        if (!dictionary.TryGetValue(key, out Dictionary<string, HashSet<string>>? value))
        {
            value = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            dictionary.Add(key, value);
        }

        return value;
    }

    private static HashSet<string> GetOrAdd(
        IDictionary<string, HashSet<string>> dictionary,
        string key)
    {
        if (!dictionary.TryGetValue(key, out HashSet<string>? value))
        {
            value = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            dictionary.Add(key, value);
        }

        return value;
    }

    private static string GroupPrefix(string groupCode) =>
        string.Concat(ExcelScheduleImporter.NormalizeGroupCode(groupCode).Reverse().SkipWhile(char.IsDigit).Reverse()).ToLowerInvariant();

    private static string InferCourse(string groupCode, IReadOnlyDictionary<string, int> sourceCourses, int academicYear)
    {
        if (sourceCourses.TryGetValue(groupCode, out int course)) return course.ToString(CultureInfo.InvariantCulture);
        string digits = new(groupCode.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return digits.Length == 3 && int.TryParse(digits[..2], out int admissionYear)
            ? Math.Clamp(academicYear - (2000 + admissionYear) + 1, 1, 6).ToString(CultureInfo.InvariantCulture)
            : "1";
    }

    private static string EntryKey(TeacherScheduleEntry entry) => string.Join('|',
        entry.GroupCode,
        entry.Subgroup,
        entry.Day,
        entry.PairNumber,
        entry.Parity,
        entry.Date,
        entry.Subject,
        entry.LessonType,
        entry.Classroom,
        entry.Building);

    private static string LessonSlotKey(string groupCode, CfuLessonDocument lesson) => string.Join('|',
        groupCode.Trim(),
        lesson.Subgroup,
        lesson.Day,
        lesson.PairNumber,
        lesson.Parity,
        lesson.Date);

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string BuildReport(ManualScheduleOverrideDocument parsed, ReferenceCatalogSnapshot merged, int incomingGroups, bool replaceGroups)
    {
        int lessonCount = parsed.Groups.Sum(group => group.Lessons.Count);
        var builder = new StringBuilder();
        builder.AppendLine("# Импорт расписания из Excel");
        builder.AppendLine();
        builder.AppendLine($"Источник: `{parsed.SourceFile}`.");
        builder.AppendLine($"Дата импорта: {parsed.ImportedAtUtc:yyyy-MM-dd HH:mm} UTC.");
        builder.AppendLine();
        builder.AppendLine($"- Групп в новом источнике: {incomingGroups}");
        builder.AppendLine($"- Всего групп в локальном расписании: {parsed.Groups.Count}");
        builder.AppendLine($"- Режим: {(replaceGroups ? "полная замена расписания групп из нового источника" : "добавление и обновление отдельных строк")}");
        builder.AppendLine($"- Всего записей занятий (недельные шаблоны и датированные занятия): {lessonCount}");
        builder.AppendLine($"- Преподавателей с расписанием после объединения: {merged.Statistics.TeachersWithScheduleCount}");
        builder.AppendLine();
        builder.AppendLine("Расписание из этого файла имеет приоритет над официальным источником для перечисленных групп. Для остальных групп приложение продолжает использовать официальный API КФУ.");
        return builder.ToString();
    }

    private static async Task WriteAtomicAsync(string path, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporaryPath, content, new UTF8Encoding(false), cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }
}
