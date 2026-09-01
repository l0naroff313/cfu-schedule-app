using UniversitySchedule.Domain.Identity;

namespace UniversitySchedule.Application.Identity;

public interface IInstallationAccessTokenIssuer
{
    InstallationAccessToken Issue(Installation installation, DateTimeOffset issuedAtUtc);
}
