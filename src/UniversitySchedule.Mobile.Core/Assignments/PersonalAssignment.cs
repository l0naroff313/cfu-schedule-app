namespace UniversitySchedule.Mobile.Core.Assignments;

public enum PersonalAssignmentStatus
{
    New = 0,
    InProgress = 1,
    Completed = 2,
}

public sealed record PersonalAssignment(
    Guid Id,
    Guid? LessonId,
    string Subject,
    string Text,
    DateTimeOffset? DeadlineUtc,
    PersonalAssignmentStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
