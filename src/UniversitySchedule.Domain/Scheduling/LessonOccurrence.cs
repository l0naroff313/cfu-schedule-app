namespace UniversitySchedule.Domain.Scheduling;

public sealed class LessonOccurrence
{
    public LessonOccurrence(
        Guid id,
        string subjectName,
        int pairNumber,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        LessonStatus status = LessonStatus.Regular,
        IEnumerable<Guid>? teacherIds = null,
        string? classroom = null,
        string? building = null,
        Guid? logicalLessonId = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Lesson identifier cannot be empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(subjectName);

        if (pairNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pairNumber), "Pair number must be positive.");
        }

        DateTimeOffset startsAtUtc = startsAt.ToUniversalTime();
        DateTimeOffset endsAtUtc = endsAt.ToUniversalTime();

        if (endsAtUtc <= startsAtUtc)
        {
            throw new ArgumentException("Lesson end must be later than its start.", nameof(endsAt));
        }

        Id = id;
        SubjectName = subjectName.Trim();
        PairNumber = pairNumber;
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        Status = status;
        TeacherIds = (teacherIds ?? [])
            .Distinct()
            .ToArray();

        if (TeacherIds.Any(teacherId => teacherId == Guid.Empty))
        {
            throw new ArgumentException("Teacher identifiers cannot be empty.", nameof(teacherIds));
        }

        if (logicalLessonId == Guid.Empty)
        {
            throw new ArgumentException("Logical lesson identifier cannot be empty.", nameof(logicalLessonId));
        }

        Classroom = NormalizeOptional(classroom);
        Building = NormalizeOptional(building);
        LogicalLessonId = logicalLessonId ?? id;
    }

    public Guid Id { get; }

    public string SubjectName { get; }

    public int PairNumber { get; }

    public DateTimeOffset StartsAtUtc { get; }

    public DateTimeOffset EndsAtUtc { get; }

    public LessonStatus Status { get; }

    public IReadOnlyList<Guid> TeacherIds { get; }

    public string? Classroom { get; }

    public string? Building { get; }

    public Guid LogicalLessonId { get; }

    public bool IsCancelled => Status == LessonStatus.Cancelled;

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
