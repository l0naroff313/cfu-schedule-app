using System.Security.Cryptography;
using System.Text;
using UniversitySchedule.Domain.Catalog;

namespace UniversitySchedule.Application.Catalog;

public enum ReferenceCatalogPublishStatus
{
    Published = 0,
    Unchanged = 1,
    RejectedAsStale = 2,
}

public sealed record ReferenceCatalogPublishCommand(
    string PayloadJson,
    int SchemaVersion,
    DateTimeOffset SourceGeneratedAtUtc,
    int ProgramCount,
    int GroupCount,
    int TeacherCount);

public sealed record ReferenceCatalogPublishResult(
    ReferenceCatalogPublishStatus Status,
    string ContentHash,
    DateTimeOffset ImportedAtUtc);

public sealed class ReferenceCatalogPersistenceService(
    IReferenceCatalogRepository repository,
    TimeProvider timeProvider)
{
    public Task<ReferenceCatalogDocument?> GetCurrentAsync(
        CancellationToken cancellationToken = default) =>
        repository.GetCurrentAsync(cancellationToken);

    public async Task<ReferenceCatalogPublishResult> PublishAsync(
        ReferenceCatalogPublishCommand command,
        CancellationToken cancellationToken = default)
    {
        Validate(command);
        DateTimeOffset startedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(command.PayloadJson)));
        ReferenceCatalogDocument? current = await repository.GetCurrentAsync(cancellationToken);
        ReferenceCatalogPublishStatus status;

        if (current is not null && command.SourceGeneratedAtUtc.ToUniversalTime() < current.SourceGeneratedAtUtc)
        {
            status = ReferenceCatalogPublishStatus.RejectedAsStale;
        }
        else if (current is not null && string.Equals(current.ContentHash, hash, StringComparison.Ordinal))
        {
            current.Replace(
                command.PayloadJson,
                hash,
                command.SchemaVersion,
                command.SourceGeneratedAtUtc,
                startedAtUtc);
            status = ReferenceCatalogPublishStatus.Unchanged;
        }
        else if (current is null)
        {
            current = ReferenceCatalogDocument.Create(
                command.PayloadJson,
                hash,
                command.SchemaVersion,
                command.SourceGeneratedAtUtc,
                startedAtUtc);
            repository.Add(current);
            status = ReferenceCatalogPublishStatus.Published;
        }
        else
        {
            current.Replace(
                command.PayloadJson,
                hash,
                command.SchemaVersion,
                command.SourceGeneratedAtUtc,
                startedAtUtc);
            status = ReferenceCatalogPublishStatus.Published;
        }

        DateTimeOffset finishedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        repository.AddImportLog(ReferenceCatalogImportLog.Create(
            startedAtUtc,
            finishedAtUtc,
            (ReferenceCatalogImportStatus)status,
            hash,
            command.ProgramCount,
            command.GroupCount,
            command.TeacherCount));
        await repository.SaveChangesAsync(cancellationToken);
        return new ReferenceCatalogPublishResult(status, hash, finishedAtUtc);
    }

    private static void Validate(ReferenceCatalogPublishCommand command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.PayloadJson);
        if (command.SchemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command.SchemaVersion));
        }

        if (command.ProgramCount < 0 || command.GroupCount < 0 || command.TeacherCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command.ProgramCount));
        }
    }
}
