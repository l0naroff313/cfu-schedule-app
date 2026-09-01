namespace UniversitySchedule.Domain.Scheduling;

public sealed record SchedulePosition(
    LessonOccurrence? Current,
    LessonOccurrence? Next);
