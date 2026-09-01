using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using UniversitySchedule.Application.Catalog;
using UniversitySchedule.Contracts.Catalog;
using UniversitySchedule.Infrastructure.Catalog;
using UniversitySchedule.Infrastructure.Persistence;

namespace UniversitySchedule.ScheduleImporter;

public sealed class ReferenceCatalogDatabaseWriter(
    IDbContextFactory<AppDbContext> dbContextFactory,
    TimeProvider timeProvider,
    ILogger<ReferenceCatalogDatabaseWriter> logger)
    : IReferenceCatalogSink, IReferenceCatalogFailureSink
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public async Task WriteAsync(
        ReferenceCatalogSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await using AppDbContext dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.MigrateAsync(cancellationToken);
        var repository = new EfReferenceCatalogRepository(dbContext);
        var service = new ReferenceCatalogPersistenceService(repository, timeProvider);
        ReferenceCatalogPublishResult result = await service.PublishAsync(
            new ReferenceCatalogPublishCommand(
                JsonSerializer.Serialize(snapshot, JsonOptions),
                snapshot.SchemaVersion,
                snapshot.GeneratedAtUtc,
                snapshot.Statistics.ProgramCount,
                snapshot.Statistics.GroupCount,
                snapshot.Statistics.TeacherCount),
            cancellationToken);
        logger.LogInformation(
            "PostgreSQL catalog publication finished with {Status}; hash {ContentHash}",
            result.Status,
            result.ContentHash);
    }

    public async Task WriteFailureAsync(
        DateTimeOffset startedAtUtc,
        Exception exception,
        CancellationToken cancellationToken)
    {
        await using AppDbContext dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.MigrateAsync(cancellationToken);
        string message = $"{exception.GetType().Name}: {exception.Message}";
        if (message.Length > 4_000)
        {
            message = message[..4_000];
        }

        dbContext.ReferenceCatalogImportLogs.Add(
            UniversitySchedule.Domain.Catalog.ReferenceCatalogImportLog.CreateFailure(
                startedAtUtc,
                timeProvider.GetUtcNow(),
                message));
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
