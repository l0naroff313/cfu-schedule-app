using System.ComponentModel.DataAnnotations;

namespace UniversitySchedule.Contracts.PersonalData;

public sealed record SyncNoteRequest(
    [Required] Guid MutationId,
    Guid? LessonId,
    [Required, StringLength(8_000, MinimumLength = 1)] string Text,
    [MaxLength(200)] string? Title,
    [MaxLength(200)] string? Subject,
    bool IsPinned,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
