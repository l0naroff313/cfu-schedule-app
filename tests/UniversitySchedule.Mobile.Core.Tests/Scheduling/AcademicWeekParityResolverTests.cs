using UniversitySchedule.Contracts.Catalog;
using UniversitySchedule.Mobile.Core.Scheduling;

namespace UniversitySchedule.Mobile.Core.Tests.Scheduling;

public sealed class AcademicWeekParityResolverTests
{
    private static readonly ReferenceScheduleCalendar Calendar = new(
        [],
        ["2026-09-07", "2026-09-21"],
        ["2026-09-14", "2026-09-28"]);

    [Theory]
    [InlineData(2026, 9, 1, AcademicWeekParity.Odd)]
    [InlineData(2026, 9, 7, AcademicWeekParity.Even)]
    [InlineData(2026, 9, 13, AcademicWeekParity.Even)]
    [InlineData(2026, 9, 18, AcademicWeekParity.Odd)]
    [InlineData(2026, 10, 5, AcademicWeekParity.Even)]
    public void Resolve_UsesTheUniversityAcademicCalendar(
        int year,
        int month,
        int day,
        AcademicWeekParity expected)
    {
        AcademicWeekParity actual = AcademicWeekParityResolver.Resolve(
            new DateOnly(year, month, day),
            Calendar);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Resolve_ReturnsUnknownWithoutCalendarAnchors()
    {
        var emptyCalendar = new ReferenceScheduleCalendar([], [], []);

        AcademicWeekParity actual = AcademicWeekParityResolver.Resolve(
            new DateOnly(2026, 9, 7),
            emptyCalendar);

        Assert.Equal(AcademicWeekParity.Unknown, actual);
        Assert.Empty(AcademicWeekParityResolver.Format(actual));
    }

    [Theory]
    [InlineData(AcademicWeekParity.Even, "Чётная неделя")]
    [InlineData(AcademicWeekParity.Odd, "Нечётная неделя")]
    public void Format_ReturnsUserFacingRussianText(
        AcademicWeekParity parity,
        string expected)
    {
        Assert.Equal(expected, AcademicWeekParityResolver.Format(parity));
    }
}
