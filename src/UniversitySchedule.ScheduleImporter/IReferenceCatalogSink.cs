using UniversitySchedule.Contracts.Catalog;

namespace UniversitySchedule.ScheduleImporter;

public interface IReferenceCatalogSink
{
    Task WriteAsync(ReferenceCatalogSnapshot snapshot, CancellationToken cancellationToken);
}
