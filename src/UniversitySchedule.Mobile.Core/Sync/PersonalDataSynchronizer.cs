namespace UniversitySchedule.Mobile.Core.Sync;

public sealed record PersonalDataSyncRunResult(
    int SynchronizedCount,
    int PendingCount,
    int ConflictCount,
    int FailedCount,
    bool IsConfigured);

public sealed class PersonalDataSynchronizer(
    PersonalDataSyncQueue queue,
    UniversityScheduleApiClient apiClient)
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<PersonalDataSyncRunResult> SynchronizeAsync(
        CancellationToken cancellationToken = default)
    {
        if (!apiClient.IsEnabled)
        {
            IReadOnlyList<PersonalDataSyncOperation> queued = await queue.GetPendingAsync(cancellationToken);
            return BuildResult(0, queued, false);
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            int synchronizedCount = 0;
            IReadOnlyList<PersonalDataSyncOperation> operations = await queue.GetPendingAsync(cancellationToken);
            var blockedEntities = new HashSet<(PersonalDataSyncEntityKind Kind, Guid Id)>();
            foreach (PersonalDataSyncOperation operation in operations)
            {
                var entityKey = (operation.EntityKind, operation.EntityId);
                if (operation.State is PersonalDataSyncOperationState.Conflict or PersonalDataSyncOperationState.Failed)
                {
                    blockedEntities.Add(entityKey);
                    continue;
                }

                if (blockedEntities.Contains(entityKey))
                {
                    continue;
                }

                PersonalDataPushResult pushResult = await apiClient.PushAsync(operation, cancellationToken);
                switch (pushResult.Outcome)
                {
                    case PersonalDataPushOutcome.Succeeded:
                        await queue.RemoveAsync(operation.MutationId, cancellationToken);
                        synchronizedCount++;
                        break;
                    case PersonalDataPushOutcome.Conflict:
                        await queue.MarkConflictAsync(
                            operation.MutationId,
                            pushResult.ErrorCode ?? "server_conflict",
                            pushResult.ServerStateJson,
                            cancellationToken);
                        blockedEntities.Add(entityKey);
                        break;
                    case PersonalDataPushOutcome.PermanentFailure:
                        await queue.MarkFailedAsync(
                            operation.MutationId,
                            pushResult.ErrorCode ?? "permanent_failure",
                            cancellationToken);
                        blockedEntities.Add(entityKey);
                        break;
                    case PersonalDataPushOutcome.RetryableFailure:
                        await queue.RecordRetryAsync(
                            operation.MutationId,
                            pushResult.ErrorCode ?? "retryable_failure",
                            cancellationToken);
                        return BuildResult(
                            synchronizedCount,
                            await queue.GetPendingAsync(cancellationToken),
                            true);
                    case PersonalDataPushOutcome.NotConfigured:
                        return BuildResult(synchronizedCount, operations, false);
                    default:
                        throw new InvalidOperationException(
                            $"Unsupported personal data push outcome: {pushResult.Outcome}.");
                }
            }

            return BuildResult(
                synchronizedCount,
                await queue.GetPendingAsync(cancellationToken),
                true);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static PersonalDataSyncRunResult BuildResult(
        int synchronizedCount,
        IReadOnlyCollection<PersonalDataSyncOperation> operations,
        bool isConfigured) =>
        new(
            synchronizedCount,
            operations.Count(operation => operation.State == PersonalDataSyncOperationState.Pending),
            operations.Count(operation => operation.State == PersonalDataSyncOperationState.Conflict),
            operations.Count(operation => operation.State == PersonalDataSyncOperationState.Failed),
            isConfigured);
}
