namespace UniversitySchedule.ScheduleImporter;

public interface IReferenceCatalogFailureSink
{
    Task WriteFailureAsync(
        DateTimeOffset startedAtUtc,
        Exception exception,
        CancellationToken cancellationToken);
}
