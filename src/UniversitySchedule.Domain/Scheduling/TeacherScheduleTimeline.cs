namespace UniversitySchedule.Domain.Scheduling;

public static class TeacherScheduleTimeline
{
    public static TeacherTeachingState ResolveCurrent(
        IEnumerable<LessonOccurrence> lessons,
        Guid teacherId,
        DateTimeOffset instant)
    {
        ArgumentNullException.ThrowIfNull(lessons);

        if (teacherId == Guid.Empty)
        {
            throw new ArgumentException("Teacher identifier cannot be empty.", nameof(teacherId));
        }

        DateTimeOffset instantUtc = instant.ToUniversalTime();
        LessonOccurrence[] currentLessons = lessons
            .Where(lesson =>
                !lesson.IsCancelled &&
                lesson.TeacherIds.Contains(teacherId) &&
                lesson.StartsAtUtc <= instantUtc &&
                lesson.EndsAtUtc > instantUtc)
            .OrderBy(lesson => lesson.StartsAtUtc)
            .ThenBy(lesson => lesson.PairNumber)
            .ToArray();

        if (currentLessons.Length == 0)
        {
            return TeacherTeachingState.NotTeaching;
        }

        IGrouping<Guid, LessonOccurrence>[] logicalLessons = currentLessons
            .GroupBy(lesson => lesson.LogicalLessonId)
            .ToArray();

        if (logicalLessons.Length > 1)
        {
            return CreateConflict();
        }

        LessonOccurrence[] logicalLessonRows = logicalLessons[0].ToArray();
        string[] subjects = DistinctKnownValues(logicalLessonRows.Select(lesson => lesson.SubjectName));
        string[] classrooms = DistinctKnownValues(logicalLessonRows.Select(lesson => lesson.Classroom));
        string[] buildings = DistinctKnownValues(logicalLessonRows.Select(lesson => lesson.Building));
        DateTimeOffset[] starts = logicalLessonRows.Select(lesson => lesson.StartsAtUtc).Distinct().ToArray();
        DateTimeOffset[] ends = logicalLessonRows.Select(lesson => lesson.EndsAtUtc).Distinct().ToArray();

        if (subjects.Length != 1 ||
            classrooms.Length > 1 ||
            buildings.Length > 1 ||
            starts.Length != 1 ||
            ends.Length != 1)
        {
            return CreateConflict();
        }

        LessonOccurrence primaryLesson = logicalLessonRows[0];

        return new TeacherTeachingState(
            TeacherTeachingStatus.Teaching,
            subjects[0],
            classrooms.SingleOrDefault(),
            buildings.SingleOrDefault(),
            primaryLesson.EndsAtUtc);
    }

    private static TeacherTeachingState CreateConflict()
    {
        return new TeacherTeachingState(
            TeacherTeachingStatus.ConflictingScheduleData,
            null,
            null,
            null,
            null);
    }

    private static string[] DistinctKnownValues(IEnumerable<string?> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
