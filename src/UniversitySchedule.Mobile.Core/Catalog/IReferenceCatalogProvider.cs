using UniversitySchedule.Contracts.Catalog;

namespace UniversitySchedule.Mobile.Core.Catalog;

public interface IReferenceCatalogProvider
{
    Task<ReferenceCatalogSnapshot?> LoadAsync(CancellationToken cancellationToken = default);
}
