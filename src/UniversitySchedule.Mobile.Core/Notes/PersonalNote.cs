namespace UniversitySchedule.Mobile.Core.Notes;

public sealed record PersonalNote(
    Guid Id,
    Guid? LessonId,
    string Text,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
