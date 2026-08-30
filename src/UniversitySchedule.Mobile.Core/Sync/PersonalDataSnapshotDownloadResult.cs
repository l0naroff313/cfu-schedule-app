using UniversitySchedule.Contracts.PersonalData;

namespace UniversitySchedule.Mobile.Core.Sync;

public enum PersonalDataSnapshotDownloadOutcome
{
    Succeeded = 0,
    NotConfigured = 1,
    RetryableFailure = 2,
    PermanentFailure = 3,
}

public sealed record PersonalDataSnapshotDownloadResult(
    PersonalDataSnapshotDownloadOutcome Outcome,
    int RequestCount,
    PersonalDataSnapshotResponse? Snapshot = null,
    string? ErrorCode = null);
