using UniversitySchedule.Mobile.Core.Scheduling;

namespace UniversitySchedule.Mobile.Core.Tests.Scheduling;

public sealed class DailyScheduleRefreshPolicyTests
{
    [Theory]
    [InlineData("2026-09-11T02:59:59Z", false)]
    [InlineData("2026-09-11T03:00:00Z", true)]
    [InlineData("2026-09-11T06:00:00+03:00", true)]
    [InlineData("2026-09-11T12:00:00+09:00", true)]
    [InlineData("2026-09-10T20:00:00-07:00", true)]
    public void RefreshBoundary_IsSixAmMoscow_RegardlessOfDeviceOffset(string instant, bool due)
        => Assert.Equal(due, DailyScheduleRefreshPolicy.IsDue(DateTimeOffset.Parse(instant), null));

    [Fact]
    public void SuccessfulRefresh_AfterCutoff_IsNotRepeated()
        => Assert.False(DailyScheduleRefreshPolicy.IsDue(DateTimeOffset.Parse("2026-09-11T09:00Z"),
            DateTimeOffset.Parse("2026-09-11T03:00Z")));

    [Fact]
    public void RefreshBeforeCutoff_DoesNotReplaceDailyRefresh()
        => Assert.True(DailyScheduleRefreshPolicy.IsDue(DateTimeOffset.Parse("2026-09-11T03:00Z"),
            DateTimeOffset.Parse("2026-09-11T02:59Z")));

    [Fact]
    public void FailedRefresh_RetriesInFifteenMinutes()
        => Assert.Equal(TimeSpan.FromMinutes(15), DailyScheduleRefreshPolicy.GetDelayUntilNextCheck(
            DateTimeOffset.Parse("2026-09-11T03:00Z"), null, true));

    [Fact]
    public void BeforeCutoff_WaitsUntilSixMoscow()
        => Assert.Equal(TimeSpan.FromHours(1), DailyScheduleRefreshPolicy.GetDelayUntilNextCheck(
            DateTimeOffset.Parse("2026-09-11T02:00Z"), null, true));

    [Fact]
    public void SuccessfulRefresh_NextCheckIsTomorrowAtSixMoscow()
        => Assert.Equal(TimeSpan.FromHours(23), DailyScheduleRefreshPolicy.GetDelayUntilNextCheck(
            DateTimeOffset.Parse("2026-09-11T04:00Z"), DateTimeOffset.Parse("2026-09-11T03:00Z"), true));
}
