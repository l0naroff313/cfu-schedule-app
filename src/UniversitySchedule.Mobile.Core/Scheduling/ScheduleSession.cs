using System.Text.Json;
using UniversitySchedule.Contracts.Schedule;
using UniversitySchedule.Mobile.Core.Cfu;
using UniversitySchedule.Mobile.Core.Profiles;

namespace UniversitySchedule.Mobile.Core.Scheduling;

public sealed class ScheduleSession(
    AcademicProfileStore profileStore,
    CfuScheduleRepository scheduleRepository,
    TimeProvider? timeProvider = null)
{
    private readonly AcademicProfileStore _profileStore = profileStore
        ?? throw new ArgumentNullException(nameof(profileStore));
    private readonly CfuScheduleRepository _scheduleRepository = scheduleRepository
        ?? throw new ArgumentNullException(nameof(scheduleRepository));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public event EventHandler? Changed;

    public AcademicProfile? Profile { get; private set; }

    public ScheduleSnapshot? Snapshot { get; private set; }

    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public DateTimeOffset? LastNetworkRefreshAtUtc { get; private set; }

    public bool IsFromCache { get; private set; }

    public string? LastError { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            Profile = await _profileStore.GetAsync(cancellationToken);
            if (Profile is not null)
            {
                CfuScheduleLoadResult? cached = await _scheduleRepository.LoadCachedGroupScheduleAsync(
                    Profile.GroupName,
                    GetSubgroupNumber(Profile),
                    cancellationToken);
                if (cached is not null)
                {
                    Apply(cached);
                }

                await TryRefreshAsync(cancellationToken);
            }

            _initialized = true;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task SetProfileAsync(
        AcademicProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        CfuScheduleLoadResult result = await _scheduleRepository.LoadGroupScheduleAsync(
            profile.GroupName,
            GetSubgroupNumber(profile),
            cancellationToken);
        await _profileStore.SaveAsync(profile, cancellationToken);

        Profile = profile;
        Apply(result);
        LastError = null;
        _initialized = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        if (Profile is null)
        {
            return;
        }

        await TryRefreshAsync(cancellationToken);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<OfflineScheduleReadiness> CheckOfflineReadinessAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await InitializeAsync(cancellationToken);
            if (Profile is null)
            {
                return new OfflineScheduleReadiness(false, null, 0, null);
            }

            CfuScheduleLoadResult? cached = await _scheduleRepository.LoadCachedGroupScheduleAsync(
                Profile.GroupName,
                GetSubgroupNumber(Profile),
                cancellationToken);
            return cached is null
                ? new OfflineScheduleReadiness(false, Profile.GroupName, 0, null)
                : new OfflineScheduleReadiness(
                    true,
                    Profile.GroupName,
                    cached.Snapshot.Lessons.Count,
                    cached.UpdatedAtUtc);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            return new OfflineScheduleReadiness(false, Profile?.GroupName, 0, null);
        }
    }

    public async Task<OfflineSchedulePreparationResult> PrepareOfflineAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        if (Profile is null)
        {
            throw new InvalidOperationException("Сначала выберите учебную группу.");
        }

        CfuScheduleLoadResult result = await _scheduleRepository.LoadGroupScheduleAsync(
            Profile.GroupName,
            GetSubgroupNumber(Profile),
            cancellationToken);
        Apply(result);

        OfflineScheduleReadiness readiness = await CheckOfflineReadinessAsync(cancellationToken);
        if (!readiness.IsReady)
        {
            throw new InvalidOperationException("Не удалось проверить сохранённую копию расписания.");
        }

        LastError = null;
        Changed?.Invoke(this, EventArgs.Empty);
        return new OfflineSchedulePreparationResult(readiness, !result.IsFromCache);
    }

    private async Task TryRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            CfuScheduleLoadResult result = await _scheduleRepository.LoadGroupScheduleAsync(
                Profile!.GroupName,
                GetSubgroupNumber(Profile),
                cancellationToken);
            Apply(result);
            LastError = null;
        }
        catch (InvalidOperationException exception) when (Snapshot is not null)
        {
            IsFromCache = true;
            LastError = exception.Message;
        }
    }

    private void Apply(CfuScheduleLoadResult result)
    {
        Snapshot = result.Snapshot;
        UpdatedAtUtc = result.UpdatedAtUtc;
        IsFromCache = result.IsFromCache;
        if (!result.IsFromCache)
        {
            LastNetworkRefreshAtUtc = _timeProvider.GetUtcNow();
        }
    }

    private static int? GetSubgroupNumber(AcademicProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.SubgroupName))
        {
            return null;
        }

        string digits = new(profile.SubgroupName.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out int value) && value > 0 ? value : null;
    }
}
