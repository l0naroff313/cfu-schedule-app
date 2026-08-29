using System.Net;
using System.Text;
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
}
