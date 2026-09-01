using UniversitySchedule.Application.Scheduling;
using UniversitySchedule.Domain.Scheduling;

namespace UniversitySchedule.Application.Tests.Scheduling;

public sealed class ScheduleStatusServiceTests
{
    [Fact]
    public void GetCurrentAndNext_UsesInjectedTimeProvider()
    {
        DateTimeOffset now = new(2026, 9, 1, 5, 30, 0, TimeSpan.Zero);
        var service = new ScheduleStatusService(new FixedTimeProvider(now));
        var lesson = new LessonOccurrence(
            Guid.NewGuid(),
            "Физика",
            1,
            now.AddMinutes(-30),
            now.AddMinutes(60));

        SchedulePosition result = service.GetCurrentAndNext([lesson]);

        Assert.Same(lesson, result.Current);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
