using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UniversitySchedule.Domain.Scheduling;
using UniversitySchedule.Infrastructure.Persistence;

namespace UniversitySchedule.Infrastructure.Cfu;

public sealed record CfuDocumentLoadResult<T>(
    T Value,
    DateTimeOffset UpdatedAtUtc,
    bool IsFromCache);

public sealed class CfuScheduleBackendClient(
    HttpClient httpClient,
    AppDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<CfuScheduleCacheOptions> cacheOptions,
    ILogger<CfuScheduleBackendClient> logger)
{
    public const string BaseAddress = "https://cfuv.ru/wp-json/cfu/v1/sched/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public Task<CfuDocumentLoadResult<CfuScheduleIndexDocument>> LoadIndexAsync(
        CancellationToken cancellationToken = default) =>
        LoadNetworkFirstAsync<CfuScheduleIndexDocument>(
            "cfu:index",
            "index",
            ValidateIndex,
            cancellationToken);

    public Task<CfuDocumentLoadResult<CfuGroupScheduleDocument>> LoadGroupAsync(
        string groupCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupCode);
        string normalizedCode = groupCode.Trim();
        return LoadNetworkFirstAsync<CfuGroupScheduleDocument>(
            $"cfu:group:{NormalizeKey(normalizedCode)}",
            $"group?code={Uri.EscapeDataString(normalizedCode)}",
            value => ValidateGroup(value, normalizedCode),
            cancellationToken);
    }

    public Task<CfuDocumentLoadResult<IReadOnlyList<CfuLessonDocument>>> FindTeacherAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        string normalizedQuery = query.Trim();
        if (normalizedQuery.Length < 2)
        {
            throw new ArgumentException("Teacher query must contain at least two characters.", nameof(query));
        }

        return LoadNetworkFirstAsync<IReadOnlyList<CfuLessonDocument>>(
            $"cfu:teacher:{NormalizeKey(normalizedQuery)}",
            $"find?by=teacher&q={Uri.EscapeDataString(normalizedQuery)}",
            value => value ?? throw new InvalidDataException("CFU returned an empty teacher response."),
            cancellationToken);
    }

    private async Task<CfuDocumentLoadResult<T>> LoadNetworkFirstAsync<T>(
        string key,
        string relativeUri,
        Func<T, T> validate,
        CancellationToken cancellationToken)
    {
        DateTimeOffset requestStartedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        ScheduleSourceDocument? freshDocument = await dbContext.ScheduleSourceDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Key == key, cancellationToken);
        if (freshDocument is not null &&
            requestStartedAtUtc - freshDocument.FetchedAtUtc <= cacheOptions.Value.FreshFor)
        {
            return new CfuDocumentLoadResult<T>(
                Deserialize(freshDocument.PayloadJson, validate),
                freshDocument.FetchedAtUtc,
                IsFromCache: true);
        }

        Exception? sourceError = null;
        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(relativeUri, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new InvalidDataException("The requested schedule was not found by CFU.");
            }

            response.EnsureSuccessStatusCode();
            string content = await response.Content.ReadAsStringAsync(cancellationToken);
            T value = Deserialize(content, validate);
            DateTimeOffset fetchedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
            ScheduleSourceDocument? cached = await dbContext.ScheduleSourceDocuments.FindAsync(
                [key],
                cancellationToken);
            string sourceUrl = new Uri(httpClient.BaseAddress!, relativeUri).ToString();
            if (cached is null)
            {
                dbContext.ScheduleSourceDocuments.Add(ScheduleSourceDocument.Create(
                    key,
                    sourceUrl,
                    content,
                    fetchedAtUtc));
            }
            else
            {
                cached.Replace(sourceUrl, content, fetchedAtUtc);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return new CfuDocumentLoadResult<T>(value, fetchedAtUtc, IsFromCache: false);
        }
        catch (Exception exception) when (IsRecoverableSourceError(exception, cancellationToken))
        {
            sourceError = exception;
            logger.LogWarning(exception, "CFU source request failed for {DocumentKey}; trying PostgreSQL cache", key);
        }

        ScheduleSourceDocument? document = freshDocument ?? await dbContext.ScheduleSourceDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Key == key, cancellationToken);
        if (document is null)
        {
            throw new CfuScheduleUnavailableException(
                "CFU schedule is unavailable and PostgreSQL has no verified copy yet.",
                sourceError);
        }

        return new CfuDocumentLoadResult<T>(
            Deserialize(document.PayloadJson, validate),
            document.FetchedAtUtc,
            IsFromCache: true);
    }

    private static T Deserialize<T>(string content, Func<T, T> validate)
    {
        T value = JsonSerializer.Deserialize<T>(content, JsonOptions)
            ?? throw new InvalidDataException("CFU returned an empty JSON document.");
        return validate(value);
    }

    private static CfuScheduleIndexDocument ValidateIndex(CfuScheduleIndexDocument index)
    {
        if (index.Tree.Count == 0 || index.Bells.Count == 0)
        {
            throw new InvalidDataException("CFU catalog has no institutes or bell schedule.");
        }

        return index;
    }

    private static CfuGroupScheduleDocument ValidateGroup(
        CfuGroupScheduleDocument schedule,
        string requestedGroupCode)
    {
        if (!string.Equals(schedule.Code.Trim(), requestedGroupCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("CFU returned a schedule for another group.");
        }

        return schedule;
    }

    private static bool IsRecoverableSourceError(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or JsonException or InvalidDataException ||
        exception is TaskCanceledException && !cancellationToken.IsCancellationRequested;

    private static string NormalizeKey(string value) =>
        string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant()
            .Replace('ё', 'е');
}

public sealed class CfuScheduleUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
