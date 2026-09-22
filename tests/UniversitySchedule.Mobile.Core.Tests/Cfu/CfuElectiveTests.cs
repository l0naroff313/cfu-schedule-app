using System.Net;
using System.Text.Json;
using UniversitySchedule.Mobile.Core.Cfu;
using UniversitySchedule.Mobile.Core.Profiles;
using UniversitySchedule.Mobile.Core.Storage;

namespace UniversitySchedule.Mobile.Core.Tests.Cfu;

public sealed class CfuElectiveTests
{
    private static AcademicProfile Profile(int course = 2, string? elective = "ЦК-700", int? module = 1) =>
        new(Guid.NewGuid(), "ФТИ", Guid.NewGuid(), "ПИ", Guid.NewGuid(), "ПИ-б-о-252", course, null, null, elective, module);

    private static readonly CfuBellDocument[] Bells = [new() { PairNumber = 1, StartsAt = "08:00", EndsAt = "09:30" }];
    private static readonly CfuElectiveDocument Document = new() {
        Bells = Bells, Weeks = new() { Even = ["2026-09-21"], Odd = ["2026-09-28"] },
        Groups = [new() { Code = "ЦК-700", Course = 2, Discipline = "3Д" }, new() { Code = "ДРПК-101", Course = 4, Discipline = "Право" }],
        Lessons = [Row("ЦК-700", "Модуль один", 2, 1), Row("ЦК-700", "Модуль два", 2, 2),
            Row("ЦК-700", "Практика", 6, 1), Row("ДРПК-101", "Право", 4, null, "нечёт")]
    };

    private static CfuLessonDocument Row(string group, string subject, int day, int? module, string parity = "") =>
        new() { GroupCode = group, Subject = subject, Day = day, PairNumber = 1, Module = module, Parity = parity, Classroom = "305В" };

    [Theory]
    [InlineData(1, false)] [InlineData(2, true)] [InlineData(3, false)] [InlineData(4, true)] [InlineData(5, false)]
    public void Eligibility(int course, bool expected) => Assert.Equal(expected, CfuElectiveDocument.IsEligible(course));

    [Fact]
    public void ModuleIsExplicit_NotTreatedAsParity()
    {
        var result = Document.Map(Profile());
        Assert.Equal(4, result.Lessons.Count);
        Assert.Contains(result.Lessons, l => l.Date == new DateOnly(2026, 9, 22));
        Assert.Contains(result.Lessons, l => l.Date == new DateOnly(2026, 9, 26));
        Assert.DoesNotContain(result.Lessons, l => l.Subject == "Модуль два");
        Assert.All(result.Lessons, l => Assert.Equal("305В", l.Classroom));
        Assert.Throws<InvalidDataException>(() => Document.Map(Profile(module: null)));
        Assert.Throws<InvalidDataException>(() => Document.Map(Profile(4)));
    }

    [Fact]
    public void DrpkPreservesActualWeekdayAndParity()
    {
        var lesson = Assert.Single(Document.Map(Profile(4, "ДРПК-101", null)).Lessons);
        Assert.Equal(new DateOnly(2026, 10, 1), lesson.Date);
    }

    [Fact]
    public async Task SelectionFiltersCoursesAndClearsStaleGroupAndModule()
    {
        var selection = new ElectiveSelection(Repository(new Store()));
        selection.SetCourse(2);
        await selection.LoadAsync();
        Assert.Equal("3Д", Assert.Single(selection.Disciplines));
        selection.Discipline = "3Д";
        Assert.False(selection.CanSave);
        selection.Group = Assert.Single(selection.Groups);
        Assert.False(selection.CanSave);
        selection.Module = 1;
        Assert.True(selection.CanSave);
        selection.SetCourse(4);
        Assert.Null(selection.Group);
        Assert.Null(selection.Module);
        Assert.Equal("Право", Assert.Single(selection.Disciplines));
        selection.SetCourse(3);
        Assert.False(selection.IsEligible);
        Assert.Empty(selection.Disciplines);
    }

    [Fact]
    public async Task CombinedScheduleSurvivesOffline_AndOnlyReplacesElectivePlaceholders()
    {
        var store = new Store();
        var profile = Profile();
        var online = Repository(store);
        var result = (await online.LoadProfileScheduleAsync(profile))!;
        Assert.DoesNotContain(result.Snapshot.Lessons, l => l.Subject == "Элективные дисциплины");
        Assert.Contains(result.Snapshot.Lessons, l => l.Subject == "Математика");
        Assert.Contains(result.Snapshot.Lessons, l => l.Subject == "Модуль один");
        var offline = Repository(store, offline: true);
        var cached = (await offline.LoadProfileScheduleAsync(profile, true))!;
        Assert.Equal(result.Snapshot.Lessons.Select(l => l.Id), cached.Snapshot.Lessons.Select(l => l.Id));
        var refresh = (await offline.LoadProfileScheduleAsync(profile))!;
        Assert.Equal(cached.Snapshot.Lessons.Count, refresh.Snapshot.Lessons.Count);
        var noElective = (await offline.LoadProfileScheduleAsync(profile with { ElectiveGroupCode = null }, true))!;
        Assert.DoesNotContain(noElective.Snapshot.Lessons, l => l.Subject == "Модуль один");
        var fourth = (await offline.LoadProfileScheduleAsync(profile with { CourseNumber = 1 }, true))!;
        Assert.DoesNotContain(fourth.Snapshot.Lessons, l => l.Subject == "Модуль один");
    }

    [Fact]
    public async Task ProfileRoundtripPreservesElective_AndLegacyProfileStillLoads()
    {
        var store = new Store();
        var profiles = new AcademicProfileStore(store);
        var profile = Profile();
        await profiles.SaveAsync(profile);
        Assert.Equal(profile, await profiles.GetAsync());
        var json = JsonSerializer.SerializeToNode(profile)!.AsObject();
        json.Remove("ElectiveGroupCode"); json.Remove("ElectiveModule");
        await store.SaveAsync(new("profile:academic", json.ToJsonString(), DateTimeOffset.UtcNow));
        Assert.Null((await profiles.GetAsync())!.ElectiveGroupCode);
    }

    private static CfuScheduleRepository Repository(Store store, bool offline = false)
    {
        var index = new CfuScheduleIndexDocument { Bells = Bells, Weeks = new() {
            EvenWeekMondays = Document.Weeks.Even, OddWeekMondays = Document.Weeks.Odd },
            Tree = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>> {
                ["ФТИ"] = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> {
                    ["ПИ"] = new Dictionary<string, IReadOnlyList<string>> { ["2"] = ["ПИ-б-о-252"] } } } };
        var group = new CfuGroupScheduleDocument { Code = "ПИ-б-о-252", Lessons = [Row("ПИ-б-о-252", "Элективные дисциплины", 2, null, "обе"), Row("ПИ-б-о-252", "Математика", 2, null, "обе")] };
        return new(new HttpClient(new Handler(request => {
            if (offline) throw new HttpRequestException("offline");
            string json = request.RequestUri!.AbsolutePath.EndsWith("elektiv") ? JsonSerializer.Serialize(Document)
                : request.RequestUri.AbsolutePath.EndsWith("index") ? JsonSerializer.Serialize(index) : JsonSerializer.Serialize(group);
            return new(HttpStatusCode.OK) { Content = new StringContent(json) };
        })) { BaseAddress = new(CfuScheduleRepository.BaseAddress) }, store);
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> action) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(action(request)); }
    private sealed class Store : ILocalDataStore
    {
        private readonly Dictionary<string, LocalDocument> _docs = [];
        public Task<LocalDocument?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(_docs.GetValueOrDefault(key));
        public Task SaveAsync(LocalDocument document, CancellationToken cancellationToken = default) { _docs[document.Key] = document; return Task.CompletedTask; }
    }
}
