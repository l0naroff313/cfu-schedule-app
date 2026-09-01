using System.Text.Json.Serialization;

namespace UniversitySchedule.Infrastructure.Cfu;

public sealed class CfuScheduleIndexDocument
{
    [JsonPropertyName("bells")]
    public IReadOnlyList<CfuBellDocument> Bells { get; init; } = [];

    [JsonPropertyName("weeks")]
    public CfuWeeksDocument Weeks { get; init; } = new();

    [JsonPropertyName("tree")]
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>> Tree { get; init; }
        = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>>();
}

public sealed class CfuBellDocument
{
    [JsonPropertyName("пара")]
    public int PairNumber { get; init; }

    [JsonPropertyName("начало")]
    public string StartsAt { get; init; } = string.Empty;

    [JsonPropertyName("конец")]
    public string EndsAt { get; init; } = string.Empty;
}

public sealed class CfuWeeksDocument
{
    [JsonPropertyName("ch")]
    public IReadOnlyList<string> EvenWeekMondays { get; init; } = [];

    [JsonPropertyName("nch")]
    public IReadOnlyList<string> OddWeekMondays { get; init; } = [];
}

public sealed class CfuGroupScheduleDocument
{
    [JsonPropertyName("код")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("занятия")]
    public IReadOnlyList<CfuLessonDocument> Lessons { get; init; } = [];

    [JsonPropertyName("fak")]
    public IReadOnlyList<CfuFacultyLessonDocument> FacultyLessons { get; init; } = [];
}

public sealed class CfuLessonDocument
{
    [JsonPropertyName("группа")]
    public string GroupCode { get; init; } = string.Empty;

    [JsonPropertyName("подгруппа")]
    public int Subgroup { get; init; }

    [JsonPropertyName("день")]
    public int Day { get; init; }

    [JsonPropertyName("пара")]
    public int PairNumber { get; init; }

    [JsonPropertyName("чётность")]
    public string Parity { get; init; } = string.Empty;

    [JsonPropertyName("дата")]
    public string? Date { get; init; }

    [JsonPropertyName("предмет")]
    public string Subject { get; init; } = string.Empty;

    [JsonPropertyName("вид")]
    public string? LessonType { get; init; }

    [JsonPropertyName("преподаватели")]
    public IReadOnlyList<string> Teachers { get; init; } = [];

    [JsonPropertyName("аудитория")]
    public string? Classroom { get; init; }

    [JsonPropertyName("корпус")]
    public string? Building { get; init; }

    [JsonPropertyName("примечание")]
    public string? Note { get; init; }

    [JsonPropertyName("онлайн")]
    public string? Online { get; init; }
}

public sealed class CfuFacultyLessonDocument
{
    [JsonPropertyName("группа")]
    public string? GroupCode { get; init; }

    [JsonPropertyName("период")]
    public string Period { get; init; } = string.Empty;

    [JsonPropertyName("день")]
    public int Day { get; init; }

    [JsonPropertyName("пара")]
    public int PairNumber { get; init; }

    [JsonPropertyName("предмет")]
    public string Subject { get; init; } = string.Empty;

    [JsonPropertyName("вид")]
    public string? LessonType { get; init; }

    [JsonPropertyName("преподаватели")]
    public IReadOnlyList<string> Teachers { get; init; } = [];

    [JsonPropertyName("аудитория")]
    public string? Classroom { get; init; }

    [JsonPropertyName("корпус")]
    public string? Building { get; init; }
}
