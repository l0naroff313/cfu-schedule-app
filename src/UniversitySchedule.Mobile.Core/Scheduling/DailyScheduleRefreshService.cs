namespace UniversitySchedule.Mobile.Core.Scheduling;

public sealed class DailyScheduleRefreshService(
    ScheduleSession scheduleSession,
    TimeProvider timeProvider) : IDisposable
{
    private readonly ScheduleSession _scheduleSession = scheduleSession
        ?? throw new ArgumentNullException(nameof(scheduleSession));
    private readonly TimeProvider _timeProvider = timeProvider
        ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly object _lifecycleLock = new();
    private CancellationTokenSource? _loopCancellation;
    private Task? _loopTask;

    public event EventHandler? RefreshAttempted;

    public void Start()
    {
        lock (_lifecycleLock)
        {
            if (_loopTask is { IsCompleted: false })
            {
                return;
            }

            _loopCancellation?.Dispose();
            _loopCancellation = new CancellationTokenSource();
            _loopTask = RunAsync(_loopCancellation.Token);
        }
    }

    public async Task<bool> CheckNowAsync(CancellationToken cancellationToken = default)
    {
        if (_scheduleSession.Profile is null || !IsDue())
        {
            return false;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (_scheduleSession.Profile is null || !IsDue())
            {
                return false;
            }

            await _scheduleSession.RefreshAsync(cancellationToken);
            RefreshAttempted?.Invoke(this, EventArgs.Empty);
            return true;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            _loopCancellation?.Cancel();
            _loopCancellation?.Dispose();
            _loopCancellation = null;
            _loopTask = null;
        }

        _refreshLock.Dispose();
    }

    private bool IsDue() => DailyScheduleRefreshPolicy.IsDue(
        _timeProvider.GetUtcNow(),
        _scheduleSession.LastNetworkRefreshAtUtc,
        _timeProvider.LocalTimeZone);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await CheckNowAsync(cancellationToken);
            }
            catch (Exception exception) when (
                exception is HttpRequestException or InvalidOperationException or TaskCanceledException &&
                !cancellationToken.IsCancellationRequested)
            {
                RefreshAttempted?.Invoke(this, EventArgs.Empty);
            }

            TimeSpan delay = DailyScheduleRefreshPolicy.GetDelayUntilNextCheck(
                _timeProvider.GetUtcNow(),
                _scheduleSession.LastNetworkRefreshAtUtc,
                _timeProvider.LocalTimeZone,
                _scheduleSession.Profile is not null);

            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
