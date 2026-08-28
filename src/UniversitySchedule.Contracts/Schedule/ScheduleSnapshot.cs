namespace UniversitySchedule.Contracts.Schedule;

public sealed record ScheduleSnapshot(
    Guid GroupId,
    Guid? SubgroupId,
    string Version,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<ScheduleLesson> Lessons);

public sealed record ScheduleLesson(
    Guid Id,
    DateOnly Date,
    int PairNumber,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    string Subject,
    string? LessonType,
    string? Teacher,
    string? Classroom,
    string? Building,
    string Status,
    string? SourceNote);
