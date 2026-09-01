using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using UniversitySchedule.Contracts.Catalog;
using UniversitySchedule.ScheduleImporter.Sources;

namespace UniversitySchedule.ScheduleImporter;

public sealed partial class ReferenceCatalogBuilder(
    CfuScheduleSourceClient cfuSource,
    VuzopediaSourceClient vuzopediaSource,
    ImportOptions options,
    ILogger<ReferenceCatalogBuilder> logger)
{
    private static readonly IReadOnlyDictionary<string, int> ExpectedProgramCounts =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Агротехнологическая академия"] = 12,
            ["Академия строительства и архитектуры"] = 11,
            ["Институт биохимических технологий, экологии и фармации"] = 3,
            ["Институт медиакоммуникаций, медиатехнологий и дизайна"] = 11,
            ["Институт экономики и управления"] = 14,
            ["Исторический факультет"] = 6,
            ["Факультет географии, геоэкологии и туризма"] = 2,
            ["Факультет психологии"] = 9,
            ["Факультет русского языка и литературы"] = 3,
            ["Физико-технический институт"] = 27,
            ["Филологический факультет"] = 5,
            ["Философский факультет"] = 9,
            ["Юридический факультет"] = 3,
        };
    private static readonly string[] SupplementalPhilologyCodes = ["45.05.01", "58.03.01", "58.04.01"];

    private readonly CfuScheduleSourceClient _cfuSource = cfuSource;
    private readonly VuzopediaSourceClient _vuzopediaSource = vuzopediaSource;
    private readonly ImportOptions _options = options;
    private readonly ILogger<ReferenceCatalogBuilder> _logger = logger;

    public async Task<ReferenceCatalogSnapshot> BuildAsync(CancellationToken cancellationToken)
    {
        CfuScheduleIndexDocument index = await _cfuSource.LoadIndexAsync(cancellationToken);
        IReadOnlyList<CfuGroupScheduleSource> groupSchedules =
            await _cfuSource.LoadGroupSchedulesAsync(index, cancellationToken);
        IReadOnlyList<VuzopediaProgram> vuzopediaPrograms =
            await _vuzopediaSource.LoadProgramsAsync(cancellationToken);
        IReadOnlyList<VuzopediaTeacherProfile> vuzopediaTeachers =
            await _vuzopediaSource.LoadTeachersAsync(cancellationToken);

        IReadOnlyList<AcademicProgramReference> programs = BuildPrograms(index, groupSchedules, vuzopediaPrograms);
        IReadOnlyDictionary<string, IReadOnlyList<TeacherScheduleEntry>> teacherSchedules =
            BuildTeacherSchedules(groupSchedules);
        IReadOnlyList<TeacherReference> teachers = BuildTeachers(vuzopediaTeachers, teacherSchedules);
        HashSet<string> matchedProgramUrls = programs
            .Select(program => program.VuzopediaUrl)
            .Where(url => url is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        ReferenceCatalogDiscrepancy[] sourceOnlyPrograms = vuzopediaPrograms
            .Where(program => !matchedProgramUrls.Contains(program.Url))
            .Select(program => new ReferenceCatalogDiscrepancy(
                "Vuzopedia",
                program.Code,
                program.Name,
                program.Level,
                program.StudyForms,
                program.Url))
            .OrderBy(program => program.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(program => program.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        ReferenceCatalogStatistics statistics = new(
            InstituteCount: programs.Select(program => program.InstituteId).Distinct().Count(),
            ProgramCount: programs.Count,
            GroupCount: programs.SelectMany(program => program.Groups).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            TeacherCount: teachers.Count,
            EnrichedTeacherProfileCount: teachers.Count(teacher =>
                teacher.Position is not null || teacher.Disciplines.Count > 0 || teacher.Specialties.Count > 0),
            TeachersWithScheduleCount: teachers.Count(teacher => teacher.Schedule.Count > 0),
            TeachersWithoutScheduleCount: teachers.Count(teacher => teacher.Schedule.Count == 0),
            AmbiguousTeacherMatchesCount: teachers.Count(teacher => teacher.MatchStatus == TeacherScheduleMatchStatus.Ambiguous),
            ScheduleOnlyTeacherCount: teachers.Count(teacher => teacher.MatchStatus == TeacherScheduleMatchStatus.ScheduleOnly),
            ProgramsWithoutScheduleCount: programs.Count(program => !program.HasPublishedSchedule),
            UnmatchedVuzopediaProgramsCount: vuzopediaPrograms.Count(program => !matchedProgramUrls.Contains(program.Url)),
            TeacherDetailsIncluded: !_options.SkipTeacherDetails);

        _logger.LogInformation(
            "Built reference catalog: {Institutes} institutes, {Programs} programs, {Groups} groups, {Teachers} teachers",
            statistics.InstituteCount,
            statistics.ProgramCount,
            statistics.GroupCount,
            statistics.TeacherCount);

        return new ReferenceCatalogSnapshot(
            SchemaVersion: 1,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Sources: new ReferenceCatalogSources(
                CfuScheduleSourceClient.IndexUrl,
                CfuScheduleSourceClient.ApiBaseUrl,
                VuzopediaSourceClient.TeachersUrl,
                VuzopediaSourceClient.SpecialtiesUrl),
            Calendar: new ReferenceScheduleCalendar(
                index.Bells.Select(bell => new ReferenceBell(bell.PairNumber, bell.StartsAt, bell.EndsAt)).ToArray(),
                index.Weeks.EvenWeekMondays,
                index.Weeks.OddWeekMondays),
            Programs: programs,
            Teachers: teachers,
            SourceOnlyPrograms: sourceOnlyPrograms,
            Statistics: statistics);
    }

    internal static IReadOnlyList<AcademicProgramReference> BuildPrograms(
        CfuScheduleIndexDocument index,
        IReadOnlyList<CfuGroupScheduleSource> groupSchedules,
        IReadOnlyList<VuzopediaProgram> vuzopediaPrograms)
    {
        HashSet<string> groupsWithSchedule = groupSchedules
            .Where(item => item.Schedule.Lessons.Count > 0 || item.Schedule.FacultyLessons.Count > 0)
            .Select(item => item.Group.GroupCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        ILookup<string, VuzopediaProgram> byCode = vuzopediaPrograms.ToLookup(
            program => NormalizeCode(program.Code),
            StringComparer.OrdinalIgnoreCase);
        var result = new List<AcademicProgramReference>();

        foreach ((string instituteName, IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> directions) in index.Tree)
        {
            Guid instituteId = CatalogStableId.Create("institute", instituteName);
            foreach ((string directionName, IReadOnlyDictionary<string, IReadOnlyList<string>> courses) in directions)
            {
                Match directionMatch = DirectionRegex().Match(directionName.Trim());
                if (!directionMatch.Success)
                {
                    continue;
                }

                string code = directionMatch.Groups["code"].Value;
                string name = NormalizeText(directionMatch.Groups["name"].Value.TrimStart('.', ' '));
                string[] groups = courses.Values
                    .SelectMany(values => values)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(NormalizeText)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();
                VuzopediaProgram? vuzopedia = FindVuzopediaProgram(code, name, byCode[NormalizeCode(code)]);
                EducationLevel level = InferEducationLevel(code);
                StudyForm[] forms = InferStudyForms(groups);

                result.Add(new AcademicProgramReference(
                    Id: CatalogStableId.Create("direction", instituteName, directionName),
                    InstituteId: instituteId,
                    InstituteName: instituteName,
                    SourceDirectionName: directionName,
                    Code: code,
                    Name: name,
                    Level: level == EducationLevel.Unknown && vuzopedia is not null ? vuzopedia.Level : level,
                    StudyForms: forms.Length == 0 && vuzopedia is not null ? vuzopedia.StudyForms : forms,
                    Groups: groups,
                    HasPublishedSchedule: groups.Any(groupsWithSchedule.Contains),
                    VuzopediaUrl: vuzopedia?.Url));
            }
        }

        AddSupplementalPhilologyPrograms(result, vuzopediaPrograms);
        ValidateExpectedStructure(result);

        return result
            .OrderBy(program => program.InstituteName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(program => program.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(program => program.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static void AddSupplementalPhilologyPrograms(
        ICollection<AcademicProgramReference> programs,
        IReadOnlyList<VuzopediaProgram> vuzopediaPrograms)
    {
        const string instituteName = "Филологический факультет";
        Guid instituteId = CatalogStableId.Create("institute", instituteName);
        foreach (string code in SupplementalPhilologyCodes)
        {
            if (programs.Any(program =>
                    string.Equals(program.InstituteName, instituteName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(program.Code, code, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            VuzopediaProgram program = vuzopediaPrograms.Single(item =>
                string.Equals(NormalizeCode(item.Code), code, StringComparison.OrdinalIgnoreCase));
            string sourceName = $"{program.Code} {program.Name}";
            programs.Add(new AcademicProgramReference(
                CatalogStableId.Create("direction", instituteName, sourceName),
                instituteId,
                instituteName,
                sourceName,
                program.Code,
                program.Name,
                program.Level,
                program.StudyForms,
                Groups: [],
                HasPublishedSchedule: false,
                VuzopediaUrl: program.Url));
        }
    }

    private static void ValidateExpectedStructure(IReadOnlyCollection<AcademicProgramReference> programs)
    {
        Dictionary<string, int> actual = programs
            .GroupBy(program => program.InstituteName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        string[] differences = ExpectedProgramCounts
            .Where(expected => !actual.TryGetValue(expected.Key, out int count) || count != expected.Value)
            .Select(expected => $"{expected.Key}: expected {expected.Value}, actual {actual.GetValueOrDefault(expected.Key)}")
            .ToArray();
        if (actual.Count != ExpectedProgramCounts.Count || differences.Length > 0)
        {
            throw new InvalidDataException(
                "The curated 13/115 CFU program structure changed. " + string.Join("; ", differences));
        }
    }

    internal static IReadOnlyDictionary<string, IReadOnlyList<TeacherScheduleEntry>> BuildTeacherSchedules(
        IReadOnlyList<CfuGroupScheduleSource> groupSchedules)
    {
        var entries = new Dictionary<string, Dictionary<string, TeacherScheduleEntry>>(StringComparer.OrdinalIgnoreCase);

        foreach (CfuGroupScheduleSource source in groupSchedules)
        {
            foreach (CfuLessonDocument lesson in source.Schedule.Lessons)
            {
                string groupCode = NormalizeText(string.IsNullOrWhiteSpace(lesson.GroupCode)
                    ? source.Group.GroupCode
                    : lesson.GroupCode);
                TeacherScheduleEntry entry = new(
                    groupCode,
                    lesson.Subgroup,
                    lesson.Day,
                    lesson.PairNumber,
                    NormalizeText(lesson.Parity),
                    NormalizeOptional(lesson.Date),
                    NormalizeText(lesson.Subject),
                    NormalizeOptional(lesson.LessonType),
                    NormalizeOptional(lesson.Classroom),
                    NormalizeOptional(lesson.Building),
                    NormalizeOptional(lesson.Note),
                    NormalizeOptional(lesson.Online));
                AddTeacherEntries(entries, lesson.Teachers, entry);
            }

            foreach (CfuFacultyLessonDocument lesson in source.Schedule.FacultyLessons)
            {
                TeacherScheduleEntry entry = new(
                    NormalizeText(lesson.GroupCode ?? source.Group.GroupCode),
                    Subgroup: 0,
                    lesson.Day,
                    lesson.PairNumber,
                    NormalizeText(lesson.Period),
                    Date: null,
                    NormalizeText(lesson.Subject),
                    NormalizeOptional(lesson.LessonType),
                    NormalizeOptional(lesson.Classroom),
                    NormalizeOptional(lesson.Building),
                    Note: null,
                    Online: null);
                AddTeacherEntries(entries, lesson.Teachers, entry);
            }
        }

        return entries.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<TeacherScheduleEntry>)pair.Value.Values
                .OrderBy(entry => entry.Day)
                .ThenBy(entry => entry.PairNumber)
                .ThenBy(entry => entry.GroupCode, StringComparer.CurrentCultureIgnoreCase)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    internal static IReadOnlyList<TeacherReference> BuildTeachers(
        IReadOnlyList<VuzopediaTeacherProfile> profiles,
        IReadOnlyDictionary<string, IReadOnlyList<TeacherScheduleEntry>> schedules)
    {
        var result = new List<TeacherReference>();
        var matchedScheduleKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ILookup<string, VuzopediaTeacherProfile> groupedProfiles = profiles
            .Select(profile => (Profile: profile, Identity: TryIdentity(profile.FullName)))
            .Where(value => value.Identity is not null)
            .ToLookup(value => value.Identity!.Key, value => value.Profile, StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, VuzopediaTeacherProfile> group in groupedProfiles)
        {
            VuzopediaTeacherProfile[] candidates = group
                .GroupBy(profile => profile.Url, StringComparer.OrdinalIgnoreCase)
                .Select(values => values.First())
                .ToArray();
            bool ambiguous = candidates.Length > 1;
            IReadOnlyList<TeacherScheduleEntry> schedule = schedules.TryGetValue(group.Key, out IReadOnlyList<TeacherScheduleEntry>? found)
                ? found
                : [];
            if (!ambiguous && schedule.Count > 0)
            {
                matchedScheduleKeys.Add(group.Key);
            }

            foreach (VuzopediaTeacherProfile profile in candidates)
            {
                TeacherIdentity identity = TryIdentity(profile.FullName)!;
                result.Add(new TeacherReference(
                    Id: CatalogStableId.Create("teacher", ambiguous ? profile.Url : identity.Key),
                    IdentityKey: identity.Key,
                    FullName: profile.FullName,
                    ScheduleDisplayName: identity.ScheduleDisplayName,
                    Surname: identity.Surname,
                    Position: profile.Position,
                    Disciplines: profile.Disciplines,
                    Specialties: profile.Specialties.Select(ToReference).ToArray(),
                    Schedule: ambiguous ? [] : schedule,
                    MatchStatus: ambiguous
                        ? TeacherScheduleMatchStatus.Ambiguous
                        : schedule.Count > 0
                            ? TeacherScheduleMatchStatus.Exact
                            : TeacherScheduleMatchStatus.NoPublishedSchedule,
                    VuzopediaUrl: profile.Url));
            }
        }

        foreach ((string key, IReadOnlyList<TeacherScheduleEntry> schedule) in schedules)
        {
            if (matchedScheduleKeys.Contains(key) || groupedProfiles.Contains(key))
            {
                continue;
            }

            TeacherIdentity identity = ParseIdentityKey(key);
            result.Add(new TeacherReference(
                CatalogStableId.Create("teacher", key),
                key,
                identity.ScheduleDisplayName,
                identity.ScheduleDisplayName,
                identity.Surname,
                Position: null,
                Disciplines: [],
                Specialties: [],
                Schedule: schedule,
                MatchStatus: TeacherScheduleMatchStatus.ScheduleOnly,
                VuzopediaUrl: null));
        }

        return result
            .OrderBy(teacher => teacher.FullName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(teacher => teacher.VuzopediaUrl, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddTeacherEntries(
        IDictionary<string, Dictionary<string, TeacherScheduleEntry>> schedules,
        IEnumerable<string> teacherNames,
        TeacherScheduleEntry entry)
    {
        foreach (string teacherName in teacherNames.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (!TeacherIdentityParser.TryParse(teacherName, out TeacherIdentity identity))
            {
                continue;
            }

            if (!schedules.TryGetValue(identity.Key, out Dictionary<string, TeacherScheduleEntry>? teacherEntries))
            {
                teacherEntries = new Dictionary<string, TeacherScheduleEntry>(StringComparer.OrdinalIgnoreCase);
                schedules.Add(identity.Key, teacherEntries);
            }

            string entryKey = string.Join('|',
                entry.GroupCode,
                entry.Subgroup.ToString(CultureInfo.InvariantCulture),
                entry.Day.ToString(CultureInfo.InvariantCulture),
                entry.PairNumber.ToString(CultureInfo.InvariantCulture),
                entry.Parity,
                entry.Date,
                entry.Subject,
                entry.LessonType,
                entry.Classroom,
                entry.Building);
            teacherEntries.TryAdd(entryKey, entry);
        }
    }

    private static VuzopediaProgram? FindVuzopediaProgram(
        string code,
        string name,
        IEnumerable<VuzopediaProgram> candidates)
    {
        VuzopediaProgram[] exact = candidates
            .Where(program => NormalizeComparable(program.Name) == NormalizeComparable(name))
            .ToArray();
        if (exact.Length == 1)
        {
            return exact[0];
        }

        VuzopediaProgram[] values = candidates.ToArray();
        return values.Length == 1 ? values[0] : null;
    }

    private static TeacherSpecialtyReference ToReference(VuzopediaProgram program) => new(
        program.Code,
        program.Name,
        program.Level,
        program.StudyForms,
        program.Url);

    private static TeacherIdentity? TryIdentity(string name) =>
        TeacherIdentityParser.TryParse(name, out TeacherIdentity identity) ? identity : null;

    private static TeacherIdentity ParseIdentityKey(string key)
    {
        string[] parts = key.Split('|');
        string surname = parts.Length > 0
            ? CultureInfo.GetCultureInfo("ru-RU").TextInfo.ToTitleCase(parts[0])
            : key;
        char? first = parts.Length > 1 && parts[1].Length > 0 ? parts[1][0] : null;
        char? middle = parts.Length > 2 && parts[2].Length > 0 ? parts[2][0] : null;
        return new TeacherIdentity(key, surname, first, middle);
    }

    private static EducationLevel InferEducationLevel(string code)
    {
        string normalized = NormalizeCode(code);
        if (normalized.StartsWith("1.", StringComparison.Ordinal))
        {
            return EducationLevel.Postgraduate;
        }

        string[] parts = normalized.Split('.');
        return parts.Length > 1 ? parts[1] switch
        {
            "03" => EducationLevel.Bachelor,
            "04" => EducationLevel.Master,
            "05" => EducationLevel.Specialist,
            _ => EducationLevel.Unknown,
        } : EducationLevel.Unknown;
    }

    private static StudyForm[] InferStudyForms(IEnumerable<string> groups)
    {
        return groups.Select(group => group.ToLowerInvariant().Replace('ё', 'е'))
            .Select(group => group.Contains("-оз-", StringComparison.Ordinal) ? StudyForm.PartTime
                : group.Contains("-з-", StringComparison.Ordinal) ? StudyForm.Extramural
                : group.Contains("-о-", StringComparison.Ordinal) ? StudyForm.FullTime
                : StudyForm.Unknown)
            .Where(form => form != StudyForm.Unknown)
            .Distinct()
            .OrderBy(form => form)
            .ToArray();
    }

    private static string NormalizeCode(string value) => NormalizeText(value).Trim('.');

    private static string NormalizeComparable(string value) => string.Concat(
        NormalizeText(value).ToLowerInvariant().Replace('ё', 'е').Where(char.IsLetterOrDigit));

    private static string NormalizeText(string? value) => string.Join(' ', (value ?? string.Empty)
        .Replace('\u00a0', ' ')
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string? NormalizeOptional(string? value)
    {
        string normalized = NormalizeText(value);
        return normalized.Length == 0 ? null : normalized;
    }

    [GeneratedRegex(@"^(?<code>\d+(?:\.\d+){2})\s*(?<name>.*)$")]
    private static partial Regex DirectionRegex();
}
