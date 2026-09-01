using UniversitySchedule.Application.Catalog;
using UniversitySchedule.Domain.Catalog;

namespace UniversitySchedule.Application.Tests.Catalog;

public sealed class ReferenceCatalogPersistenceServiceTests
{
    [Fact]
    public async Task PublishAsync_DoesNotReplaceNewerKnownGoodCatalog()
    {
        DateTimeOffset now = new(2026, 8, 30, 6, 0, 0, TimeSpan.Zero);
        var repository = new InMemoryRepository();
        var service = new ReferenceCatalogPersistenceService(repository, new FixedTimeProvider(now));
        ReferenceCatalogPublishCommand current = CreateCommand("{\"version\":2}", now);
        ReferenceCatalogPublishCommand stale = CreateCommand("{\"version\":1}", now.AddMinutes(-1));

        ReferenceCatalogPublishResult first = await service.PublishAsync(current);
        ReferenceCatalogPublishResult second = await service.PublishAsync(stale);

        Assert.Equal(ReferenceCatalogPublishStatus.Published, first.Status);
        Assert.Equal(ReferenceCatalogPublishStatus.RejectedAsStale, second.Status);
        Assert.Equal("{\"version\":2}", repository.Current!.PayloadJson);
        Assert.Equal(2, repository.Logs.Count);
        Assert.Equal(ReferenceCatalogImportStatus.RejectedAsStale, repository.Logs[^1].Status);
    }

    [Fact]
    public async Task PublishAsync_SameContentAdvancesSourceTimestampAndIsLogged()
    {
        DateTimeOffset now = new(2026, 8, 30, 6, 0, 0, TimeSpan.Zero);
        var repository = new InMemoryRepository();
        var service = new ReferenceCatalogPersistenceService(repository, new FixedTimeProvider(now));
        ReferenceCatalogPublishCommand command = CreateCommand("{\"version\":1}", now);

        await service.PublishAsync(command);
        ReferenceCatalogPublishCommand later = command with { SourceGeneratedAtUtc = now.AddMinutes(1) };
        ReferenceCatalogPublishResult repeated = await service.PublishAsync(later);

        Assert.Equal(ReferenceCatalogPublishStatus.Unchanged, repeated.Status);
        Assert.Equal(later.SourceGeneratedAtUtc, repository.Current!.SourceGeneratedAtUtc);
        Assert.Equal(2, repository.SaveCount);
    }

    private static ReferenceCatalogPublishCommand CreateCommand(
        string payload,
        DateTimeOffset generatedAtUtc) =>
        new(payload, 1, generatedAtUtc, 115, 423, 2_345);

    private sealed class InMemoryRepository : IReferenceCatalogRepository
    {
        public ReferenceCatalogDocument? Current { get; private set; }

        public List<ReferenceCatalogImportLog> Logs { get; } = [];

        public int SaveCount { get; private set; }

        public Task<ReferenceCatalogDocument?> GetCurrentAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);

        public void Add(ReferenceCatalogDocument document) => Current = document;

        public void AddImportLog(ReferenceCatalogImportLog importLog) => Logs.Add(importLog);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
