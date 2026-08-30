namespace UniversitySchedule.Mobile.Core.Sync;

public sealed record PersonalDataSyncRunResult(
    int SynchronizedCount,
    int PendingCount,
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
            return new PersonalDataSyncRunResult(0, 0, false);
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            int synchronizedCount = 0;
            while (true)
            {
                IReadOnlyList<PersonalDataSyncOperation> pending = await queue.GetPendingAsync(cancellationToken);
                if (pending.Count == 0)
                {
                    return new PersonalDataSyncRunResult(synchronizedCount, 0, true);
                }

                PersonalDataSyncOperation operation = pending[0];
                if (!await apiClient.TryPushAsync(operation, cancellationToken))
                {
                    return new PersonalDataSyncRunResult(synchronizedCount, pending.Count, true);
                }

                await queue.RemoveAsync(operation.MutationId, cancellationToken);
                synchronizedCount++;
            }
        }
        finally
        {
            _lock.Release();
        }
    }
}
