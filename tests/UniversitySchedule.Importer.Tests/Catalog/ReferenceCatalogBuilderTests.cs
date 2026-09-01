using UniversitySchedule.Contracts.Catalog;
using UniversitySchedule.ScheduleImporter;
using UniversitySchedule.ScheduleImporter.Sources;

namespace UniversitySchedule.Importer.Tests.Catalog;

public sealed class ReferenceCatalogBuilderTests
{
    [Fact]
    public void BuildTeachers_DoesNotAssignScheduleToAmbiguousIdentity()
    {
        VuzopediaTeacherProfile[] profiles =
        [
            new("Иванов Иван Иванович", "доцент", ["Математика"], [], "https://example.test/one"),
            new("Иванов Илья Игоревич", "профессор", ["Физика"], [], "https://example.test/two"),
        ];
        var entry = new TeacherScheduleEntry("ПИ-б-о-252", 1, 1, 2, "чет", null, "Математика", "ЛК", "301", "А", null, null);
        var schedules = new Dictionary<string, IReadOnlyList<TeacherScheduleEntry>>
        {
            ["иванов|и|и"] = [entry],
        };

        IReadOnlyList<TeacherReference> result = ReferenceCatalogBuilder.BuildTeachers(profiles, schedules);

        Assert.Equal(2, result.Count);
        Assert.All(result, teacher => Assert.Equal(TeacherScheduleMatchStatus.Ambiguous, teacher.MatchStatus));
        Assert.All(result, teacher => Assert.Empty(teacher.Schedule));
    }

    [Fact]
    public void BuildTeacherSchedules_DeduplicatesLessonSeenThroughGroupSource()
    {
        var lesson = new CfuLessonDocument
        {
            GroupCode = "ПИ-б-о-252",
            Day = 1,
            PairNumber = 2,
            Parity = "чет",
            Subject = "Программирование",
            LessonType = "ПЗ",
            Teachers = ["Атик А.А."],
            Classroom = "301",
        };
        CfuGroupScheduleSource[] sources =
        [
            new(new CfuGroupSource("ФТИ", "09.03.04 Программная инженерия", "ПИ-б-о-252"),
                new CfuGroupScheduleDocument { Code = "ПИ-б-о-252", Lessons = [lesson] }),
            new(new CfuGroupSource("ФТИ", "09.03.04 Программная инженерия", "ПИ-б-о-252"),
                new CfuGroupScheduleDocument { Code = "ПИ-б-о-252", Lessons = [lesson] }),
        ];

        IReadOnlyDictionary<string, IReadOnlyList<TeacherScheduleEntry>> result =
            ReferenceCatalogBuilder.BuildTeacherSchedules(sources);

        Assert.Single(result["атик|а|а"]);
    }
}
