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
    private const string AccessTokenKey = "cfu.installation.access-token.v1";
    private const string AccessTokenExpiresAtKey = "cfu.installation.access-token-expires-at.v1";
    private readonly SemaphoreSlim _authenticationLock = new(1, 1);
    private string? _cachedAccessToken;
    private DateTimeOffset _cachedExpiresAtUtc;

    public bool IsEnabled => options.IsEnabled;

    public async Task<bool> TryPushAsync(
        PersonalDataSyncOperation operation,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return false;
        }

        try
        {
            string? accessToken = await GetAccessTokenAsync(forceRefresh: false, cancellationToken);
            if (accessToken is null)
            {
                return false;
            }

            HttpStatusCode status = await SendOperationAsync(operation, accessToken, cancellationToken);
            if (status != HttpStatusCode.Unauthorized)
            {
                return IsSuccessful(status);
            }

            accessToken = await GetAccessTokenAsync(forceRefresh: true, cancellationToken);
            if (accessToken is null)
            {
                return false;
            }

            status = await SendOperationAsync(operation, accessToken, cancellationToken);
            return IsSuccessful(status);
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
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

    private async Task<HttpStatusCode> SendOperationAsync(
        PersonalDataSyncOperation operation,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(operation);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        return response.StatusCode;
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
}
