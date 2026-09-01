using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace UniversitySchedule.ScheduleImporter.Sources;

public sealed class CfuScheduleSourceClient(
    CachedHttpSource source,
    ILogger<CfuScheduleSourceClient> logger)
{
    public const string IndexUrl = "https://cfuv.ru/wp-json/cfu/v1/sched/index";
    public const string ApiBaseUrl = "https://cfuv.ru/wp-json/cfu/v1/sched/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly CachedHttpSource _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly ILogger<CfuScheduleSourceClient> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<CfuScheduleIndexDocument> LoadIndexAsync(CancellationToken cancellationToken)
    {
        string content = await _source.GetAsync(
            new Uri(IndexUrl),
            "cfu-index",
            applyVuzopediaDelay: false,
            cancellationToken);
        CfuScheduleIndexDocument document = Deserialize<CfuScheduleIndexDocument>(content);
        if (document.Tree.Count == 0 || document.Bells.Count == 0)
        {
            throw new InvalidDataException("The CFU schedule index is empty.");
        }

        return document;
    }

    public async Task<IReadOnlyList<CfuGroupScheduleSource>> LoadGroupSchedulesAsync(
        CfuScheduleIndexDocument index,
        CancellationToken cancellationToken)
    {
        CfuGroupSource[] groups = EnumerateGroups(index).ToArray();
        var schedules = new ConcurrentBag<CfuGroupScheduleSource>();
        int completed = 0;

        await Parallel.ForEachAsync(
            groups,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = 4,
            },
            async (group, token) =>
            {
                Uri uri = new($"{ApiBaseUrl}group?code={Uri.EscapeDataString(group.GroupCode)}");
                string content = await _source.GetAsync(
                    uri,
                    "cfu-groups",
                    applyVuzopediaDelay: false,
                    token);
                CfuGroupScheduleDocument schedule = Deserialize<CfuGroupScheduleDocument>(content);
                if (!string.Equals(
                        schedule.Code.Trim(),
                        group.GroupCode.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"CFU returned another group for {group.GroupCode}.");
                }

                schedules.Add(new CfuGroupScheduleSource(group, schedule));
                int current = Interlocked.Increment(ref completed);
                if (current % 25 == 0 || current == groups.Length)
                {
                    _logger.LogInformation("Loaded {Completed}/{Total} CFU group schedules", current, groups.Length);
                }
            });

        return schedules
            .OrderBy(item => item.Group.GroupCode, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static IEnumerable<CfuGroupSource> EnumerateGroups(CfuScheduleIndexDocument index)
    {
        foreach ((string instituteName, IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> directions) in index.Tree)
        {
            foreach ((string directionName, IReadOnlyDictionary<string, IReadOnlyList<string>> courses) in directions)
            {
                foreach (IReadOnlyList<string> groupCodes in courses.Values)
                {
                    foreach (string groupCode in groupCodes.Where(value => !string.IsNullOrWhiteSpace(value)))
                    {
                        yield return new CfuGroupSource(instituteName, directionName, groupCode.Trim());
                    }
                }
            }
        }
    }

    private static T Deserialize<T>(string content)
    {
        return JsonSerializer.Deserialize<T>(content, JsonOptions)
            ?? throw new InvalidDataException($"Could not deserialize {typeof(T).Name}.");
    }
}
