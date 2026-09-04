namespace UniversitySchedule.Mobile.Core.Scheduling;

public sealed record OfflineScheduleReadiness(
    bool IsReady,
    string? GroupName,
    int LessonCount,
    DateTimeOffset? UpdatedAtUtc);

public sealed record OfflineSchedulePreparationResult(
    OfflineScheduleReadiness Readiness,
    bool DownloadedFromNetwork);
