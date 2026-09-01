using System.Net;
using System.Net.Http.Json;
using UniversitySchedule.Contracts.Identity;
using UniversitySchedule.Mobile.Core.Identity;
using UniversitySchedule.Mobile.Core.Storage;
using UniversitySchedule.Mobile.Core.Sync;

namespace UniversitySchedule.Mobile.Core.Tests.Sync;

public sealed class PersonalDataSynchronizerTests
{
    [Fact]
    public async Task SynchronizeAsync_RetainsConflictAndContinuesWithAnotherEntity()
    {
        DateTimeOffset now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        var dataStore = new InMemoryLocalDataStore();
        var queue = new PersonalDataSyncQueue(dataStore, new FixedTimeProvider(now));
        await queue.EnqueueNoteDeleteAsync(Guid.NewGuid(), now);
        await queue.EnqueueAssignmentDeleteAsync(Guid.NewGuid(), now);
        var handler = new SequencedHandler(
            now,
            new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent("{\"revision\":4}"),
            },
            new HttpResponseMessage(HttpStatusCode.OK));
        UniversityScheduleApiClient client = CreateClient(handler, now);
        var synchronizer = new PersonalDataSynchronizer(queue, client);

        PersonalDataSyncRunResult result = await synchronizer.SynchronizeAsync();

        Assert.Equal(1, result.SynchronizedCount);
        Assert.Equal(0, result.PendingCount);
        Assert.Equal(1, result.ConflictCount);
        Assert.Equal(0, result.FailedCount);
        Assert.True(result.CanDownloadSnapshot);
        PersonalDataSyncOperation conflict = Assert.Single(await queue.GetPendingAsync());
        Assert.Equal(PersonalDataSyncOperationState.Conflict, conflict.State);
        Assert.Equal("{\"revision\":4}", conflict.ConflictServerStateJson);
    }

    [Fact]
    public async Task SynchronizeAsync_ExhaustedTransientRetriesRemainPending()
    {
        DateTimeOffset now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        var dataStore = new InMemoryLocalDataStore();
        var queue = new PersonalDataSyncQueue(dataStore, new FixedTimeProvider(now));
        await queue.EnqueueNoteDeleteAsync(Guid.NewGuid(), now);
        var handler = new SequencedHandler(
            now,
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        UniversityScheduleApiClient client = CreateClient(handler, now);
        var synchronizer = new PersonalDataSynchronizer(queue, client);

        PersonalDataSyncRunResult result = await synchronizer.SynchronizeAsync();

        Assert.Equal(0, result.SynchronizedCount);
        Assert.Equal(1, result.PendingCount);
        Assert.Equal(0, result.ConflictCount);
        Assert.False(result.CanDownloadSnapshot);
        PersonalDataSyncOperation pending = Assert.Single(await queue.GetPendingAsync());
        Assert.Equal(1, pending.AttemptCount);
        Assert.Equal("transient_retry_exhausted", pending.LastErrorCode);
        Assert.Equal(4, handler.RequestCount);
    }

    private static UniversityScheduleApiClient CreateClient(
        SequencedHandler handler,
        DateTimeOffset now)
    {
        var secureStore = new InMemorySecureValueStore();
        var timeProvider = new FixedTimeProvider(now);
        var options = new UniversityScheduleApiOptions(
            new Uri("https://api.example.test/"),
            "android",
            "1.0.0");
        return new UniversityScheduleApiClient(
            new HttpClient(handler) { BaseAddress = options.BaseAddress },
            options,
            new InstallationIdentityService(secureStore, timeProvider),
            secureStore,
            timeProvider);
    }

    private sealed class SequencedHandler(
        DateTimeOffset now,
        params HttpResponseMessage[] mutationResponses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _mutationResponses = new(mutationResponses);

        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (request.RequestUri!.AbsolutePath.EndsWith("/installations/register", StringComparison.Ordinal))
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

            return _mutationResponses.Dequeue();
        }
    }

    private sealed class InMemoryLocalDataStore : ILocalDataStore
    {
        private readonly Dictionary<string, LocalDocument> _documents = [];

        public Task<LocalDocument?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_documents.GetValueOrDefault(key));

        public Task SaveAsync(LocalDocument document, CancellationToken cancellationToken = default)
        {
            _documents[document.Key] = document;
            return Task.CompletedTask;
        }
    }

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
