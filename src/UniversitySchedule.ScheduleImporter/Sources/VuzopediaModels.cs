using UniversitySchedule.Contracts.Catalog;

namespace UniversitySchedule.ScheduleImporter.Sources;

public sealed record VuzopediaTeacherListItem(
    string FullName,
    string Url);

public sealed record VuzopediaTeacherProfile(
    string FullName,
    string? Position,
    IReadOnlyList<string> Disciplines,
    IReadOnlyList<VuzopediaProgram> Specialties,
    string Url);

public sealed record VuzopediaProgram(
    string Code,
    string Name,
    EducationLevel Level,
    IReadOnlyList<StudyForm> StudyForms,
    string Url);
