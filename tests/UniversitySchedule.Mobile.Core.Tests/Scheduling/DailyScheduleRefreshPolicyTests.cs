using UniversitySchedule.Mobile.Core.Scheduling;

namespace UniversitySchedule.Mobile.Core.Tests.Scheduling;

public sealed class DailyScheduleRefreshPolicyTests
{
    private static readonly TimeZoneInfo MoscowTimeZone = TimeZoneInfo.CreateCustomTimeZone(
        "UTC+03:00-test",
        TimeSpan.FromHours(3),
        "UTC+03:00-test",
        "UTC+03:00-test");

    [Fact]
    public void BeforeFourAm_RefreshIsNotDue()
    {
        var nowUtc = new DateTimeOffset(2026, 9, 2, 0, 59, 0, TimeSpan.Zero);

        bool result = DailyScheduleRefreshPolicy.IsDue(nowUtc, null, MoscowTimeZone);

        Assert.False(result);
    }

    [Fact]
    public void AtFourAm_RefreshIsDueWhenTodayHasNotBeenUpdated()
    {
        var nowUtc = new DateTimeOffset(2026, 9, 2, 1, 0, 0, TimeSpan.Zero);
        var yesterdayUtc = new DateTimeOffset(2026, 9, 1, 18, 0, 0, TimeSpan.Zero);

        bool result = DailyScheduleRefreshPolicy.IsDue(nowUtc, yesterdayUtc, MoscowTimeZone);

        Assert.True(result);
    }

    [Fact]
    public void AfterSuccessfulRefresh_AnotherRefreshIsNotDueThatDay()
    {
        var nowUtc = new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);
        var refreshedUtc = new DateTimeOffset(2026, 9, 2, 1, 5, 0, TimeSpan.Zero);

        bool result = DailyScheduleRefreshPolicy.IsDue(nowUtc, refreshedUtc, MoscowTimeZone);

        Assert.False(result);
    }

    [Fact]
    public void FailedDueRefresh_IsRetriedInFifteenMinutes()
    {
        var nowUtc = new DateTimeOffset(2026, 9, 2, 5, 0, 0, TimeSpan.Zero);

        TimeSpan result = DailyScheduleRefreshPolicy.GetDelayUntilNextCheck(
            nowUtc,
            lastNetworkRefreshUtc: null,
            MoscowTimeZone,
            hasProfile: true);

        Assert.Equal(TimeSpan.FromMinutes(15), result);
    }

    [Fact]
    public void BeforeFourAm_NextCheckIsScheduledForFourAmLocalTime()
    {
        var nowUtc = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);

        TimeSpan result = DailyScheduleRefreshPolicy.GetDelayUntilNextCheck(
            nowUtc,
            lastNetworkRefreshUtc: null,
            MoscowTimeZone,
            hasProfile: true);

        Assert.Equal(TimeSpan.FromHours(1), result);
    }
}
