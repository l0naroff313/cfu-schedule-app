using Microsoft.Extensions.Logging.Abstractions;
using UniversitySchedule.Contracts.Catalog;
using UniversitySchedule.ScheduleImporter;
using UniversitySchedule.ScheduleImporter.Sources;

namespace UniversitySchedule.Importer.Tests.Sources;

public sealed class VuzopediaSourceClientTests
{
    [Fact]
    public async Task ParseTeacherProfile_ReadsPositionDisciplinesAndSpecialties()
    {
        const string html = """
            <html><body>
              <h1>Атик Аниса Ахмедовна</h1>
              <div class="itemVuz">
                <div><b>Должность:</b> доцент</div>
                <div><b>Преподаваемые дисциплины:</b><ul><li>Философия</li><li>Социология</li></ul></div>
                <div class="blockNewItem" data-entity="napr">
                  <a class="newItemSpPrTitle" href="/vuz/5346/napr/153">Филология</a>
                  <div class="osnBlockInfoSm">45.03.01 Бакалавриат | Очная ЕГЭ: русский</div>
                </div>
              </div>
            </body></html>
            """;
        ImportOptions options = new("out.json", "reports", "cache", false, false, TimeSpan.FromSeconds(5));
        var cachedSource = new CachedHttpSource(new HttpClient(), options, NullLogger<CachedHttpSource>.Instance);
        var client = new VuzopediaSourceClient(cachedSource, options, NullLogger<VuzopediaSourceClient>.Instance);

        VuzopediaTeacherProfile profile = await client.ParseTeacherProfileAsync(
            new VuzopediaTeacherListItem("Атик Аниса Ахмедовна", "https://example.test/atik"),
            html,
            CancellationToken.None);

        Assert.Equal("доцент", profile.Position);
        Assert.Equal(["Философия", "Социология"], profile.Disciplines);
        VuzopediaProgram specialty = Assert.Single(profile.Specialties);
        Assert.Equal("45.03.01", specialty.Code);
        Assert.Equal(EducationLevel.Bachelor, specialty.Level);
        Assert.Equal([StudyForm.FullTime], specialty.StudyForms);
    }
}
