namespace UniversitySchedule.Mobile.Core.Scheduling;

public static class DailyScheduleRefreshPolicy
{
    public static readonly TimeSpan RefreshTime = TimeSpan.FromHours(4);

    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(15);

    public static bool IsDue(
        DateTimeOffset nowUtc,
        DateTimeOffset? lastNetworkRefreshUtc,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        DateTime localNow = TimeZoneInfo.ConvertTime(nowUtc, timeZone).DateTime;
        if (localNow.TimeOfDay < RefreshTime)
        {
            return false;
        }

        DateTimeOffset cutoffUtc = ToUtc(localNow.Date.Add(RefreshTime), timeZone);
        return lastNetworkRefreshUtc is null || lastNetworkRefreshUtc.Value < cutoffUtc;
    }

    public static TimeSpan GetDelayUntilNextCheck(
        DateTimeOffset nowUtc,
        DateTimeOffset? lastNetworkRefreshUtc,
        TimeZoneInfo timeZone,
        bool hasProfile)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        if (hasProfile && IsDue(nowUtc, lastNetworkRefreshUtc, timeZone))
        {
            return RetryDelay;
        }

        DateTime localNow = TimeZoneInfo.ConvertTime(nowUtc, timeZone).DateTime;
        DateTime nextLocal = localNow.TimeOfDay < RefreshTime
            ? localNow.Date.Add(RefreshTime)
            : localNow.Date.AddDays(1).Add(RefreshTime);
        TimeSpan delay = ToUtc(nextLocal, timeZone) - nowUtc;
        return delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1);
    }

    private static DateTimeOffset ToUtc(DateTime localDateTime, TimeZoneInfo timeZone)
    {
        DateTime unspecified = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddHours(1);
        }

        return new DateTimeOffset(unspecified, timeZone.GetUtcOffset(unspecified)).ToUniversalTime();
    }
}
