namespace UniversitySchedule.Mobile.Core.Sync;

public enum PersonalDataPushOutcome
{
    Succeeded = 0,
    Conflict = 1,
    RetryableFailure = 2,
    PermanentFailure = 3,
    NotConfigured = 4,
}

public sealed record PersonalDataPushResult(
    PersonalDataPushOutcome Outcome,
    int RequestCount,
    string? ErrorCode = null,
    string? ServerStateJson = null);
