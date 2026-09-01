using UniversitySchedule.Contracts.Catalog;
using UniversitySchedule.Mobile.Core.Scheduling;

namespace UniversitySchedule.Mobile.Core.Tests.Scheduling;

public sealed class TeacherProfileCardTests
{
    [Fact]
    public void FromReference_FormatsAllDisciplinesAndSpecialties()
    {
        var teacher = new TeacherReference(
            Guid.NewGuid(),
            "атик|а|а",
            "Атик Аниса Ахмедовна",
            "Атик А.А.",
            "Атик",
            "доцент",
            ["Проектная деятельность", "Социология"],
            [
                new TeacherSpecialtyReference(
                    "45.03.01",
                    "Филология",
                    EducationLevel.Bachelor,
                    [StudyForm.FullTime, StudyForm.Extramural],
                    "https://example.test/specialty"),
            ],
            [],
            TeacherScheduleMatchStatus.NoPublishedSchedule,
            "https://example.test/teacher");

        TeacherProfileCard card = TeacherProfileCard.FromReference(teacher);

        Assert.Equal("Атик Аниса Ахмедовна", card.FullName);
        Assert.Equal("доцент", card.PositionText);
        Assert.Contains("1. Проектная деятельность", card.DisciplinesText);
        Assert.Contains("2. Социология", card.DisciplinesText);
        Assert.Contains("45.03.01 • Филология • бакалавриат • очная, заочная", card.SpecialtiesText);
        Assert.True(card.HasDisciplines);
        Assert.True(card.HasSpecialties);
        Assert.False(card.HasNoAcademicDetails);
    }

    [Fact]
    public void FromSummary_DoesNotInventMissingAcademicDetails()
    {
        TeacherProfileCard card = TeacherProfileCard.FromSummary(
            new TeacherSummary(Guid.NewGuid(), "Иванов И.И."));

        Assert.Equal("Должность не указана", card.PositionText);
        Assert.False(card.HasDisciplines);
        Assert.False(card.HasSpecialties);
        Assert.True(card.HasNoAcademicDetails);
    }
}
