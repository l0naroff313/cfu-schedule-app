using System.Text.Json.Serialization;

namespace UniversitySchedule.Mobile.Core.Cfu;

/// <summary>
/// A locally bundled schedule overlay produced from an operator supplied Excel workbook.
/// It deliberately contains only the groups that were supplied by the workbook; all other
/// groups continue to use the official CFU API.
/// </summary>
public sealed class ManualScheduleOverrideDocument
{
    [JsonPropertyName("sourceFile")]
    public string SourceFile { get; init; } = string.Empty;

    [JsonPropertyName("importedAtUtc")]
    public DateTimeOffset ImportedAtUtc { get; init; }

    [JsonPropertyName("bells")]
    public IReadOnlyList<CfuBellDocument> Bells { get; init; } = [];

    [JsonPropertyName("weeks")]
    public CfuWeeksDocument Weeks { get; init; } = new();

    [JsonPropertyName("tree")]
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>> Tree { get; init; }
        = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>>();

    [JsonPropertyName("groups")]
    public IReadOnlyList<CfuGroupScheduleDocument> Groups { get; init; } = [];

    public CfuScheduleIndexDocument ToIndex() => new()
    {
        Bells = Bells,
        Weeks = Weeks,
        Tree = Tree,
    };

    public CfuGroupScheduleDocument? FindGroup(string groupCode) => Groups.FirstOrDefault(group =>
        string.Equals(group.Code.Trim(), groupCode.Trim(), StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<CfuLessonDocument> FindTeacherLessons(string query)
    {
        string normalized = query.Trim();
        return Groups
            .SelectMany(group => group.Lessons)
            .Where(lesson => lesson.Teachers.Any(teacher => MatchesTeacher(teacher, normalized)))
            .ToArray();
    }

    private static bool MatchesTeacher(string teacher, string query)
    {
        string normalizedTeacher = string.Join(' ', teacher.Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (string.Equals(normalizedTeacher, query, StringComparison.CurrentCultureIgnoreCase))
        {
            return true;
        }

        int firstSpace = normalizedTeacher.IndexOf(' ');
        return firstSpace > 0 &&
               !query.Contains(' ', StringComparison.Ordinal) &&
               string.Equals(normalizedTeacher[..firstSpace], query, StringComparison.CurrentCultureIgnoreCase);
    }
}

public interface IManualScheduleOverrideProvider
{
    Task<ManualScheduleOverrideDocument?> LoadAsync(CancellationToken cancellationToken = default);
}
