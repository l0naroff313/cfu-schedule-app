using UniversitySchedule.ScheduleImporter.Sources;

namespace UniversitySchedule.Importer.Tests.Sources;

public sealed class SourceDocumentVersionTests
{
    [Fact]
    public void Matches_UsesExactETagWhenBothVersionsHaveOne()
    {
        var previous = new SourceDocumentVersion("\"abc\"", null, null);
        var current = new SourceDocumentVersion("\"abc\"", DateTimeOffset.UtcNow, 100);

        Assert.True(previous.Matches(current));
    }

    [Fact]
    public void Matches_DetectsChangedETag()
    {
        var previous = new SourceDocumentVersion("\"abc\"", null, null);
        var current = new SourceDocumentVersion("\"def\"", null, null);

        Assert.False(previous.Matches(current));
    }

    [Fact]
    public void Matches_FallsBackToTimestampAndLength()
    {
        DateTimeOffset modifiedAt = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
        var previous = new SourceDocumentVersion(null, modifiedAt, 1_024);
        var current = new SourceDocumentVersion(null, modifiedAt.ToOffset(TimeSpan.FromHours(3)), 1_024);

        Assert.True(previous.Matches(current));
    }
}
