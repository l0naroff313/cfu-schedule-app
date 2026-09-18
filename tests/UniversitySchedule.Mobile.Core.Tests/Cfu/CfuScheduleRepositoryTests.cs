using System.Net;
using System.Text;
using System.Text.Json;
using UniversitySchedule.Mobile.Core.Cfu;
using UniversitySchedule.Mobile.Core.Storage;

namespace UniversitySchedule.Mobile.Core.Tests.Cfu;

public sealed class CfuScheduleRepositoryTests
{
    private const string IndexJson = """
        {
          "bells": [{"пара": 1, "начало": "08:00", "конец": "09:30"}],
          "weeks": {"ch": ["2026-09-07"], "nch": ["2026-09-14"]},
          "tree": {"ФТИ": {"01.03.01 Математика": {"2": ["МАТ-б-о-251"]}}}
        }
        """;

    private const string GroupJson = """
        {
          "код": "МАТ-б-о-251",
          "занятия": [{
            "группа": "МАТ-б-о-251",
            "подгруппа": 0,
            "день": 1,
            "пара": 1,
            "чётность": "чёт",
            "предмет": "Алгоритмы",
            "вид": "ЛК",
            "преподаватели": ["Иванова Н. П."],
            "аудитория": "305",
            "корпус": "корпус А"
          }],
          "fak": []
        }
        """;

    [Fact]
    public async Task NetworkFailure_ReturnsLastSuccessfulCache()
    {
        var store = new InMemoryLocalDataStore();
        DateTimeOffset cachedAt = new(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);
        await store.SaveAsync(new LocalDocument("cfu:index", IndexJson, cachedAt));
        await store.SaveAsync(new LocalDocument("cfu:group:мат-б-о-251", GroupJson, cachedAt));
        var repository = new CfuScheduleRepository(
            CreateClient(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
            store);

        CfuScheduleLoadResult result = await repository.LoadGroupScheduleAsync("МАТ-б-о-251");

        Assert.True(result.IsFromCache);
        Assert.Equal(cachedAt, result.UpdatedAtUtc);
        Assert.Equal("Алгоритмы", Assert.Single(result.Snapshot.Lessons).Subject);
    }

    [Fact]
    public async Task SuccessfulResponse_IsValidatedAndSaved()
    {
        var store = new InMemoryLocalDataStore();
        var repository = new CfuScheduleRepository(
            CreateClient(request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    request.RequestUri!.PathAndQuery.EndsWith("index", StringComparison.Ordinal)
                        ? IndexJson
                        : GroupJson,
                    Encoding.UTF8,
                    "application/json"),
            }),
            store);

        CfuScheduleLoadResult result = await repository.LoadGroupScheduleAsync("МАТ-б-о-251");

        Assert.False(result.IsFromCache);
        Assert.NotNull(await store.GetAsync("cfu:index"));
        Assert.NotNull(await store.GetAsync("cfu:group:мат-б-о-251"));
    }

    [Fact]
    public async Task ExcelOverride_TakesPriorityForMatchingGroup()
    {
        var store = new InMemoryLocalDataStore();
        var repository = new CfuScheduleRepository(
            CreateClient(request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    request.RequestUri!.PathAndQuery.EndsWith("index", StringComparison.Ordinal)
                        ? IndexJson
                        : GroupJson,
                    Encoding.UTF8,
                    "application/json"),
            }),
            store,
            new StubManualScheduleOverrideProvider(new ManualScheduleOverrideDocument
            {
                ImportedAtUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
                Bells = [new CfuBellDocument { PairNumber = 1, StartsAt = "08:00", EndsAt = "09:30" }],
                Weeks = new CfuWeeksDocument
                {
                    EvenWeekMondays = ["2026-09-07"],
                    OddWeekMondays = ["2026-09-14"],
                },
                Groups = [new CfuGroupScheduleDocument
                {
                    Code = "МАТ-б-о-251",
                    Lessons = [new CfuLessonDocument
                    {
                        GroupCode = "МАТ-б-о-251",
                        Day = 1,
                        PairNumber = 1,
                        Parity = "четная",
                        Subject = "Excel версия",
                        LessonType = "ПЗ",
                    }],
                }],
            }));

        CfuScheduleLoadResult result = await repository.LoadGroupScheduleAsync("МАТ-б-о-251");

        Assert.True(result.IsFromCache);
        Assert.Equal("Excel версия", Assert.Single(result.Snapshot.Lessons).Subject);
    }

    [Fact]
    public void ExcelOverride_TeacherSearchUsesExactSurname()
    {
        var document = new ManualScheduleOverrideDocument
        {
            Groups = [new CfuGroupScheduleDocument
            {
                Code = "МАТ-б-о-251",
                Lessons = [new CfuLessonDocument
                {
                    GroupCode = "МАТ-б-о-251",
                    Teachers = ["Зуев С.А."],
                }],
            }],
        };

        Assert.Single(document.FindTeacherLessons("Зуев"));
        Assert.Empty(document.FindTeacherLessons("Зу"));
    }

    [Theory]
    [InlineData(true, 2019, "Алгоритмы")]
    [InlineData(false, 2019, "Официальный снимок")]
    [InlineData(false, 2021, "Новый локальный кэш")]
    public async Task OfficialMode_ApiWins_AndFallbackNeverRollsBackNewerCache(bool online, int cacheYear, string expected)
    {
        var store = new InMemoryLocalDataStore();
        var cachedAt = new DateTimeOffset(cacheYear, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await store.SaveAsync(new LocalDocument("cfu:index", IndexJson, cachedAt));
        await store.SaveAsync(new LocalDocument("cfu:group:мат-б-о-251",
            GroupJson.Replace("Алгоритмы", "Новый локальный кэш"), cachedAt));
        var repository = new CfuScheduleRepository(CreateClient(request => online
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                request.RequestUri!.AbsolutePath.EndsWith("index") ? IndexJson : GroupJson, Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)), store, OfficialFallback());
        var result = await repository.LoadGroupScheduleAsync("МАТ-б-о-251");
        Assert.Equal(!online, result.IsFromCache);
        Assert.Equal(expected, Assert.Single(result.Snapshot.Lessons).Subject);
        var saved = await repository.LoadCachedGroupScheduleAsync("МАТ-б-о-251");
        Assert.Equal(expected, Assert.Single(saved!.Snapshot.Lessons).Subject);
    }

    [Fact]
    public async Task OfficialEmptyResponse_CancelsOldLessons_WithoutRestoringFallback()
    {
        var repository = new CfuScheduleRepository(CreateClient(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("index") ? IndexJson
                : """{"код":"МАТ-б-о-251","занятия":[],"fak":[]} """, Encoding.UTF8, "application/json"),
        }), new InMemoryLocalDataStore(), OfficialFallback());
        var result = await repository.LoadGroupScheduleAsync("МАТ-б-о-251");
        Assert.False(result.IsFromCache);
        Assert.Empty(result.Snapshot.Lessons);
        Assert.Empty((await repository.LoadCachedGroupScheduleAsync("МАТ-б-о-251"))!.Snapshot.Lessons);
    }

    [Fact]
    public async Task OfficialTeacherSearch_UsesLiveEmptyResponse_NotBundledLessons()
    {
        var repository = new CfuScheduleRepository(CreateClient(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("index") ? IndexJson : "[]",
                Encoding.UTF8, "application/json"),
        }), new InMemoryLocalDataStore(), OfficialFallback());
        var result = await repository.SearchTeachersAsync("Иванова");
        Assert.False(result.IsFromCache);
        Assert.Empty(result.Search.Lessons);
    }

    [Theory]
    [InlineData("{\"код\":\"МАТ-б-о-251\"}")]
    [InlineData("{\"код\":\"ДРУГАЯ\",\"занятия\":[]}")]
    [InlineData("not json")]
    public async Task MalformedOfficialResponse_DoesNotEraseWorkingFallback(string broken)
    {
        var repository = new CfuScheduleRepository(CreateClient(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("index") ? IndexJson : broken,
                Encoding.UTF8, "application/json"),
        }), new InMemoryLocalDataStore(), OfficialFallback());
        var result = await repository.LoadGroupScheduleAsync("МАТ-б-о-251");
        Assert.True(result.IsFromCache);
        Assert.Equal("Официальный снимок", Assert.Single(result.Snapshot.Lessons).Subject);
    }

    [Theory]
    [InlineData("ПИ-б-о-252")]
    [InlineData("МАТ-б-о-261")]
    public async Task TruncatedCalendar_UsesVerifiedCalendarAndFreshGroup_ThenRecovers(string code)
    {
        string good = IndexJson.Replace("МАТ-б-о-251", code);
        string broken = good.Replace("\"ch\": [\"2026-09-07\"], \"nch\": [\"2026-09-14\"]",
            "\"ch\": [\"2026-10-05\"], \"nch\": []");
        string group = GroupJson.Replace("МАТ-б-о-251", code).Replace("\"день\": 1", "\"день\": 5")
            .Replace("\"чётность\": \"чёт\"", "\"чётность\": \"нечёт\"");
        var store = new InMemoryLocalDataStore();
        var calendar = JsonSerializer.Deserialize<CfuScheduleIndexDocument>(good)!;
        // Only a calendar is bundled: new groups must work without an old bundled timetable.
        var fallback = new StubManualScheduleOverrideProvider(new ManualScheduleOverrideDocument
        {
            PreferOfficialApi = true, ImportedAtUtc = DateTimeOffset.Parse("2026-09-11T10:00:00Z"),
            Bells = calendar.Bells, Weeks = calendar.Weeks, Tree = calendar.Tree,
        });
        bool healthy = false;
        var repository = new CfuScheduleRepository(CreateClient(request => new(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("index")
                ? healthy ? good : broken : group, Encoding.UTF8, "application/json"),
        }), store, fallback);

        var result = await repository.LoadGroupScheduleAsync(code, 1);
        Assert.Equal(new DateOnly(2026, 9, 18), Assert.Single(result.Snapshot.Lessons).Date);
        Assert.True(result.IsFromCache);
        Assert.NotNull(result.Warning);
        string savedCalendar = (await store.GetAsync("cfu:index"))!.Content;
        Assert.True(CfuCalendarIntegrity.IsUsable(JsonSerializer.Deserialize<CfuScheduleIndexDocument>(savedCalendar)!));
        Assert.NotNull((await repository.LoadCachedGroupScheduleAsync(code, 1))!.Warning);

        // Repeated malformed refresh must not erase working weeks or their capture date.
        var again = await repository.LoadGroupScheduleAsync(code, 1);
        Assert.Equal(result.UpdatedAtUtc, again.UpdatedAtUtc);
        Assert.Single(again.Snapshot.Lessons);

        healthy = true;
        var restored = await repository.LoadGroupScheduleAsync(code, 1);
        Assert.False(restored.IsFromCache);
        Assert.Null(restored.Warning);
        Assert.Null((await repository.LoadCachedGroupScheduleAsync(code, 1))!.Warning);
    }

    [Fact]
    public async Task LegacyPoisonedPair_IsRepairedWithoutNetwork()
    {
        var store = new InMemoryLocalDataStore();
        string broken = IndexJson.Replace("\"nch\": [\"2026-09-14\"]", "\"nch\": []");
        string paired = "{\"Index\":" + broken + ",\"Schedule\":" + GroupJson + "}";
        await store.SaveAsync(new("cfu:cached-group:мат-б-о-251", paired, DateTimeOffset.UtcNow));
        var repository = new CfuScheduleRepository(CreateClient(_ => throw new Exception("No network on cached startup")), store, OfficialFallback());
        var result = await repository.LoadCachedGroupScheduleAsync("МАТ-б-о-251");
        Assert.NotNull(result);
        Assert.Single(result.Snapshot.Lessons);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public async Task UnrecoverableCalendar_ThrowsInsteadOfSavingEmptySchedule()
    {
        var store = new InMemoryLocalDataStore();
        string broken = IndexJson.Replace("\"nch\": [\"2026-09-14\"]", "\"nch\": []");
        var repository = new CfuScheduleRepository(CreateClient(_ => new(HttpStatusCode.OK)
        { Content = new StringContent(broken, Encoding.UTF8, "application/json") }), store);
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.LoadGroupScheduleAsync("МАТ-б-о-261"));
        Assert.Null(await store.GetAsync("cfu:index"));
        Assert.Null(await store.GetAsync("cfu:cached-group:мат-б-о-261"));
    }

    [Theory]
    [InlineData("2026-09-07", "2026-09-07")]
    [InlineData("2026-09-08", "2026-09-14")]
    [InlineData("broken", "2026-09-14")]
    [InlineData("2026-09-07", "2026-09-28")]
    public void InvalidCalendar_IsRejected(string even, string odd)
    {
        Assert.False(CfuCalendarIntegrity.IsUsable(new()
        { Weeks = new() { EvenWeekMondays = [even], OddWeekMondays = [odd] } }));
    }

    private static StubManualScheduleOverrideProvider OfficialFallback()
    {
        var index = JsonSerializer.Deserialize<CfuScheduleIndexDocument>(IndexJson)!;
        return new(new ManualScheduleOverrideDocument
        {
            PreferOfficialApi = true,
            ImportedAtUtc = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Bells = index.Bells, Weeks = index.Weeks, Tree = index.Tree,
            Groups = [JsonSerializer.Deserialize<CfuGroupScheduleDocument>(GroupJson.Replace("Алгоритмы", "Официальный снимок"))!],
        });
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> response)
    {
        return new HttpClient(new StubHttpMessageHandler(response))
        {
            BaseAddress = new Uri(CfuScheduleRepository.BaseAddress),
        };
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(response(request));
        }
    }

    private sealed class InMemoryLocalDataStore : ILocalDataStore
    {
        private readonly Dictionary<string, LocalDocument> _documents = [];

        public Task<LocalDocument?> GetAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _documents.TryGetValue(key, out LocalDocument? value);
            return Task.FromResult(value);
        }

        public Task SaveAsync(
            LocalDocument document,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _documents[document.Key] = document;
            return Task.CompletedTask;
        }
    }

    private sealed class StubManualScheduleOverrideProvider(ManualScheduleOverrideDocument document)
        : IManualScheduleOverrideProvider
    {
        public Task<ManualScheduleOverrideDocument?> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<ManualScheduleOverrideDocument?>(document);
        }
    }
}
