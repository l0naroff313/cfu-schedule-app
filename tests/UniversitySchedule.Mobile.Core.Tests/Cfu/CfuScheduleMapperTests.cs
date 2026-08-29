using UniversitySchedule.Mobile.Core.Cfu;

namespace UniversitySchedule.Mobile.Core.Tests.Cfu;

public sealed class CfuScheduleMapperTests
{
    [Fact]
    public void GroupSchedule_ExpandsParityAndFiltersSubgroup()
    {
        CfuScheduleIndexDocument index = CreateIndex();
        var schedule = new CfuGroupScheduleDocument
        {
            Code = "МАТ-б-о-251",
            Lessons =
            [
                CreateLesson("Общий предмет", subgroup: 0, parity: "обе"),
                CreateLesson("Первая подгруппа", subgroup: 1, parity: "чёт"),
                CreateLesson("Вторая подгруппа", subgroup: 2, parity: "чёт"),
            ],
        };

        Contracts.Schedule.ScheduleSnapshot result = CfuScheduleMapper.MapGroup(index, schedule, subgroup: 1);

        Assert.Equal(3, result.Lessons.Count);
        Assert.DoesNotContain(result.Lessons, lesson => lesson.Subject == "Вторая подгруппа");
        Assert.Equal(
            new DateOnly(2026, 9, 7),
            result.Lessons.First(lesson => lesson.Subject == "Первая подгруппа").Date);
    }

    [Fact]
    public void GroupSchedule_UsesMoscowOffsetAndOfficialBellTimes()
    {
        Contracts.Schedule.ScheduleSnapshot result = CfuScheduleMapper.MapGroup(
            CreateIndex(),
            new CfuGroupScheduleDocument
            {
                Code = "МАТ-б-о-251",
                Lessons = [CreateLesson("Алгоритмы", subgroup: 0, parity: "чёт")],
            });

        Contracts.Schedule.ScheduleLesson lesson = Assert.Single(result.Lessons);
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 5, 0, 0, TimeSpan.Zero), lesson.StartsAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 6, 30, 0, TimeSpan.Zero), lesson.EndsAtUtc);
        Assert.Equal("305", lesson.Classroom);
        Assert.Equal("корпус А", lesson.Building);
    }

    [Fact]
    public void StableId_IsRepeatableAndNormalizesWhitespaceAndYo()
    {
        Guid first = CfuStableId.Create("teacher", "Алёна   Иванова");
        Guid second = CfuStableId.Create("TEACHER", " алена иванова ");

        Assert.Equal(first, second);
        Assert.NotEqual(Guid.Empty, first);
    }

    private static CfuScheduleIndexDocument CreateIndex()
    {
        return new CfuScheduleIndexDocument
        {
            Bells = [new CfuBellDocument { PairNumber = 1, StartsAt = "08:00", EndsAt = "09:30" }],
            Weeks = new CfuWeeksDocument
            {
                EvenWeekMondays = ["2026-09-07"],
                OddWeekMondays = ["2026-09-14"],
            },
            Tree = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>>
            {
                ["Физико-технический институт"] =
                    new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>
                    {
                        ["01.03.01 Математика"] = new Dictionary<string, IReadOnlyList<string>>
                        {
                            ["2"] = ["МАТ-б-о-251"],
                        },
                    },
            },
        };
    }

    private static CfuLessonDocument CreateLesson(string subject, int subgroup, string parity)
    {
        return new CfuLessonDocument
        {
            GroupCode = "МАТ-б-о-251",
            Subgroup = subgroup,
            Day = 1,
            PairNumber = 1,
            Parity = parity,
            Subject = subject,
            LessonType = "ЛК",
            Teachers = ["Иванова Н. П."],
            Classroom = "305",
            Building = "корпус А",
        };
    }
}
