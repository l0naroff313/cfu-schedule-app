using UniversitySchedule.Contracts.Catalog;

namespace UniversitySchedule.Contracts.Schedule;

public enum ScheduleScopeKind
{
    Group = 0,
    Teacher = 1,
}

public sealed record ScheduleScope(
    ScheduleScopeKind Kind,
    Guid Id,
    string DisplayName,
    Guid? SubgroupId = null);

public sealed record ScheduleSnapshot(
    ScheduleScope Scope,
    string Version,
    DateTimeOffset GeneratedAtUtc,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<ScheduleLesson> Lessons);

public sealed record ScheduleGroupReference(
    Guid Id,
    string DisplayName,
    Guid? SubgroupId = null);

public sealed record ScheduleLesson(
    Guid Id,
    DateOnly Date,
    int PairNumber,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    string Subject,
    string? LessonType,
    IReadOnlyList<TeacherSummary> Teachers,
    IReadOnlyList<ScheduleGroupReference> Groups,
    string? Classroom,
    string? Building,
    string? RawLocation,
    string Status,
    string? SourceNote);
