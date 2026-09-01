namespace UniversitySchedule.Domain.Scheduling;

public sealed record TeacherTeachingState(
    TeacherTeachingStatus Status,
    string? SubjectName,
    string? Classroom,
    string? Building,
    DateTimeOffset? EndsAtUtc)
{
    public static TeacherTeachingState NotTeaching { get; } = new(
        TeacherTeachingStatus.NotTeaching,
        null,
        null,
        null,
        null);
}

public enum TeacherTeachingStatus
{
    NotTeaching = 0,
    Teaching = 1,
    ConflictingScheduleData = 2,
}
