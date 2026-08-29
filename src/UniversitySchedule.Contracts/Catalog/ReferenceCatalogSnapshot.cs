namespace UniversitySchedule.Contracts.Catalog;

public sealed record ReferenceCatalogSnapshot(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    ReferenceCatalogSources Sources,
    ReferenceScheduleCalendar Calendar,
    IReadOnlyList<AcademicProgramReference> Programs,
    IReadOnlyList<TeacherReference> Teachers,
    IReadOnlyList<ReferenceCatalogDiscrepancy> SourceOnlyPrograms,
    ReferenceCatalogStatistics Statistics);

public sealed record ReferenceCatalogSources(
    string CfuScheduleIndexUrl,
    string CfuScheduleApiUrl,
    string VuzopediaTeachersUrl,
    string VuzopediaSpecialtiesUrl);

public sealed record ReferenceScheduleCalendar(
    IReadOnlyList<ReferenceBell> Bells,
    IReadOnlyList<string> EvenWeekMondays,
    IReadOnlyList<string> OddWeekMondays);

public sealed record ReferenceBell(
    int PairNumber,
    string StartsAt,
    string EndsAt);

public sealed record AcademicProgramReference(
    Guid Id,
    Guid InstituteId,
    string InstituteName,
    string SourceDirectionName,
    string Code,
    string Name,
    EducationLevel Level,
    IReadOnlyList<StudyForm> StudyForms,
    IReadOnlyList<string> Groups,
    bool HasPublishedSchedule,
    string? VuzopediaUrl);

public sealed record TeacherReference(
    Guid Id,
    string IdentityKey,
    string FullName,
    string ScheduleDisplayName,
    string Surname,
    string? Position,
    IReadOnlyList<string> Disciplines,
    IReadOnlyList<TeacherSpecialtyReference> Specialties,
    IReadOnlyList<TeacherScheduleEntry> Schedule,
    TeacherScheduleMatchStatus MatchStatus,
    string? VuzopediaUrl);

public sealed record TeacherSpecialtyReference(
    string Code,
    string Name,
    EducationLevel Level,
    IReadOnlyList<StudyForm> StudyForms,
    string? VuzopediaUrl);

public sealed record ReferenceCatalogDiscrepancy(
    string Source,
    string Code,
    string Name,
    EducationLevel Level,
    IReadOnlyList<StudyForm> StudyForms,
    string Url);

public sealed record TeacherScheduleEntry(
    string GroupCode,
    int Subgroup,
    int Day,
    int PairNumber,
    string Parity,
    string? Date,
    string Subject,
    string? LessonType,
    string? Classroom,
    string? Building,
    string? Note,
    string? Online);

public sealed record ReferenceCatalogStatistics(
    int InstituteCount,
    int ProgramCount,
    int GroupCount,
    int TeacherCount,
    int EnrichedTeacherProfileCount,
    int TeachersWithScheduleCount,
    int TeachersWithoutScheduleCount,
    int AmbiguousTeacherMatchesCount,
    int ScheduleOnlyTeacherCount,
    int ProgramsWithoutScheduleCount,
    int UnmatchedVuzopediaProgramsCount,
    bool TeacherDetailsIncluded);

public enum EducationLevel
{
    Unknown = 0,
    Bachelor = 1,
    Specialist = 2,
    Master = 3,
    Postgraduate = 4,
}

public enum StudyForm
{
    Unknown = 0,
    FullTime = 1,
    PartTime = 2,
    Extramural = 3,
}

public enum TeacherScheduleMatchStatus
{
    NoPublishedSchedule = 0,
    Exact = 1,
    Ambiguous = 2,
    ScheduleOnly = 3,
}
