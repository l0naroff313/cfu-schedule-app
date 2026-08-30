using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using UniversitySchedule.Contracts.Identity;
using UniversitySchedule.Contracts.PersonalData;
using UniversitySchedule.Mobile.Core.Assignments;
using UniversitySchedule.Mobile.Core.Identity;

namespace UniversitySchedule.Mobile.Core.Sync;

public sealed class UniversityScheduleApiClient(
    HttpClient httpClient,
    UniversityScheduleApiOptions options,
    InstallationIdentityService installationIdentity,
    ISecureValueStore secureValueStore,
    TimeProvider timeProvider)
{
    private const int MaximumPushRequests = 3;
    private const string AccessTokenKey = "cfu.installation.access-token.v1";
    private const string AccessTokenExpiresAtKey = "cfu.installation.access-token-expires-at.v1";
    private readonly SemaphoreSlim _authenticationLock = new(1, 1);
    private string? _cachedAccessToken;
    private DateTimeOffset _cachedExpiresAtUtc;

    public bool IsEnabled => options.IsEnabled;

    public async Task<PersonalDataPushResult> PushAsync(
        PersonalDataSyncOperation operation,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return new PersonalDataPushResult(PersonalDataPushOutcome.NotConfigured, 0);
        }

        string? accessToken;
        try
        {
            accessToken = await GetAccessTokenAsync(forceRefresh: false, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return new PersonalDataPushResult(
                PersonalDataPushOutcome.RetryableFailure,
                0,
                "authentication_network_error");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PersonalDataPushResult(
                PersonalDataPushOutcome.RetryableFailure,
                0,
                "authentication_timeout");
        }

        if (accessToken is null)
        {
            return new PersonalDataPushResult(
                PersonalDataPushOutcome.RetryableFailure,
                0,
                "authentication_unavailable");
        }

        string currentAccessToken = accessToken;
        bool refreshedToken = false;
        int requestCount = 0;
        while (requestCount < MaximumPushRequests)
        {
            requestCount++;
            try
            {
                using HttpResponseMessage response = await SendOperationAsync(
                    operation,
                    currentAccessToken,
                    cancellationToken);

                if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshedToken)
                {
                    refreshedToken = true;
                    string? refreshedAccessToken = await GetAccessTokenAsync(
                        forceRefresh: true,
                        cancellationToken);
                    if (refreshedAccessToken is null)
                    {
                        return new PersonalDataPushResult(
                            PersonalDataPushOutcome.RetryableFailure,
                            requestCount,
                            "authentication_refresh_failed");
                    }

                    currentAccessToken = refreshedAccessToken;
                    continue;
                }

                if (IsSuccessful(response.StatusCode))
                {
                    SyncMutationDisposition? disposition = await ReadDispositionAsync(
                        operation,
                        response,
                        cancellationToken);
                    return disposition == SyncMutationDisposition.Conflict
                        ? new PersonalDataPushResult(
                            PersonalDataPushOutcome.Conflict,
                            requestCount,
                            "server_conflict",
                            await ReadServerStateAsync(response, cancellationToken))
                        : new PersonalDataPushResult(
                            PersonalDataPushOutcome.Succeeded,
                            requestCount);
                }

                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    return new PersonalDataPushResult(
                        PersonalDataPushOutcome.Conflict,
                        requestCount,
                        "server_conflict",
                        await ReadServerStateAsync(response, cancellationToken));
                }

                if (!IsTransient(response.StatusCode))
                {
                    return new PersonalDataPushResult(
                        PersonalDataPushOutcome.PermanentFailure,
                        requestCount,
                        $"http_{(int)response.StatusCode}");
                }

                if (requestCount < MaximumPushRequests)
                {
                    await DelayBeforeRetryAsync(response, requestCount, cancellationToken);
                }
            }
            catch (HttpRequestException) when (requestCount < MaximumPushRequests)
            {
                await DelayBeforeRetryAsync(response: null, requestCount, cancellationToken);
            }
            catch (TaskCanceledException) when (
                !cancellationToken.IsCancellationRequested &&
                requestCount < MaximumPushRequests)
            {
                await DelayBeforeRetryAsync(response: null, requestCount, cancellationToken);
            }
            catch (HttpRequestException)
            {
                return new PersonalDataPushResult(
                    PersonalDataPushOutcome.RetryableFailure,
                    requestCount,
                    "network_unavailable");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new PersonalDataPushResult(
                    PersonalDataPushOutcome.RetryableFailure,
                    requestCount,
                    "request_timeout");
            }
        }

        return new PersonalDataPushResult(
            PersonalDataPushOutcome.RetryableFailure,
            requestCount,
            "transient_retry_exhausted");
    }

    private async Task<string?> GetAccessTokenAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        await _authenticationLock.WaitAsync(cancellationToken);
        try
        {
            DateTimeOffset validAfter = timeProvider.GetUtcNow().ToUniversalTime().AddMinutes(1);
            if (!forceRefresh &&
                !string.IsNullOrWhiteSpace(_cachedAccessToken) &&
                _cachedExpiresAtUtc > validAfter)
            {
                return _cachedAccessToken;
            }

            if (!forceRefresh)
            {
                string? storedToken = await secureValueStore.GetAsync(AccessTokenKey, cancellationToken);
                string? storedExpiration = await secureValueStore.GetAsync(
                    AccessTokenExpiresAtKey,
                    cancellationToken);
                if (!string.IsNullOrWhiteSpace(storedToken) &&
                    DateTimeOffset.TryParseExact(
                        storedExpiration,
                        "O",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out DateTimeOffset expiresAt) &&
                    expiresAt.ToUniversalTime() > validAfter)
                {
                    _cachedAccessToken = storedToken;
                    _cachedExpiresAtUtc = expiresAt.ToUniversalTime();
                    return storedToken;
                }
            }

            await ClearAccessTokenAsync(cancellationToken);
            InstallationIdentity identity = await installationIdentity.GetOrCreateAsync(cancellationToken);
            using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
                "api/v1/installations/register",
                new RegisterInstallationRequest(
                    identity.Id,
                    identity.Secret,
                    options.Platform,
                    options.AppVersion),
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            RegisterInstallationResponse? registration = await response.Content
                .ReadFromJsonAsync<RegisterInstallationResponse>(cancellationToken);
            if (registration is null ||
                string.IsNullOrWhiteSpace(registration.AccessToken) ||
                !string.Equals(registration.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase) ||
                registration.ExpiresAtUtc <= validAfter)
            {
                return null;
            }

            _cachedExpiresAtUtc = registration.ExpiresAtUtc.ToUniversalTime();
            _cachedAccessToken = registration.AccessToken;
            await secureValueStore.SetAsync(
                AccessTokenExpiresAtKey,
                _cachedExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture),
                cancellationToken);
            await secureValueStore.SetAsync(AccessTokenKey, _cachedAccessToken, cancellationToken);
            return _cachedAccessToken;
        }
        finally
        {
            _authenticationLock.Release();
        }
    }

    private async Task ClearAccessTokenAsync(CancellationToken cancellationToken)
    {
        _cachedAccessToken = null;
        _cachedExpiresAtUtc = default;
        await secureValueStore.RemoveAsync(AccessTokenKey, cancellationToken);
        await secureValueStore.RemoveAsync(AccessTokenExpiresAtKey, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendOperationAsync(
        PersonalDataSyncOperation operation,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(operation);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await httpClient.SendAsync(request, cancellationToken);
    }

    private static async Task<SyncMutationDisposition?> ReadDispositionAsync(
        PersonalDataSyncOperation operation,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return operation.EntityKind switch
            {
                PersonalDataSyncEntityKind.Note =>
                    (await response.Content.ReadFromJsonAsync<SyncedNoteResponse>(cancellationToken))?.Disposition,
                PersonalDataSyncEntityKind.Assignment =>
                    (await response.Content.ReadFromJsonAsync<SyncedAssignmentResponse>(cancellationToken))?.Disposition,
                _ => null,
            };
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static async Task<string?> ReadServerStateAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string content = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(content) ? null : content;
    }

    private async Task DelayBeforeRetryAsync(
        HttpResponseMessage? response,
        int requestCount,
        CancellationToken cancellationToken)
    {
        TimeSpan delay = response?.Headers.RetryAfter?.Delta ??
            (response?.Headers.RetryAfter?.Date - timeProvider.GetUtcNow()) ??
            TimeSpan.FromMilliseconds(250 * Math.Pow(2, requestCount - 1));
        delay = delay < TimeSpan.Zero
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds, 5_000));
        await Task.Delay(delay, cancellationToken);
    }

    private static HttpRequestMessage CreateRequest(PersonalDataSyncOperation operation)
    {
        string entityPath = operation.EntityKind switch
        {
            PersonalDataSyncEntityKind.Note => "notes",
            PersonalDataSyncEntityKind.Assignment => "assignments",
            _ => throw new InvalidOperationException($"Unsupported sync entity: {operation.EntityKind}."),
        };
        string path = $"api/v1/sync/{entityPath}/{operation.EntityId:D}";

        if (operation.MutationKind == PersonalDataSyncMutationKind.Delete)
        {
            string timestamp = Uri.EscapeDataString(
                operation.OccurredAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            return new HttpRequestMessage(
                HttpMethod.Delete,
                $"{path}?mutationId={operation.MutationId:D}&deletedAtUtc={timestamp}");
        }

        if (operation.EntityKind == PersonalDataSyncEntityKind.Note && operation.Note is not null)
        {
            var payload = new SyncNoteRequest(
                operation.MutationId,
                operation.Note.LessonId,
                operation.Note.Text,
                operation.Note.Title,
                operation.Note.Subject,
                operation.Note.IsPinned,
                operation.Note.CreatedAtUtc,
                operation.Note.UpdatedAtUtc);
            return new HttpRequestMessage(HttpMethod.Put, path)
            {
                Content = JsonContent.Create(payload),
            };
        }

        if (operation.EntityKind == PersonalDataSyncEntityKind.Assignment && operation.Assignment is not null)
        {
            PersonalAssignment assignment = operation.Assignment;
            var payload = new SyncAssignmentRequest(
                operation.MutationId,
                assignment.LessonId,
                assignment.Subject,
                assignment.Text,
                assignment.DeadlineUtc,
                (AssignmentSyncStatus)assignment.Status,
                assignment.CreatedAtUtc,
                assignment.UpdatedAtUtc);
            return new HttpRequestMessage(HttpMethod.Put, path)
            {
                Content = JsonContent.Create(payload),
            };
        }

        throw new InvalidOperationException("The sync operation payload is missing or invalid.");
    }

    private static bool IsSuccessful(HttpStatusCode statusCode) =>
        (int)statusCode is >= 200 and <= 299;

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
        (int)statusCode is >= 500 and <= 599;
}
