using UniversitySchedule.Contracts.Catalog;

namespace UniversitySchedule.Mobile.Core.Scheduling;

public sealed record TeacherProfileCard(
    string FullName,
    string PositionText,
    string DisciplinesText,
    string SpecialtiesText,
    bool HasDisciplines,
    bool HasSpecialties,
    bool HasNoAcademicDetails)
{
    public static TeacherProfileCard FromReference(TeacherReference teacher)
    {
        ArgumentNullException.ThrowIfNull(teacher);

        string disciplines = string.Join(
            Environment.NewLine,
            teacher.Disciplines.Select((discipline, index) => $"{index + 1}. {discipline}"));
        string specialties = string.Join(
            Environment.NewLine,
            teacher.Specialties.Select((specialty, index) =>
                $"{index + 1}. {FormatSpecialty(specialty)}"));

        return new TeacherProfileCard(
            teacher.FullName,
            string.IsNullOrWhiteSpace(teacher.Position) ? "Должность не указана" : teacher.Position,
            disciplines,
            specialties,
            teacher.Disciplines.Count > 0,
            teacher.Specialties.Count > 0,
            teacher.Disciplines.Count == 0 && teacher.Specialties.Count == 0);
    }

    public static TeacherProfileCard FromSummary(TeacherSummary teacher)
    {
        ArgumentNullException.ThrowIfNull(teacher);

        return new TeacherProfileCard(
            teacher.DisplayName,
            string.IsNullOrWhiteSpace(teacher.SecondaryText) ? "Должность не указана" : teacher.SecondaryText,
            string.Empty,
            string.Empty,
            false,
            false,
            true);
    }

    private static string FormatSpecialty(TeacherSpecialtyReference specialty)
    {
        string[] details =
        [
            specialty.Code,
            specialty.Name,
            FormatLevel(specialty.Level),
            string.Join(", ", specialty.StudyForms.Select(FormatStudyForm)),
        ];

        return string.Join(" • ", details.Where(detail => !string.IsNullOrWhiteSpace(detail)));
    }

    private static string FormatLevel(EducationLevel level) => level switch
    {
        EducationLevel.Bachelor => "бакалавриат",
        EducationLevel.Specialist => "специалитет",
        EducationLevel.Master => "магистратура",
        EducationLevel.Postgraduate => "аспирантура",
        _ => string.Empty,
    };

    private static string FormatStudyForm(StudyForm form) => form switch
    {
        StudyForm.FullTime => "очная",
        StudyForm.PartTime => "очно-заочная",
        StudyForm.Extramural => "заочная",
        _ => string.Empty,
    };
}
