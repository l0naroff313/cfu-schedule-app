using Microsoft.EntityFrameworkCore;
using UniversitySchedule.Application.Catalog;
using UniversitySchedule.Domain.Catalog;
using UniversitySchedule.Infrastructure.Persistence;

namespace UniversitySchedule.Infrastructure.Catalog;

public sealed class EfReferenceCatalogRepository(AppDbContext dbContext)
    : IReferenceCatalogRepository
{
    public Task<ReferenceCatalogDocument?> GetCurrentAsync(
        CancellationToken cancellationToken = default) =>
        dbContext.ReferenceCatalogDocuments.SingleOrDefaultAsync(
            document => document.Id == ReferenceCatalogDocument.CurrentDocumentId,
            cancellationToken);

    public void Add(ReferenceCatalogDocument document) =>
        dbContext.ReferenceCatalogDocuments.Add(document);

    public void AddImportLog(ReferenceCatalogImportLog importLog) =>
        dbContext.ReferenceCatalogImportLogs.Add(importLog);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
