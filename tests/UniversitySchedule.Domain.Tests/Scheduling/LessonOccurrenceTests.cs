using UniversitySchedule.Domain.Scheduling;

namespace UniversitySchedule.Domain.Tests.Scheduling;

public sealed class LessonOccurrenceTests
{
    [Fact]
    public void Constructor_NormalizesTimesToUtc()
    {
        var lesson = new LessonOccurrence(
            Guid.NewGuid(),
            "Математика",
            1,
            new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.FromHours(3)),
            new DateTimeOffset(2026, 9, 1, 9, 30, 0, TimeSpan.FromHours(3)));

        Assert.Equal(TimeSpan.Zero, lesson.StartsAtUtc.Offset);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 5, 0, 0, TimeSpan.Zero), lesson.StartsAtUtc);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveDuration()
    {
        DateTimeOffset instant = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentException>(() => new LessonOccurrence(
            Guid.NewGuid(),
            "Математика",
            1,
            instant,
            instant));
    }
}
