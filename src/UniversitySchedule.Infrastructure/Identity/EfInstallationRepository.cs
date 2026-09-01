using Microsoft.EntityFrameworkCore;
using Npgsql;
using UniversitySchedule.Application.Identity;
using UniversitySchedule.Domain.Identity;
using UniversitySchedule.Infrastructure.Persistence;

namespace UniversitySchedule.Infrastructure.Identity;

public sealed class EfInstallationRepository(AppDbContext dbContext) : IInstallationRepository
{
    public async Task<Installation?> FindAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Installations.FindAsync([id], cancellationToken);
    }

    public async Task<bool> TryAddAsync(
        Installation installation,
        CancellationToken cancellationToken = default)
    {
        dbContext.Installations.Add(installation);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            dbContext.Entry(installation).State = EntityState.Detached;
            return false;
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        };
    }
}
