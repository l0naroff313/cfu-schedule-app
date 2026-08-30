namespace UniversitySchedule.Contracts.Schedule;

public sealed record CurrentScheduleResponse(
    DateTimeOffset EvaluatedAtUtc,
    ScheduleLesson? Current,
    ScheduleLesson? Next);
