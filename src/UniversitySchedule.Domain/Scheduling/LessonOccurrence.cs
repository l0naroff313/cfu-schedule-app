namespace UniversitySchedule.Domain.Scheduling;

public sealed class LessonOccurrence
{
    public LessonOccurrence(
        Guid id,
        string subjectName,
        int pairNumber,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        LessonStatus status = LessonStatus.Regular)
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
    }

    public Guid Id { get; }

    public string SubjectName { get; }

    public int PairNumber { get; }

    public DateTimeOffset StartsAtUtc { get; }

    public DateTimeOffset EndsAtUtc { get; }

    public LessonStatus Status { get; }

    public bool IsCancelled => Status == LessonStatus.Cancelled;
}
