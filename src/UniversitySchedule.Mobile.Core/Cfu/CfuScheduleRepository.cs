using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using UniversitySchedule.Contracts.Schedule;
using UniversitySchedule.Contracts.Catalog;
using UniversitySchedule.Mobile.Core.Storage;

namespace UniversitySchedule.Mobile.Core.Cfu;

public sealed record CfuCatalogLoadResult(
    CfuScheduleCatalog Catalog,
    DateTimeOffset UpdatedAtUtc,
    bool IsFromCache);

public sealed record CfuScheduleLoadResult(
    ScheduleSnapshot Snapshot,
    DateTimeOffset UpdatedAtUtc,
    bool IsFromCache,
    string? Warning = null,
    ReferenceScheduleCalendar? Calendar = null);

public sealed record CfuTeacherSearchLoadResult(
    CfuTeacherScheduleSearch Search,
    DateTimeOffset UpdatedAtUtc,
    bool IsFromCache);

public sealed class CfuScheduleRepository
{
    public const string BaseAddress = "https://cfuv.ru/wp-json/cfu/v1/sched/";

    private const string IndexKey = "cfu:index";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

	private readonly HttpClient _httpClient;
	private readonly ILocalDataStore _localDataStore;
	private readonly IManualScheduleOverrideProvider _manualScheduleOverrideProvider;
	private ManualScheduleOverrideDocument? _manualScheduleOverride;
	private bool _manualScheduleLoaded;
	private readonly SemaphoreSlim _manualScheduleLock = new(1, 1);

	public CfuScheduleRepository(
		HttpClient httpClient,
		ILocalDataStore localDataStore,
		IManualScheduleOverrideProvider? manualScheduleOverrideProvider = null)
	{
		_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
		_localDataStore = localDataStore ?? throw new ArgumentNullException(nameof(localDataStore));
		_manualScheduleOverrideProvider = manualScheduleOverrideProvider ?? EmptyManualScheduleOverrideProvider.Instance;
	}

	public async Task<CfuCatalogLoadResult> LoadCatalogAsync(
		CancellationToken cancellationToken = default)
	{
		try
		{
			DocumentLoadResult<CfuScheduleIndexDocument> result = await LoadNetworkFirstAsync<CfuScheduleIndexDocument>(
				"cfu:catalog",
				"index",
				ValidateIndex,
				cancellationToken);
			ManualScheduleOverrideDocument? manual = await LoadManualScheduleOverrideAsync(cancellationToken);
			CfuScheduleIndexDocument index = manual is null || manual.PreferOfficialApi || manual.Tree.Count == 0
				? result.Value
				: MergeCatalogIndex(result.Value, manual);
			return new CfuCatalogLoadResult(
				CfuScheduleCatalogMapper.Map(index),
				result.UpdatedAtUtc,
				result.IsFromCache);
		}
		catch (InvalidOperationException)
		{
			ManualScheduleOverrideDocument? manual = await LoadManualScheduleOverrideAsync(cancellationToken);
			if (manual is null || manual.Bells.Count == 0 || manual.Tree.Count == 0)
			{
				throw;
			}

			return new CfuCatalogLoadResult(
				CfuScheduleCatalogMapper.Map(manual.ToIndex()),
				manual.ImportedAtUtc,
				IsFromCache: true);
		}
	}

    public async Task<CfuScheduleLoadResult?> LoadCachedGroupScheduleAsync(
        string groupCode,
        int? subgroup = null,
        CancellationToken cancellationToken = default)
    {
		ArgumentException.ThrowIfNullOrWhiteSpace(groupCode);

		// Startup and readiness checks must only read durable local storage.
		LocalDocument? saved = await _localDataStore.GetAsync(CachedGroupKey(groupCode), cancellationToken);
		if (saved is not null)
		{
			CachedGroupDocument pair = Deserialize<CachedGroupDocument>(saved.Content, value => value);
			bool repaired = !CfuCalendarIntegrity.IsUsable(pair.Index);
			CfuScheduleIndexDocument calendar = repaired
				? (await RecoverCalendarAsync(null, cancellationToken)).Value
				: pair.Index;
			return new CfuScheduleLoadResult(
				CfuScheduleMapper.MapGroup(ValidateScheduleIndex(calendar), ValidateGroup(pair.Schedule, groupCode), subgroup),
				saved.UpdatedAtUtc,
				IsFromCache: true,
				Warning: repaired ? CfuCalendarIntegrity.FallbackWarning : pair.Warning,
				Calendar: ToCalendar(calendar));
		}

		// Compatibility with copies saved by earlier app versions.
		LocalDocument? indexDocument = await _localDataStore.GetAsync(IndexKey, cancellationToken);
        LocalDocument? groupDocument = await _localDataStore.GetAsync(GroupKey(groupCode), cancellationToken);
        if (indexDocument is null || groupDocument is null)
        {
            return null;
        }

        CfuScheduleIndexDocument index = Deserialize<CfuScheduleIndexDocument>(indexDocument.Content, ValidateIndex);
        bool repairedLegacy = !CfuCalendarIntegrity.IsUsable(index);
        if (repairedLegacy) index = (await RecoverCalendarAsync(null, cancellationToken)).Value;
        CfuGroupScheduleDocument schedule = Deserialize<CfuGroupScheduleDocument>(
            groupDocument.Content,
            value => ValidateGroup(value, groupCode));
        DateTimeOffset updatedAt = indexDocument.UpdatedAtUtc < groupDocument.UpdatedAtUtc
            ? indexDocument.UpdatedAtUtc
            : groupDocument.UpdatedAtUtc;
        return new CfuScheduleLoadResult(
            CfuScheduleMapper.MapGroup(index, schedule, subgroup),
            updatedAt,
            IsFromCache: true,
            Warning: repairedLegacy ? CfuCalendarIntegrity.FallbackWarning : null,
            Calendar: ToCalendar(index));
    }

    public async Task<CfuScheduleLoadResult> LoadGroupScheduleAsync(
        string groupCode,
        int? subgroup = null,
        CancellationToken cancellationToken = default)
    {
		ArgumentException.ThrowIfNullOrWhiteSpace(groupCode);

		ManualScheduleOverrideDocument? manual = await LoadManualScheduleOverrideAsync(cancellationToken);
		CfuGroupScheduleDocument? manualSchedule = manual?.FindGroup(groupCode);
			if (manual is not null && !manual.PreferOfficialApi && manualSchedule is not null && manual.Bells.Count > 0)
		{
			CfuScheduleIndexDocument manualIndex = manual.ToIndex();
			await SaveManualScheduleCacheAsync(manualIndex, manualSchedule, manual.ImportedAtUtc, cancellationToken);

			return new CfuScheduleLoadResult(
				CfuScheduleMapper.MapGroup(manualIndex, manualSchedule, subgroup),
				manual.ImportedAtUtc,
				IsFromCache: true,
				Calendar: ToCalendar(manualIndex));
		}

        if (manual is { PreferOfficialApi: true } && manualSchedule is not null && manual.Bells.Count > 0)
        {
            // Seed only older/missing copies. A newer successful API response must never be rolled back.
            await SeedFallbackAsync(IndexKey, manual.ToIndex(), manual.ImportedAtUtc, cancellationToken);
            await SeedFallbackAsync(GroupKey(groupCode), manualSchedule, manual.ImportedAtUtc, cancellationToken);
        }

		DocumentLoadResult<CfuScheduleIndexDocument> index = await LoadScheduleIndexAsync(groupCode, cancellationToken);
        DocumentLoadResult<CfuGroupScheduleDocument> schedule = await LoadNetworkFirstAsync<CfuGroupScheduleDocument>(
            GroupKey(groupCode),
            $"group?code={Uri.EscapeDataString(groupCode.Trim())}",
            value => ValidateMappedGroup(value, groupCode, index.Value),
            cancellationToken);
        DateTimeOffset updatedAt = index.UpdatedAtUtc < schedule.UpdatedAtUtc
            ? index.UpdatedAtUtc
            : schedule.UpdatedAtUtc;

        string? warning = index.IsFromCache ? CfuCalendarIntegrity.FallbackWarning : null;
        await SavePairedCacheAsync(index.Value, schedule.Value, updatedAt, cancellationToken, warning);
        return new CfuScheduleLoadResult(
            CfuScheduleMapper.MapGroup(index.Value, schedule.Value, subgroup),
            updatedAt,
            index.IsFromCache || schedule.IsFromCache,
            warning,
            ToCalendar(index.Value));
    }

    public async Task<CfuTeacherSearchLoadResult> SearchTeachersAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        string normalizedQuery = query.Trim();
		if (normalizedQuery.Length < 2)
		{
			throw new ArgumentException("Teacher query must contain at least two characters.", nameof(query));
		}

		ManualScheduleOverrideDocument? manual = await LoadManualScheduleOverrideAsync(cancellationToken);
		IReadOnlyList<CfuLessonDocument> manualLessons = manual?.FindTeacherLessons(normalizedQuery) ?? [];
		if (manual is not null && !manual.PreferOfficialApi && manualLessons.Count > 0 && manual.Bells.Count > 0)
		{
			return new CfuTeacherSearchLoadResult(
				CfuScheduleMapper.MapTeacherSearch(manual.ToIndex(), manualLessons),
				manual.ImportedAtUtc,
				IsFromCache: true);
		}

        string key = $"cfu:teacher:{NormalizeKey(normalizedQuery)}";
        if (manual is { PreferOfficialApi: true } && manual.Bells.Count > 0)
        {
            await SeedFallbackAsync(IndexKey, manual.ToIndex(), manual.ImportedAtUtc, cancellationToken);
            await SeedFallbackAsync(key, manualLessons, manual.ImportedAtUtc, cancellationToken);
        }

        DocumentLoadResult<CfuScheduleIndexDocument> index = await LoadScheduleIndexAsync(null, cancellationToken);
        DocumentLoadResult<IReadOnlyList<CfuLessonDocument>> lessons = await LoadNetworkFirstAsync<IReadOnlyList<CfuLessonDocument>>(
            key,
            $"find?by=teacher&q={Uri.EscapeDataString(normalizedQuery)}",
            value => value ?? throw new InvalidDataException("Teacher response is empty."),
            cancellationToken);
        DateTimeOffset updatedAt = index.UpdatedAtUtc < lessons.UpdatedAtUtc
            ? index.UpdatedAtUtc
            : lessons.UpdatedAtUtc;

        return new CfuTeacherSearchLoadResult(
            CfuScheduleMapper.MapTeacherSearch(index.Value, lessons.Value),
            updatedAt,
            index.IsFromCache || lessons.IsFromCache);
    }

    private async Task SeedFallbackAsync<T>(string key, T value, DateTimeOffset capturedAt,
        CancellationToken cancellationToken)
    {
        LocalDocument? existing = await _localDataStore.GetAsync(key, cancellationToken);
        if (existing is null || existing.UpdatedAtUtc < capturedAt)
            await _localDataStore.SaveAsync(new LocalDocument(key,
                JsonSerializer.Serialize(value, JsonOptions), capturedAt), cancellationToken);
    }

    private async Task<DocumentLoadResult<T>> LoadNetworkFirstAsync<T>(
        string key,
        string relativeUri,
        Func<T, T> validate,
        CancellationToken cancellationToken)
    {
        Exception? networkError = null;

        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(relativeUri, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new InvalidDataException("The requested schedule was not found by CFU.");
            }

            response.EnsureSuccessStatusCode();
            string content = await response.Content.ReadAsStringAsync(cancellationToken);
            T value = Deserialize(content, validate);
            var document = new LocalDocument(key, content, DateTimeOffset.UtcNow);
            await _localDataStore.SaveAsync(document, cancellationToken);
            return new DocumentLoadResult<T>(value, document.UpdatedAtUtc, IsFromCache: false);
        }
        catch (Exception exception) when (IsRecoverableNetworkOrSourceError(exception, cancellationToken))
        {
            networkError = exception;
        }

        LocalDocument? cached = await _localDataStore.GetAsync(key, cancellationToken);
        if (cached is null)
        {
            throw new InvalidOperationException(
                "CFU schedule is unavailable and no local copy has been saved yet.",
                networkError);
        }

        return new DocumentLoadResult<T>(
            Deserialize(cached.Content, validate),
            cached.UpdatedAtUtc,
            IsFromCache: true);
    }

	private static T Deserialize<T>(string content, Func<T, T> validate)
    {
        T value = JsonSerializer.Deserialize<T>(content, JsonOptions)
            ?? throw new InvalidDataException("CFU returned an empty JSON document.");
        return validate(value);
	}

	private async Task<ManualScheduleOverrideDocument?> LoadManualScheduleOverrideAsync(
		CancellationToken cancellationToken)
	{
		if (_manualScheduleLoaded)
		{
			return _manualScheduleOverride;
		}

		await _manualScheduleLock.WaitAsync(cancellationToken);
		try
		{
			if (_manualScheduleLoaded) return _manualScheduleOverride;
			_manualScheduleOverride = await _manualScheduleOverrideProvider.LoadAsync(cancellationToken);
			_manualScheduleLoaded = _manualScheduleOverride is not null;
		}
		catch (HttpRequestException)
		{
			_manualScheduleOverride = null;
		}
		catch (JsonException)
		{
			_manualScheduleOverride = null;
		}
		catch (InvalidDataException)
		{
			_manualScheduleOverride = null;
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			_manualScheduleOverride = null;
		}
		finally
		{
			_manualScheduleLock.Release();
		}

		return _manualScheduleOverride;
	}

	private async Task SaveManualScheduleCacheAsync(
		CfuScheduleIndexDocument index,
		CfuGroupScheduleDocument schedule,
		DateTimeOffset updatedAtUtc,
		CancellationToken cancellationToken)
	{
		await _localDataStore.SaveAsync(
			new LocalDocument(IndexKey, JsonSerializer.Serialize(index, JsonOptions), updatedAtUtc),
			cancellationToken);
		await _localDataStore.SaveAsync(
			new LocalDocument(GroupKey(schedule.Code), JsonSerializer.Serialize(schedule, JsonOptions), updatedAtUtc),
			cancellationToken);
		await SavePairedCacheAsync(index, schedule, updatedAtUtc, cancellationToken);
	}

	// Keep the group's calendar and lessons together even when teacher/catalog requests change the global index.
	private Task SavePairedCacheAsync(CfuScheduleIndexDocument index, CfuGroupScheduleDocument schedule,
		DateTimeOffset updatedAtUtc, CancellationToken cancellationToken, string? warning = null) =>
		_localDataStore.SaveAsync(new LocalDocument(CachedGroupKey(schedule.Code),
			JsonSerializer.Serialize(new CachedGroupDocument(index, schedule, warning), JsonOptions), updatedAtUtc), cancellationToken);

	private static string CachedGroupKey(string groupCode) => $"cfu:cached-group:{NormalizeKey(groupCode)}";
	private sealed record CachedGroupDocument(CfuScheduleIndexDocument Index, CfuGroupScheduleDocument Schedule, string? Warning = null);

    private async Task<DocumentLoadResult<CfuScheduleIndexDocument>> LoadScheduleIndexAsync(
        string? groupCode, CancellationToken cancellationToken)
    {
        try
        {
            return await LoadNetworkFirstAsync<CfuScheduleIndexDocument>(
                IndexKey, "index", ValidateScheduleIndex, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or JsonException)
        {
            return await RecoverCalendarAsync(groupCode, cancellationToken);
        }
    }

    private async Task<DocumentLoadResult<CfuScheduleIndexDocument>> RecoverCalendarAsync(
        string? groupCode, CancellationToken cancellationToken)
    {
        // A paired group copy is independent of catalog and teacher requests.
        if (groupCode is not null)
        {
            LocalDocument? saved = await _localDataStore.GetAsync(CachedGroupKey(groupCode), cancellationToken);
            if (saved is not null)
            {
                try
                {
                    var pair = Deserialize<CachedGroupDocument>(saved.Content, value => value);
                    return new(ValidateScheduleIndex(pair.Index), saved.UpdatedAtUtc, true);
                }
                catch (Exception exception) when (exception is JsonException or InvalidDataException) { }
            }
        }

        LocalDocument? global = await _localDataStore.GetAsync(IndexKey, cancellationToken);
        if (global is not null)
        {
            try { return new(Deserialize<CfuScheduleIndexDocument>(global.Content, ValidateScheduleIndex), global.UpdatedAtUtc, true); }
            catch (Exception exception) when (exception is JsonException or InvalidDataException) { }
        }

        ManualScheduleOverrideDocument? bundled = await LoadManualScheduleOverrideAsync(cancellationToken);
        if (bundled is not null && CfuCalendarIntegrity.IsUsable(bundled.ToIndex()))
        {
            CfuScheduleIndexDocument index = ValidateScheduleIndex(bundled.ToIndex());
            // Repair copies poisoned by older app versions, retaining the actual capture timestamp.
            await _localDataStore.SaveAsync(new LocalDocument(IndexKey,
                JsonSerializer.Serialize(index, JsonOptions), bundled.ImportedAtUtc), cancellationToken);
            return new(index, bundled.ImportedAtUtc, true);
        }

        throw new InvalidDataException("КФУ вернул неполный календарь недель, а проверенной копии ещё нет. Повторите обновление позже.");
    }

    private static CfuScheduleIndexDocument ValidateScheduleIndex(CfuScheduleIndexDocument index)
    {
        ValidateIndex(index);
        if (!CfuCalendarIntegrity.IsUsable(index))
            throw new InvalidDataException("КФУ вернул неполный календарь учебных недель.");
        return index;
    }

    private static ReferenceScheduleCalendar ToCalendar(CfuScheduleIndexDocument index) => new(
        index.Bells.Select(bell => new ReferenceBell(bell.PairNumber, bell.StartsAt, bell.EndsAt)).ToArray(),
        index.Weeks.EvenWeekMondays, index.Weeks.OddWeekMondays);

    private static CfuGroupScheduleDocument ValidateMappedGroup(CfuGroupScheduleDocument group,
        string groupCode, CfuScheduleIndexDocument index)
    {
        ValidateGroup(group, groupCode);
        if (group.Lessons.Count > 0 && CfuScheduleMapper.MapGroup(index, group).Lessons.Count == 0)
            throw new InvalidDataException("Занятия КФУ не удалось привязать к календарю. Сохранена предыдущая копия.");
        return group;
    }

	private static CfuScheduleIndexDocument MergeCatalogIndex(
		CfuScheduleIndexDocument official,
		ManualScheduleOverrideDocument manual)
	{
		return new CfuScheduleIndexDocument
		{
			Bells = official.Bells.Count > 0 ? official.Bells : manual.Bells,
			Weeks = official.Weeks.EvenWeekMondays.Count > 0 || official.Weeks.OddWeekMondays.Count > 0
				? official.Weeks
				: manual.Weeks,
			CurrentWeek = official.CurrentWeek,
			Tree = MergeTrees(official.Tree, manual.Tree),
		};
	}

	private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>> MergeTrees(
		IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>> official,
		IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>> manual)
	{
		var merged = new Dictionary<string, Dictionary<string, Dictionary<string, HashSet<string>>>>(StringComparer.CurrentCultureIgnoreCase);
		foreach (var source in new[] { official, manual })
		{
			foreach ((string instituteName, IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> directions) in source)
			{
				if (!merged.TryGetValue(instituteName, out Dictionary<string, Dictionary<string, HashSet<string>>>? institute))
				{
					institute = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.CurrentCultureIgnoreCase);
					merged[instituteName] = institute;
				}

				foreach ((string directionName, IReadOnlyDictionary<string, IReadOnlyList<string>> courses) in directions)
				{
					if (!institute.TryGetValue(directionName, out Dictionary<string, HashSet<string>>? direction))
					{
						direction = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
						institute[directionName] = direction;
					}

					foreach ((string courseName, IReadOnlyList<string> groups) in courses)
					{
						if (!direction.TryGetValue(courseName, out HashSet<string>? groupSet))
						{
							groupSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
							direction[courseName] = groupSet;
						}

						groupSet.UnionWith(groups.Where(group => !string.IsNullOrWhiteSpace(group)));
					}
				}
			}
		}

		return merged.ToDictionary(
			institute => institute.Key,
			institute => (IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>)institute.Value.ToDictionary(
				direction => direction.Key,
				direction => (IReadOnlyDictionary<string, IReadOnlyList<string>>)direction.Value.ToDictionary(
					course => course.Key,
					course => (IReadOnlyList<string>)course.Value.OrderBy(group => group, StringComparer.CurrentCultureIgnoreCase).ToArray(),
					StringComparer.OrdinalIgnoreCase),
				StringComparer.CurrentCultureIgnoreCase),
			StringComparer.CurrentCultureIgnoreCase);
	}

    private static CfuScheduleIndexDocument ValidateIndex(CfuScheduleIndexDocument index)
    {
        if (index is null || index.Tree is null || index.Tree.Count == 0 || index.Bells is null ||
            index.Bells.Count == 0 || index.Weeks is null)
        {
            throw new InvalidDataException("CFU catalog has no institutes or bell schedule.");
        }

        return index;
    }

    private static CfuGroupScheduleDocument ValidateGroup(
        CfuGroupScheduleDocument schedule,
        string requestedGroupCode)
    {
        if (schedule is null || schedule.Lessons is null || string.IsNullOrWhiteSpace(schedule.Code) || !string.Equals(
                schedule.Code.Trim(),
                requestedGroupCode.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("CFU returned a schedule for another group.");
        }

        return schedule;
    }

    private static bool IsRecoverableNetworkOrSourceError(
        Exception exception,
        CancellationToken cancellationToken)
    {
        return exception is HttpRequestException or JsonException or InvalidDataException ||
               exception is TaskCanceledException && !cancellationToken.IsCancellationRequested;
    }

    private static string GroupKey(string groupCode) => $"cfu:group:{NormalizeKey(groupCode)}";

    private static string NormalizeKey(string value)
    {
        return string.Join(
                ' ',
                value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant()
            .Replace('ё', 'е');
    }

	private sealed record DocumentLoadResult<T>(
		T Value,
		DateTimeOffset UpdatedAtUtc,
		bool IsFromCache);

	private sealed class EmptyManualScheduleOverrideProvider : IManualScheduleOverrideProvider
	{
		public static EmptyManualScheduleOverrideProvider Instance { get; } = new();

		public Task<ManualScheduleOverrideDocument?> LoadAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult<ManualScheduleOverrideDocument?>(null);
	}
}
