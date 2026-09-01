using UniversitySchedule.Contracts.Catalog;

namespace UniversitySchedule.Mobile.Core.Cfu;

public static class CfuStableId
{
    public static Guid Create(params string?[] parts)
    {
        return CatalogStableId.Create(parts);
    }
}
