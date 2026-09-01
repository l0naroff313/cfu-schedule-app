using UniversitySchedule.Domain.Catalog;

namespace UniversitySchedule.Application.Catalog;

public interface IReferenceCatalogRepository
{
    Task<ReferenceCatalogDocument?> GetCurrentAsync(CancellationToken cancellationToken = default);

    void Add(ReferenceCatalogDocument document);

    void AddImportLog(ReferenceCatalogImportLog importLog);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
