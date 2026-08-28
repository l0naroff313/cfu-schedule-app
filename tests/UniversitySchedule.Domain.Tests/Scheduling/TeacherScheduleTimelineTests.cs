using UniversitySchedule.Domain.Scheduling;

namespace UniversitySchedule.Domain.Tests.Scheduling;

public sealed class TeacherScheduleTimelineTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 1, 6, 0, 0, TimeSpan.Zero);

    private static readonly Guid TeacherId =
        Guid.Parse("447cfa82-b48a-4dfd-8da8-ec524036e5ae");

    [Fact]
    public void ResolveCurrent_ReturnsPublishedClassroom()
    {
        LessonOccurrence lesson = CreateLesson(classroom: "ауд. 305", building: "корпус А");

        TeacherTeachingState result = TeacherScheduleTimeline.ResolveCurrent(
            [lesson],
            TeacherId,
            Now);

        Assert.Equal(TeacherTeachingStatus.Teaching, result.Status);
        Assert.Equal("ауд. 305", result.Classroom);
        Assert.Equal("корпус А", result.Building);
        Assert.Equal("Математика", result.SubjectName);
    }

    [Fact]
    public void ResolveCurrent_DoesNotInventMissingClassroom()
    {
        LessonOccurrence lesson = CreateLesson();

        TeacherTeachingState result = TeacherScheduleTimeline.ResolveCurrent(
            [lesson],
            TeacherId,
            Now);

        Assert.Equal(TeacherTeachingStatus.Teaching, result.Status);
        Assert.Null(result.Classroom);
        Assert.Null(result.Building);
    }

    [Fact]
    public void ResolveCurrent_ReturnsConflictForDifferentPublishedRooms()
    {
        LessonOccurrence first = CreateLesson(classroom: "305");
        LessonOccurrence second = CreateLesson(classroom: "410");

        TeacherTeachingState result = TeacherScheduleTimeline.ResolveCurrent(
            [first, second],
            TeacherId,
            Now);

        Assert.Equal(TeacherTeachingStatus.ConflictingScheduleData, result.Status);
        Assert.Null(result.Classroom);
    }

    [Fact]
    public void ResolveCurrent_MergesDuplicateSubgroupRowsWithSameRoom()
    {
        Guid logicalLessonId = Guid.NewGuid();
        LessonOccurrence first = CreateLesson(
            classroom: "305",
            logicalLessonId: logicalLessonId);
        LessonOccurrence duplicate = CreateLesson(
            classroom: " 305 ",
            logicalLessonId: logicalLessonId);

        TeacherTeachingState result = TeacherScheduleTimeline.ResolveCurrent(
            [first, duplicate],
            TeacherId,
            Now);

        Assert.Equal(TeacherTeachingStatus.Teaching, result.Status);
        Assert.Equal("305", result.Classroom);
    }

    [Fact]
    public void ResolveCurrent_ReturnsConflictForDifferentLessonsInSameRoom()
    {
        LessonOccurrence first = CreateLesson(classroom: "305");
        LessonOccurrence second = new(
            Guid.NewGuid(),
            "Физика",
            1,
            Now.AddMinutes(-30),
            Now.AddMinutes(60),
            teacherIds: [TeacherId],
            classroom: "305");

        TeacherTeachingState result = TeacherScheduleTimeline.ResolveCurrent(
            [first, second],
            TeacherId,
            Now);

        Assert.Equal(TeacherTeachingStatus.ConflictingScheduleData, result.Status);
        Assert.Null(result.SubjectName);
    }

    [Fact]
    public void ResolveCurrent_ReturnsConflictForKnownAndUnknownDifferentLessons()
    {
        LessonOccurrence known = CreateLesson(classroom: "305");
        LessonOccurrence unknown = CreateLesson();

        TeacherTeachingState result = TeacherScheduleTimeline.ResolveCurrent(
            [known, unknown],
            TeacherId,
            Now);

        Assert.Equal(TeacherTeachingStatus.ConflictingScheduleData, result.Status);
        Assert.Null(result.Classroom);
    }

    [Fact]
    public void ResolveCurrent_SkipsCancelledLesson()
    {
        LessonOccurrence lesson = CreateLesson(
            classroom: "305",
            status: LessonStatus.Cancelled);

        TeacherTeachingState result = TeacherScheduleTimeline.ResolveCurrent(
            [lesson],
            TeacherId,
            Now);

        Assert.Equal(TeacherTeachingStatus.NotTeaching, result.Status);
    }

    [Fact]
    public void ResolveCurrent_IncludesExactLessonStart()
    {
        LessonOccurrence lesson = new(
            Guid.NewGuid(),
            "Математика",
            1,
            Now,
            Now.AddMinutes(90),
            teacherIds: [TeacherId],
            classroom: "305");

        TeacherTeachingState result = TeacherScheduleTimeline.ResolveCurrent(
            [lesson],
            TeacherId,
            Now);

        Assert.Equal(TeacherTeachingStatus.Teaching, result.Status);
    }

    [Fact]
    public void ResolveCurrent_ExcludesExactLessonEnd()
    {
        LessonOccurrence lesson = new(
            Guid.NewGuid(),
            "Математика",
            1,
            Now.AddMinutes(-90),
            Now,
            teacherIds: [TeacherId],
            classroom: "305");

        TeacherTeachingState result = TeacherScheduleTimeline.ResolveCurrent(
            [lesson],
            TeacherId,
            Now);

        Assert.Equal(TeacherTeachingStatus.NotTeaching, result.Status);
    }

    [Theory]
    [InlineData(LessonStatus.Rescheduled)]
    [InlineData(LessonStatus.Replaced)]
    public void ResolveCurrent_IncludesActiveChangedLesson(LessonStatus status)
    {
        LessonOccurrence lesson = CreateLesson(classroom: "305", status: status);

        TeacherTeachingState result = TeacherScheduleTimeline.ResolveCurrent(
            [lesson],
            TeacherId,
            Now);

        Assert.Equal(TeacherTeachingStatus.Teaching, result.Status);
    }

    private static LessonOccurrence CreateLesson(
        string? classroom = null,
        string? building = null,
        LessonStatus status = LessonStatus.Regular,
        Guid? logicalLessonId = null)
    {
        return new LessonOccurrence(
            Guid.NewGuid(),
            "Математика",
            1,
            Now.AddMinutes(-30),
            Now.AddMinutes(60),
            status,
            [TeacherId],
            classroom,
            building,
            logicalLessonId);
    }
}
