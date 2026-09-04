using System.Net;
using System.Text;
using UniversitySchedule.Mobile.Core.Cfu;
using UniversitySchedule.Mobile.Core.Profiles;
using UniversitySchedule.Mobile.Core.Scheduling;
using UniversitySchedule.Mobile.Core.Storage;

namespace UniversitySchedule.Mobile.Core.Tests.Scheduling;

public sealed class ScheduleSessionOfflineTests
{
    private const string IndexJson = """
        {
          "bells": [{"пара": 1, "начало": "08:00", "конец": "09:30"}],
          "weeks": {"ch": ["2026-09-07"], "nch": ["2026-09-14"]},
          "tree": {"ФТИ": {"09.03.04 Программная инженерия": {"2": ["ПИ-б-о-252"]}}}
        }
        """;

    private const string GroupJson = """
        {
          "код": "ПИ-б-о-252",
          "занятия": [{
            "группа": "ПИ-б-о-252",
            "подгруппа": 1,
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
    public async Task PrepareOffline_DownloadsAndVerifiesSelectedGroup()
    {
        var store = new MemoryStore();
        var repository = new CfuScheduleRepository(CreateSuccessfulClient(), store);
        var session = new ScheduleSession(new AcademicProfileStore(store), repository);
        await session.SetProfileAsync(CreateProfile());

        OfflineSchedulePreparationResult result = await session.PrepareOfflineAsync();

        Assert.True(result.DownloadedFromNetwork);
        Assert.True(result.Readiness.IsReady);
        Assert.Equal("ПИ-б-о-252", result.Readiness.GroupName);
        Assert.True(result.Readiness.LessonCount > 0);
        Assert.NotNull(result.Readiness.UpdatedAtUtc);
    }

    [Fact]
    public async Task PrepareOffline_WithNoNetwork_VerifiesExistingCopy()
    {
        var store = new MemoryStore();
        var initialSession = new ScheduleSession(
            new AcademicProfileStore(store),
            new CfuScheduleRepository(CreateSuccessfulClient(), store));
        await initialSession.SetProfileAsync(CreateProfile());

        var offlineSession = new ScheduleSession(
            new AcademicProfileStore(store),
            new CfuScheduleRepository(
                new HttpClient(new ResponseHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))
                {
                    BaseAddress = new Uri(CfuScheduleRepository.BaseAddress),
                },
                store));

        OfflineSchedulePreparationResult result = await offlineSession.PrepareOfflineAsync();

        Assert.False(result.DownloadedFromNetwork);
        Assert.True(result.Readiness.IsReady);
        Assert.True(result.Readiness.LessonCount > 0);
    }

    private static AcademicProfile CreateProfile() => new(
        Guid.NewGuid(),
        "Физико-технический институт",
        Guid.NewGuid(),
        "09.03.04 Программная инженерия",
        Guid.NewGuid(),
        "ПИ-б-о-252",
        2,
        Guid.NewGuid(),
        "1 подгруппа");

    private static HttpClient CreateSuccessfulClient() => new(
        new ResponseHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                request.RequestUri!.PathAndQuery.EndsWith("index", StringComparison.Ordinal)
                    ? IndexJson
                    : GroupJson,
                Encoding.UTF8,
                "application/json"),
        }))
    {
        BaseAddress = new Uri(CfuScheduleRepository.BaseAddress),
    };

    private sealed class ResponseHandler(
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

    private sealed class MemoryStore : ILocalDataStore
    {
        private readonly Dictionary<string, LocalDocument> _documents = [];

        public Task<LocalDocument?> GetAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _documents.TryGetValue(key, out LocalDocument? document);
            return Task.FromResult(document);
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
