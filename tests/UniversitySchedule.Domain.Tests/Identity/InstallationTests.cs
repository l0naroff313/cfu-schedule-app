using UniversitySchedule.Domain.Identity;

namespace UniversitySchedule.Domain.Tests.Identity;

public sealed class InstallationTests
{
    [Fact]
    public void Register_CopiesHashAndUsesUtcTimestamps()
    {
        byte[] hash = Enumerable.Range(0, 32).Select(index => (byte)index).ToArray();
        var createdAt = new DateTimeOffset(2026, 8, 30, 10, 0, 0, TimeSpan.FromHours(3));

        Installation installation = Installation.Register(
            Guid.NewGuid(),
            hash,
            "android",
            "1.0.0",
            createdAt);
        hash[0] = 255;

        Assert.Equal(0, installation.SecretHash[0]);
        Assert.Equal(TimeSpan.Zero, installation.CreatedAtUtc.Offset);
        Assert.Equal(installation.CreatedAtUtc, installation.LastSeenAtUtc);
    }

    [Fact]
    public void RecordSeen_DoesNotMoveTimestampBackwards()
    {
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        Installation installation = Installation.Register(
            Guid.NewGuid(),
            new byte[32],
            "android",
            "1.0.0",
            createdAt);

        installation.RecordSeen("ios", "1.1.0", createdAt.AddMinutes(-1));

        Assert.Equal(createdAt, installation.LastSeenAtUtc);
        Assert.Equal("ios", installation.Platform);
        Assert.Equal("1.1.0", installation.AppVersion);
    }
}
