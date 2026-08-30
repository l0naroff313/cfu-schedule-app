using System.Net;
using System.Net.Http.Json;
using UniversitySchedule.Contracts.Identity;
using UniversitySchedule.Mobile.Core.Identity;
using UniversitySchedule.Mobile.Core.Notes;
using UniversitySchedule.Mobile.Core.Sync;

namespace UniversitySchedule.Mobile.Core.Tests.Sync;

public sealed class UniversityScheduleApiClientTests
{
    [Fact]
    public async Task PushAsync_RegistersOnceAndReusesBearerToken()
    {
        DateTimeOffset now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        var handler = new RecordingHandler(now);
        var secureStore = new InMemorySecureValueStore();
        var clock = new FixedTimeProvider(now);
        var identityService = new InstallationIdentityService(secureStore, clock);
        var options = new UniversityScheduleApiOptions(
            new Uri("https://api.example.test/"),
            "android",
            "1.0.0");
        var httpClient = new HttpClient(handler) { BaseAddress = options.BaseAddress };
        var client = new UniversityScheduleApiClient(
            httpClient,
            options,
            identityService,
            secureStore,
            clock);
        var note = new PersonalNote(Guid.NewGuid(), null, "Текст", now, now);
        var upsert = new PersonalDataSyncOperation(
            Guid.NewGuid(),
            PersonalDataSyncEntityKind.Note,
            PersonalDataSyncMutationKind.Upsert,
            note.Id,
            now,
            Note: note);
        var delete = new PersonalDataSyncOperation(
            Guid.NewGuid(),
            PersonalDataSyncEntityKind.Note,
            PersonalDataSyncMutationKind.Delete,
            note.Id,
            now.AddMinutes(1));

        PersonalDataPushResult upsertResult = await client.PushAsync(upsert);
        PersonalDataPushResult deleteResult = await client.PushAsync(delete);

        Assert.Equal(PersonalDataPushOutcome.Succeeded, upsertResult.Outcome);
        Assert.Equal(PersonalDataPushOutcome.Succeeded, deleteResult.Outcome);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("api/v1/installations/register", handler.Requests[0].Path);
        Assert.Null(handler.Requests[0].Authorization);
        Assert.Equal("Bearer test-access-token", handler.Requests[1].Authorization);
        Assert.Equal("Bearer test-access-token", handler.Requests[2].Authorization);
        Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
        Assert.Equal(HttpMethod.Delete, handler.Requests[2].Method);
    }

    [Fact]
    public async Task PushAsync_HttpEndpointIsDisabledBeforeSecretCanBeSent()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var handler = new RecordingHandler(now);
        var secureStore = new InMemorySecureValueStore();
        var clock = new FixedTimeProvider(now);
        var client = new UniversityScheduleApiClient(
            new HttpClient(handler),
            new UniversityScheduleApiOptions(new Uri("http://api.example.test/"), "android", "1.0.0"),
            new InstallationIdentityService(secureStore, clock),
            secureStore,
            clock);
        var operation = new PersonalDataSyncOperation(
            Guid.NewGuid(),
            PersonalDataSyncEntityKind.Note,
            PersonalDataSyncMutationKind.Delete,
            Guid.NewGuid(),
            now);

        PersonalDataPushResult result = await client.PushAsync(operation);

        Assert.Equal(PersonalDataPushOutcome.NotConfigured, result.Outcome);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PushAsync_TransientResponsesRetrySameMutationUntilSuccess()
    {
        DateTimeOffset now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        var handler = new RecordingHandler(
            now,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.OK);
        UniversityScheduleApiClient client = CreateClient(handler, now);
        Guid mutationId = Guid.NewGuid();
        var operation = new PersonalDataSyncOperation(
            mutationId,
            PersonalDataSyncEntityKind.Note,
            PersonalDataSyncMutationKind.Delete,
            Guid.NewGuid(),
            now);

        PersonalDataPushResult result = await client.PushAsync(operation);

        Assert.Equal(PersonalDataPushOutcome.Succeeded, result.Outcome);
        Assert.Equal(3, result.RequestCount);
        string[] mutationRequests = handler.Requests
            .Where(request => request.Authorization is not null)
            .Select(request => request.Path)
            .ToArray();
        Assert.Equal(3, mutationRequests.Length);
        Assert.All(mutationRequests, path => Assert.Contains(mutationId.ToString("D"), path));
    }

    [Fact]
    public async Task PushAsync_ConflictIsReturnedWithoutDiscardingDecision()
    {
        DateTimeOffset now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        var handler = new RecordingHandler(now, HttpStatusCode.Conflict)
        {
            ConflictContent = "{\"revision\":3}",
        };
        UniversityScheduleApiClient client = CreateClient(handler, now);
        var operation = new PersonalDataSyncOperation(
            Guid.NewGuid(),
            PersonalDataSyncEntityKind.Assignment,
            PersonalDataSyncMutationKind.Delete,
            Guid.NewGuid(),
            now);

        PersonalDataPushResult result = await client.PushAsync(operation);

        Assert.Equal(PersonalDataPushOutcome.Conflict, result.Outcome);
        Assert.Equal(1, result.RequestCount);
        Assert.Equal("server_conflict", result.ErrorCode);
        Assert.Equal("{\"revision\":3}", result.ServerStateJson);
    }

    private static UniversityScheduleApiClient CreateClient(
        RecordingHandler handler,
        DateTimeOffset now)
    {
        var secureStore = new InMemorySecureValueStore();
        var clock = new FixedTimeProvider(now);
        var options = new UniversityScheduleApiOptions(
            new Uri("https://api.example.test/"),
            "android",
            "1.0.0");
        return new UniversityScheduleApiClient(
            new HttpClient(handler) { BaseAddress = options.BaseAddress },
            options,
            new InstallationIdentityService(secureStore, clock),
            secureStore,
            clock);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly DateTimeOffset _now;
        private readonly Queue<HttpStatusCode> _syncStatuses;

        public RecordingHandler(DateTimeOffset now, params HttpStatusCode[] syncStatuses)
        {
            _now = now;
            _syncStatuses = new Queue<HttpStatusCode>(syncStatuses);
        }

        public List<CapturedRequest> Requests { get; } = [];

        public string? ConflictContent { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!.PathAndQuery.TrimStart('/'),
                request.Headers.Authorization?.ToString()));

            if (request.RequestUri.AbsolutePath.EndsWith("/installations/register", StringComparison.Ordinal))
            {
                RegisterInstallationRequest? registration = await request.Content!
                    .ReadFromJsonAsync<RegisterInstallationRequest>(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new RegisterInstallationResponse(
                        registration!.InstallationId,
                        "test-access-token",
                        "Bearer",
                        _now.AddMinutes(15),
                        true)),
                };
            }

            HttpStatusCode status = _syncStatuses.TryDequeue(out HttpStatusCode configured)
                ? configured
                : HttpStatusCode.OK;
            var response = new HttpResponseMessage(status);
            if (status == HttpStatusCode.Conflict && ConflictContent is not null)
            {
                response.Content = new StringContent(ConflictContent);
            }

            if (status == HttpStatusCode.TooManyRequests)
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            }

            return response;
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, string Path, string? Authorization);

    private sealed class InMemorySecureValueStore : ISecureValueStore
    {
        private readonly Dictionary<string, string> _values = [];

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.GetValueOrDefault(key));

        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
