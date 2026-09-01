#if VISUAL_SNAPSHOTS
namespace UniversitySchedule.Mobile.Services;

public sealed class VisualSnapshotTimeProvider : TimeProvider
{
    private static readonly DateTimeOffset SnapshotTime =
        new(2026, 8, 31, 5, 45, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => SnapshotTime;
}
#endif
