using System.ComponentModel.DataAnnotations;

namespace UniversitySchedule.Contracts.PersonalData;

public enum AssignmentSyncStatus
{
    New = 0,
    InProgress = 1,
    Completed = 2,
}

public sealed record SyncAssignmentRequest(
    [Required] Guid MutationId,
    Guid? LessonId,
    [MaxLength(200)] string Subject,
    [Required, StringLength(8_000, MinimumLength = 1)] string Text,
    DateTimeOffset? DeadlineUtc,
    AssignmentSyncStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
