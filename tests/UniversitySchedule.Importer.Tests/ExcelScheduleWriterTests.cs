using System.Text.Json;
using System.Text.Json.Serialization;
using UniversitySchedule.Contracts.Catalog;
using UniversitySchedule.ScheduleImporter;
using UniversitySchedule.ScheduleImporter.Sources;

namespace UniversitySchedule.Importer.Tests;

public sealed class ExcelScheduleWriterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Write_ReplacesOnlyIncomingGroups_WhenExplicitlyRequested(bool replace)
    {
        string root = Directory.CreateTempSubdirectory("cfu-excel-replacement-test-").FullName;
        try
        {
            string manual = Path.Combine(root, "manual.json");
            string catalogPath = Path.Combine(root, "catalog.json");
            var options = new ImportOptions(catalogPath, root, root, false, false, TimeSpan.FromSeconds(5),
                ManualScheduleOutputPath: manual, CatalogInputPath: catalogPath, ReplaceExcelGroups: replace);
            var before = new ManualScheduleOverrideDocument
            {
                Groups = [Group("ПИ-б-о-252", Lesson(1, "Старая пара"), Lesson(2, "Убрана из нового файла")),
                    Group("ДРУГАЯ-б-о-251", Lesson(1, "Не менять"))],
            };
            await File.WriteAllTextAsync(manual, JsonSerializer.Serialize(before, JsonOptions));
            var teacher = new TeacherReference(Guid.NewGuid(), "иванов|и|и", "Иванов И.И.", "Иванов И.И.", "Иванов",
                "доцент", ["Дисциплина"], [],
                [Entry("ПИ-б-о-252", "Старая пара"), Entry("ДРУГАЯ-б-о-251", "Не менять")],
                TeacherScheduleMatchStatus.Exact, null);
            var catalog = new ReferenceCatalogSnapshot(1, DateTimeOffset.UtcNow,
                new("", "", "", ""), new([], [], []),
                [new(Guid.NewGuid(), Guid.NewGuid(), "ФТИ", "09.03.04 Программная инженерия", "09.03.04", "Программная инженерия",
                    EducationLevel.Bachelor, [], ["ПИ-б-о-251"], true, null)], [teacher], [],
                new(0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 0, true));
            await File.WriteAllTextAsync(catalogPath, JsonSerializer.Serialize(catalog, JsonOptions));
            var incoming = new ManualScheduleOverrideDocument
            {
                SourceFile = "update.xlsx", ImportedAtUtc = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero),
                GroupCourses = new Dictionary<string, int> { ["ПИ-б-о-252"] = 2 },
                Groups = [Group("ПИ-б-о-252", Lesson(3, "Новая пара"))],
            };
            var writer = new ExcelScheduleWriter(options);
            await writer.WriteAsync(incoming);
            var result = JsonSerializer.Deserialize<ManualScheduleOverrideDocument>(await File.ReadAllTextAsync(manual), JsonOptions)!;
            Assert.Equal(2, result.Groups.Count);
            Assert.Contains("ПИ-б-о-252", result.Tree["ФТИ"]["09.03.04 Программная инженерия"]["2"]);
            Assert.Equal(replace ? 1 : 3, result.Groups.Single(group => group.Code == "ПИ-б-о-252").Lessons.Count);
            Assert.Equal("Не менять", Assert.Single(result.Groups.Single(group => group.Code == "ДРУГАЯ-б-о-251").Lessons).Subject);
            var updatedCatalog = JsonSerializer.Deserialize<ReferenceCatalogSnapshot>(await File.ReadAllTextAsync(catalogPath), JsonOptions)!;
            var updatedTeacher = Assert.Single(updatedCatalog.Teachers);
            Assert.DoesNotContain(updatedTeacher.Schedule, entry => entry.GroupCode == "ПИ-б-о-252");
            Assert.Equal("Дисциплина", Assert.Single(updatedTeacher.Disciplines));
            string saved = await File.ReadAllTextAsync(manual);
            string savedCatalog = await File.ReadAllTextAsync(catalogPath);
            await writer.WriteAsync(incoming);
            Assert.Equal(saved, await File.ReadAllTextAsync(manual));
            Assert.Equal(savedCatalog, await File.ReadAllTextAsync(catalogPath));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static CfuGroupScheduleDocument Group(string code, params CfuLessonDocument[] lessons) =>
        new() { Code = code, Lessons = lessons };
    private static CfuLessonDocument Lesson(int pair, string subject) =>
        new() { Day = 1, PairNumber = pair, Parity = "четная", Subject = subject };
    private static TeacherScheduleEntry Entry(string group, string subject) =>
        new(group, 0, 1, 1, "четная", null, subject, "ЛК", "301", null, null, null);
}
