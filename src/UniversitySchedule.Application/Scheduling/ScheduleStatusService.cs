using UniversitySchedule.Domain.Scheduling;

namespace UniversitySchedule.Application.Scheduling;

public sealed class ScheduleStatusService(TimeProvider timeProvider)
{
    private readonly TimeProvider _timeProvider = timeProvider
        ?? throw new ArgumentNullException(nameof(timeProvider));

    public SchedulePosition GetCurrentAndNext(IEnumerable<LessonOccurrence> lessons)
    {
        return ScheduleTimeline.Resolve(lessons, _timeProvider.GetUtcNow());
    }
}
