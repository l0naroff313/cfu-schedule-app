using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using UniversitySchedule.Contracts.Identity;
using UniversitySchedule.Contracts.PersonalData;

namespace UniversitySchedule.Api.IntegrationTests;

public sealed class PersonalDataSyncEndpointsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public PersonalDataSyncEndpointsTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task NotesEndpoints_RequireInstallationToken()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/v1/sync/notes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Note_CreateUpdateDelete_SynchronizesTombstone()
    {
        using HttpClient client = await CreateAuthenticatedClientAsync();
        Guid noteId = Guid.NewGuid();
        DateTimeOffset createdAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var create = new SyncNoteRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Первый текст",
            "Лекция",
            "Архитектура ПО",
            false,
            createdAt,
            createdAt);

        SyncedNoteResponse created = await PutAsync<SyncedNoteResponse>(
            client,
            $"/api/v1/sync/notes/{noteId:D}",
            create);
        SyncedNoteResponse updated = await PutAsync<SyncedNoteResponse>(
            client,
            $"/api/v1/sync/notes/{noteId:D}",
            create with
            {
                MutationId = Guid.NewGuid(),
                Text = "Исправленный текст",
                IsPinned = true,
                UpdatedAtUtc = createdAt.AddMinutes(1),
            });
        DateTimeOffset deletedAt = createdAt.AddMinutes(2);
        using HttpResponseMessage deleteResponse = await client.DeleteAsync(
            $"/api/v1/sync/notes/{noteId:D}?mutationId={Guid.NewGuid():D}&deletedAtUtc={Uri.EscapeDataString(deletedAt.ToString("O"))}");
        SyncedNoteResponse? deleted = await deleteResponse.Content.ReadFromJsonAsync<SyncedNoteResponse>();

        Assert.True(created.WasApplied);
        Assert.Equal(1, created.Revision);
        Assert.True(updated.WasApplied);
        Assert.Equal(2, updated.Revision);
        Assert.Equal("Исправленный текст", updated.Text);
        Assert.True(updated.IsPinned);
        deleteResponse.EnsureSuccessStatusCode();
        Assert.NotNull(deleted);
        Assert.Equal(3, deleted.Revision);
        Assert.Equal(deletedAt, deleted.DeletedAtUtc);

        IReadOnlyList<SyncedNoteResponse>? active = await client.GetFromJsonAsync<IReadOnlyList<SyncedNoteResponse>>(
            "/api/v1/sync/notes");
        IReadOnlyList<SyncedNoteResponse>? withDeleted = await client.GetFromJsonAsync<IReadOnlyList<SyncedNoteResponse>>(
            "/api/v1/sync/notes?includeDeleted=true");
        Assert.DoesNotContain(active!, note => note.Id == noteId);
        Assert.Contains(withDeleted!, note => note.Id == noteId && note.DeletedAtUtc.HasValue);
    }

    [Fact]
    public async Task Note_OlderOfflineMutation_DoesNotOverwriteNewerValue()
    {
        using HttpClient client = await CreateAuthenticatedClientAsync();
        Guid noteId = Guid.NewGuid();
        DateTimeOffset newestTime = DateTimeOffset.UtcNow;
        var newest = new SyncNoteRequest(
            Guid.NewGuid(),
            null,
            "Новая версия",
            null,
            null,
            false,
            newestTime.AddHours(-1),
            newestTime);
        await PutAsync<SyncedNoteResponse>(client, $"/api/v1/sync/notes/{noteId:D}", newest);

        using JsonContent content = JsonContent.Create(
            newest with
            {
                MutationId = Guid.NewGuid(),
                Text = "Устаревшая версия",
                UpdatedAtUtc = newestTime.AddMinutes(-1),
            });
        using HttpResponseMessage response = await client.PutAsync(
            $"/api/v1/sync/notes/{noteId:D}",
            content);
        SyncedNoteResponse? staleResult = await response.Content.ReadFromJsonAsync<SyncedNoteResponse>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(staleResult);
        Assert.Equal(SyncMutationDisposition.Conflict, staleResult.Disposition);
        Assert.False(staleResult.WasApplied);
        Assert.Equal("Новая версия", staleResult.Text);
        Assert.Equal(1, staleResult.Revision);
    }

    [Fact]
    public async Task Note_OlderSuccessfulMutationRetry_IsRecognizedAfterNewerMutation()
    {
        using HttpClient client = await CreateAuthenticatedClientAsync();
        Guid noteId = Guid.NewGuid();
        DateTimeOffset createdAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var firstMutation = new SyncNoteRequest(
            Guid.NewGuid(),
            null,
            "Первая версия",
            null,
            null,
            false,
            createdAt,
            createdAt);
        await PutAsync<SyncedNoteResponse>(
            client,
            $"/api/v1/sync/notes/{noteId:D}",
            firstMutation);
        await PutAsync<SyncedNoteResponse>(
            client,
            $"/api/v1/sync/notes/{noteId:D}",
            firstMutation with
            {
                MutationId = Guid.NewGuid(),
                Text = "Вторая версия",
                UpdatedAtUtc = createdAt.AddMinutes(1),
            });

        SyncedNoteResponse repeated = await PutAsync<SyncedNoteResponse>(
            client,
            $"/api/v1/sync/notes/{noteId:D}",
            firstMutation);

        Assert.Equal(SyncMutationDisposition.AlreadyApplied, repeated.Disposition);
        Assert.False(repeated.WasApplied);
        Assert.Equal("Вторая версия", repeated.Text);
        Assert.Equal(2, repeated.Revision);
    }

    [Fact]
    public async Task MutationId_ReusedForDifferentEntity_ReturnsConflict()
    {
        using HttpClient client = await CreateAuthenticatedClientAsync();
        Guid mutationId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var request = new SyncNoteRequest(
            mutationId,
            null,
            "Текст",
            null,
            null,
            false,
            now,
            now);
        await PutAsync<SyncedNoteResponse>(
            client,
            $"/api/v1/sync/notes/{Guid.NewGuid():D}",
            request);

        using JsonContent content = JsonContent.Create(request);
        using HttpResponseMessage response = await client.PutAsync(
            $"/api/v1/sync/notes/{Guid.NewGuid():D}",
            content);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Assignments_AreCreatedUpdatedAndDeleted()
    {
        using HttpClient client = await CreateAuthenticatedClientAsync();
        Guid assignmentId = Guid.NewGuid();
        DateTimeOffset createdAt = DateTimeOffset.UtcNow.AddMinutes(-3);
        var create = new SyncAssignmentRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Алгоритмы",
            "Решить задачи",
            createdAt.AddDays(2),
            AssignmentSyncStatus.New,
            createdAt,
            createdAt);

        SyncedAssignmentResponse created = await PutAsync<SyncedAssignmentResponse>(
            client,
            $"/api/v1/sync/assignments/{assignmentId:D}",
            create);
        SyncedAssignmentResponse completed = await PutAsync<SyncedAssignmentResponse>(
            client,
            $"/api/v1/sync/assignments/{assignmentId:D}",
            create with
            {
                MutationId = Guid.NewGuid(),
                Status = AssignmentSyncStatus.Completed,
                UpdatedAtUtc = createdAt.AddMinutes(1),
            });
        DateTimeOffset deletedAt = createdAt.AddMinutes(2);
        using HttpResponseMessage deleteResponse = await client.DeleteAsync(
            $"/api/v1/sync/assignments/{assignmentId:D}?mutationId={Guid.NewGuid():D}&deletedAtUtc={Uri.EscapeDataString(deletedAt.ToString("O"))}");

        Assert.Equal(AssignmentSyncStatus.New, created.Status);
        Assert.Equal(AssignmentSyncStatus.Completed, completed.Status);
        Assert.Equal(2, completed.Revision);
        deleteResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Installations_CannotReadEachOthersNotes()
    {
        using HttpClient firstClient = await CreateAuthenticatedClientAsync();
        using HttpClient secondClient = await CreateAuthenticatedClientAsync();
        Guid sharedNoteId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var firstNote = new SyncNoteRequest(
            Guid.NewGuid(), null, "Первая установка", null, null, false, now, now);
        var secondNote = firstNote with
        {
            MutationId = Guid.NewGuid(),
            Text = "Вторая установка",
        };

        await PutAsync<SyncedNoteResponse>(firstClient, $"/api/v1/sync/notes/{sharedNoteId:D}", firstNote);
        await PutAsync<SyncedNoteResponse>(secondClient, $"/api/v1/sync/notes/{sharedNoteId:D}", secondNote);

        IReadOnlyList<SyncedNoteResponse>? firstItems = await firstClient.GetFromJsonAsync<IReadOnlyList<SyncedNoteResponse>>(
            "/api/v1/sync/notes");
        IReadOnlyList<SyncedNoteResponse>? secondItems = await secondClient.GetFromJsonAsync<IReadOnlyList<SyncedNoteResponse>>(
            "/api/v1/sync/notes");
        Assert.Contains(firstItems!, note => note.Id == sharedNoteId && note.Text == "Первая установка");
        Assert.DoesNotContain(firstItems!, note => note.Text == "Вторая установка");
        Assert.Contains(secondItems!, note => note.Id == sharedNoteId && note.Text == "Вторая установка");
        Assert.DoesNotContain(secondItems!, note => note.Text == "Первая установка");
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        var registration = new RegisterInstallationRequest(
            Guid.NewGuid(),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            "android",
            "1.0.0");
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/installations/register",
            registration);
        RegisterInstallationResponse? payload = await response.Content
            .ReadFromJsonAsync<RegisterInstallationResponse>();
        response.EnsureSuccessStatusCode();
        Assert.NotNull(payload);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            payload.TokenType,
            payload.AccessToken);
        return client;
    }

    private static async Task<TResponse> PutAsync<TResponse>(
        HttpClient client,
        string path,
        object request)
    {
        using JsonContent content = JsonContent.Create(request, request.GetType());
        using HttpResponseMessage response = await client.PutAsync(path, content);
        TResponse? payload = await response.Content.ReadFromJsonAsync<TResponse>();
        response.EnsureSuccessStatusCode();
        return Assert.IsType<TResponse>(payload);
    }
}
