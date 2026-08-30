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
    public async Task TryPushAsync_RegistersOnceAndReusesBearerToken()
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

        Assert.True(await client.TryPushAsync(upsert));
        Assert.True(await client.TryPushAsync(delete));

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("api/v1/installations/register", handler.Requests[0].Path);
        Assert.Null(handler.Requests[0].Authorization);
        Assert.Equal("Bearer test-access-token", handler.Requests[1].Authorization);
        Assert.Equal("Bearer test-access-token", handler.Requests[2].Authorization);
        Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
        Assert.Equal(HttpMethod.Delete, handler.Requests[2].Method);
    }

    [Fact]
    public async Task TryPushAsync_HttpEndpointIsDisabledBeforeSecretCanBeSent()
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

        Assert.False(await client.TryPushAsync(operation));
        Assert.Empty(handler.Requests);
    }

    private sealed class RecordingHandler(DateTimeOffset now) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

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
                        now.AddMinutes(15),
                        true)),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
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
