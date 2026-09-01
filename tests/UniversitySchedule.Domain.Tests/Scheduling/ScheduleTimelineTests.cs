using UniversitySchedule.Domain.Scheduling;

namespace UniversitySchedule.Domain.Tests.Scheduling;

public sealed class ScheduleTimelineTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 9, 1, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Resolve_ReturnsCurrentAndNextLessons()
    {
        LessonOccurrence first = CreateLesson(1, 0, 90);
        LessonOccurrence second = CreateLesson(2, 110, 200);

        SchedulePosition result = ScheduleTimeline.Resolve(
            [second, first],
            BaseTime.AddMinutes(30));

        Assert.Same(first, result.Current);
        Assert.Same(second, result.Next);
    }

    [Fact]
    public void Resolve_SkipsCancelledLessons()
    {
        LessonOccurrence cancelled = CreateLesson(1, 10, 100, LessonStatus.Cancelled);
        LessonOccurrence active = CreateLesson(2, 110, 200);

        SchedulePosition result = ScheduleTimeline.Resolve(
            [cancelled, active],
            BaseTime);

        Assert.Null(result.Current);
        Assert.Same(active, result.Next);
    }

    [Fact]
    public void Resolve_TreatsLessonStartingAtInstantAsCurrent()
    {
        LessonOccurrence lesson = CreateLesson(1, 0, 90);

        SchedulePosition result = ScheduleTimeline.Resolve([lesson], BaseTime);

        Assert.Same(lesson, result.Current);
        Assert.Null(result.Next);
    }

    private static LessonOccurrence CreateLesson(
        int pairNumber,
        int startMinutes,
        int endMinutes,
        LessonStatus status = LessonStatus.Regular)
    {
        return new LessonOccurrence(
            Guid.NewGuid(),
            $"Предмет {pairNumber}",
            pairNumber,
            BaseTime.AddMinutes(startMinutes),
            BaseTime.AddMinutes(endMinutes),
            status);
    }
}
