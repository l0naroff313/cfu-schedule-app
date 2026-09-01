using UniversitySchedule.Domain.Identity;

namespace UniversitySchedule.Application.Identity;

public interface IInstallationRepository
{
    Task<Installation?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> TryAddAsync(Installation installation, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
