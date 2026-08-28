using UniversitySchedule.Domain.Scheduling;

namespace UniversitySchedule.Mobile.Core.Scheduling;

public sealed class TeacherScheduleCoverage
{
    public TeacherScheduleCoverage(
        Guid teacherId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        IEnumerable<LessonOccurrence> lessons)
    {
        if (teacherId == Guid.Empty)
        {
            throw new ArgumentException("Teacher identifier cannot be empty.", nameof(teacherId));
        }

        ArgumentNullException.ThrowIfNull(lessons);

        DateTimeOffset startsAtUtc = startsAt.ToUniversalTime();
        DateTimeOffset endsAtUtc = endsAt.ToUniversalTime();
        if (endsAtUtc <= startsAtUtc)
        {
            throw new ArgumentException("Coverage end must be later than its start.", nameof(endsAt));
        }

        LessonOccurrence[] materializedLessons = lessons.ToArray();
        if (materializedLessons.Any(lesson => !lesson.TeacherIds.Contains(teacherId)))
        {
            throw new ArgumentException(
                "Every lesson in teacher coverage must reference that teacher.",
                nameof(lessons));
        }

        TeacherId = teacherId;
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        Lessons = materializedLessons;
    }

    public Guid TeacherId { get; }

    public DateTimeOffset StartsAtUtc { get; }

    public DateTimeOffset EndsAtUtc { get; }

    public IReadOnlyList<LessonOccurrence> Lessons { get; }

    public bool Covers(Guid teacherId, DateTimeOffset instant)
    {
        DateTimeOffset instantUtc = instant.ToUniversalTime();
        return TeacherId == teacherId &&
               StartsAtUtc <= instantUtc &&
               instantUtc < EndsAtUtc;
    }
}
