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
    DateTimeOffset UpdatedAtUtc,
    int? ReminderMinutesBefore = null)
{
    public bool HasReminder => ReminderMinutesBefore is > 0 && DeadlineUtc.HasValue;

    public string ReminderText => ReminderMinutesBefore switch
    {
        5 => "За 5 минут",
        10 => "За 10 минут",
        15 => "За 15 минут",
        30 => "За 30 минут",
        60 => "За 1 час",
        1440 => "За 1 день",
        _ => "Без напоминания",
    };
}
