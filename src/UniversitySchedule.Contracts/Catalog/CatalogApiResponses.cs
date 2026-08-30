namespace UniversitySchedule.Contracts.Catalog;

public sealed record ReferenceCatalogStatusResponse(
    int SchemaVersion,
    DateTimeOffset SourceGeneratedAtUtc,
    DateTimeOffset ImportedAtUtc,
    string ContentHash,
    ReferenceCatalogStatistics Statistics);

public sealed record CatalogGroupSearchItem(
    Guid Id,
    Guid DirectionId,
    Guid InstituteId,
    string InstituteName,
    string DirectionName,
    string GroupName,
    int CourseNumber);
