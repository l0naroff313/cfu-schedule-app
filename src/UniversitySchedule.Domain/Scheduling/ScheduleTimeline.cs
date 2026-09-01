namespace UniversitySchedule.Domain.Scheduling;

public static class ScheduleTimeline
{
    public static SchedulePosition Resolve(
        IEnumerable<LessonOccurrence> lessons,
        DateTimeOffset instant)
    {
        ArgumentNullException.ThrowIfNull(lessons);

        DateTimeOffset instantUtc = instant.ToUniversalTime();
        LessonOccurrence[] relevantLessons = lessons
            .Where(lesson => !lesson.IsCancelled && lesson.EndsAtUtc > instantUtc)
            .OrderBy(lesson => lesson.StartsAtUtc)
            .ThenBy(lesson => lesson.PairNumber)
            .ToArray();

        LessonOccurrence? current = relevantLessons
            .FirstOrDefault(lesson => lesson.StartsAtUtc <= instantUtc);

        LessonOccurrence? next = relevantLessons
            .FirstOrDefault(lesson => lesson.StartsAtUtc > instantUtc);

        return new SchedulePosition(current, next);
    }
}
