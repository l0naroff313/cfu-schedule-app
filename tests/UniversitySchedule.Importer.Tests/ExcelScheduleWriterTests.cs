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

    [Fact]
    public async Task OfficialSnapshot_ReplacesAllSchedules_PreservesProfiles_RejectsIncompleteInput()
    {
        string root = Directory.CreateTempSubdirectory("cfu-official-test-").FullName;
        try
        {
            string manual = Path.Combine(root, "manual.json");
            string catalogPath = Path.Combine(root, "catalog.json");
            var options = new ImportOptions(catalogPath, root, root, true, false, TimeSpan.FromSeconds(5),
                ManualScheduleOutputPath: manual, CatalogInputPath: catalogPath, OfficialSchedule: true);
            var teacher = new TeacherReference(Guid.NewGuid(), "иванов|и|и", "Иванов И.И.", "Иванов И.И.", "Иванов",
                "доцент", ["Дисциплина"], [], [Entry("УДАЛЕНА-б-о-251", "Устаревшая пара")],
                TeacherScheduleMatchStatus.Exact, null);
            var catalog = new ReferenceCatalogSnapshot(1, DateTimeOffset.UtcNow,
                new("", "", "", ""), new([], [], []), [], [teacher], [],
                new(0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 0, true));
            await File.WriteAllTextAsync(catalogPath, JsonSerializer.Serialize(catalog, JsonOptions));
            await File.WriteAllTextAsync(manual, "previous");
            var index = new CfuScheduleIndexDocument
            {
                Bells = [new() { PairNumber = 1, StartsAt = "08:00", EndsAt = "09:30" }],
                Tree = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>>
                { ["ФТИ"] = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>
                    { ["Программная инженерия"] = new Dictionary<string, IReadOnlyList<string>> { ["2"] = ["ПИ-б-о-252"] } } },
            };
            var writer = new ExcelScheduleWriter(options);
            await Assert.ThrowsAsync<InvalidDataException>(() => writer.WriteOfficialAsync(index, []));
            Assert.Equal("previous", await File.ReadAllTextAsync(manual));
            await writer.WriteOfficialAsync(index, [Group("ПИ-б-о-252", Lesson(1, "Официальная пара"))]);
            var snapshot = JsonSerializer.Deserialize<ManualScheduleOverrideDocument>(await File.ReadAllTextAsync(manual), JsonOptions)!;
            Assert.True(snapshot.PreferOfficialApi);
            Assert.Single(snapshot.Groups);
            Assert.Equal("Официальная пара", snapshot.Groups[0].Lessons[0].Subject);
            var result = JsonSerializer.Deserialize<ReferenceCatalogSnapshot>(await File.ReadAllTextAsync(catalogPath), JsonOptions)!;
            var retained = Assert.Single(result.Teachers);
            Assert.Equal(teacher.Id, retained.Id);
            Assert.Equal(teacher.Position, retained.Position);
            Assert.Equal(teacher.Disciplines, retained.Disciplines);
            Assert.Empty(retained.Schedule);

            // A later explicitly requested Excel import must not turn ALL official groups into overrides.
            await writer.WriteAsync(new ManualScheduleOverrideDocument
                { Groups = [Group("ДРУГАЯ-б-о-251", Lesson(1, "Excel"))] });
            var excel = JsonSerializer.Deserialize<ManualScheduleOverrideDocument>(await File.ReadAllTextAsync(manual), JsonOptions)!;
            Assert.False(excel.PreferOfficialApi);
            Assert.Equal("ДРУГАЯ-б-о-251", Assert.Single(excel.Groups).Code);
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
