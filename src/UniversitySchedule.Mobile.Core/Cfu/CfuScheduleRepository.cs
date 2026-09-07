using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using UniversitySchedule.Contracts.Schedule;
using UniversitySchedule.Mobile.Core.Storage;

namespace UniversitySchedule.Mobile.Core.Cfu;

public sealed record CfuCatalogLoadResult(
    CfuScheduleCatalog Catalog,
    DateTimeOffset UpdatedAtUtc,
    bool IsFromCache);

public sealed record CfuScheduleLoadResult(
    ScheduleSnapshot Snapshot,
    DateTimeOffset UpdatedAtUtc,
    bool IsFromCache);

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
				IndexKey,
				"index",
				ValidateIndex,
				cancellationToken);
			ManualScheduleOverrideDocument? manual = await LoadManualScheduleOverrideAsync(cancellationToken);
			CfuScheduleIndexDocument index = manual is null || manual.Tree.Count == 0
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

		ManualScheduleOverrideDocument? manual = await LoadManualScheduleOverrideAsync(cancellationToken);
		CfuGroupScheduleDocument? manualSchedule = manual?.FindGroup(groupCode);
		if (manual is not null && manualSchedule is not null && manual.Bells.Count > 0)
		{
			return new CfuScheduleLoadResult(
				CfuScheduleMapper.MapGroup(manual.ToIndex(), manualSchedule, subgroup),
				manual.ImportedAtUtc,
				IsFromCache: true);
		}

		LocalDocument? indexDocument = await _localDataStore.GetAsync(IndexKey, cancellationToken);
        LocalDocument? groupDocument = await _localDataStore.GetAsync(GroupKey(groupCode), cancellationToken);
        if (indexDocument is null || groupDocument is null)
        {
            return null;
        }

        CfuScheduleIndexDocument index = Deserialize<CfuScheduleIndexDocument>(indexDocument.Content, ValidateIndex);
        CfuGroupScheduleDocument schedule = Deserialize<CfuGroupScheduleDocument>(
            groupDocument.Content,
            value => ValidateGroup(value, groupCode));
        DateTimeOffset updatedAt = indexDocument.UpdatedAtUtc < groupDocument.UpdatedAtUtc
            ? indexDocument.UpdatedAtUtc
            : groupDocument.UpdatedAtUtc;
        return new CfuScheduleLoadResult(
            CfuScheduleMapper.MapGroup(index, schedule, subgroup),
            updatedAt,
            IsFromCache: true);
    }

    public async Task<CfuScheduleLoadResult> LoadGroupScheduleAsync(
        string groupCode,
        int? subgroup = null,
        CancellationToken cancellationToken = default)
    {
		ArgumentException.ThrowIfNullOrWhiteSpace(groupCode);

		ManualScheduleOverrideDocument? manual = await LoadManualScheduleOverrideAsync(cancellationToken);
		CfuGroupScheduleDocument? manualSchedule = manual?.FindGroup(groupCode);
			if (manual is not null && manualSchedule is not null && manual.Bells.Count > 0)
		{
			CfuScheduleIndexDocument manualIndex;
			try
			{
				manualIndex = (await LoadNetworkFirstAsync<CfuScheduleIndexDocument>(
					IndexKey,
					"index",
					ValidateIndex,
					cancellationToken)).Value;
			}
			catch (InvalidOperationException)
			{
				manualIndex = manual.ToIndex();
			}
			await SaveManualScheduleCacheAsync(manualIndex, manualSchedule, manual.ImportedAtUtc, cancellationToken);

			return new CfuScheduleLoadResult(
				CfuScheduleMapper.MapGroup(manualIndex, manualSchedule, subgroup),
				manual.ImportedAtUtc,
				IsFromCache: true);
		}

		DocumentLoadResult<CfuScheduleIndexDocument> index = await LoadNetworkFirstAsync<CfuScheduleIndexDocument>(
            IndexKey,
            "index",
            ValidateIndex,
            cancellationToken);
        DocumentLoadResult<CfuGroupScheduleDocument> schedule = await LoadNetworkFirstAsync<CfuGroupScheduleDocument>(
            GroupKey(groupCode),
            $"group?code={Uri.EscapeDataString(groupCode.Trim())}",
            value => ValidateGroup(value, groupCode),
            cancellationToken);
        DateTimeOffset updatedAt = index.UpdatedAtUtc < schedule.UpdatedAtUtc
            ? index.UpdatedAtUtc
            : schedule.UpdatedAtUtc;

        return new CfuScheduleLoadResult(
            CfuScheduleMapper.MapGroup(index.Value, schedule.Value, subgroup),
            updatedAt,
            index.IsFromCache || schedule.IsFromCache);
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
		if (manual is not null && manualLessons.Count > 0 && manual.Bells.Count > 0)
		{
			return new CfuTeacherSearchLoadResult(
				CfuScheduleMapper.MapTeacherSearch(manual.ToIndex(), manualLessons),
				manual.ImportedAtUtc,
				IsFromCache: true);
		}

        DocumentLoadResult<CfuScheduleIndexDocument> index = await LoadNetworkFirstAsync<CfuScheduleIndexDocument>(
            IndexKey,
            "index",
            ValidateIndex,
            cancellationToken);
        string key = $"cfu:teacher:{NormalizeKey(normalizedQuery)}";
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

		_manualScheduleLoaded = true;
		try
		{
			_manualScheduleOverride = await _manualScheduleOverrideProvider.LoadAsync(cancellationToken);
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
        if (!string.Equals(
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
