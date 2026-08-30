using System.Security.Claims;

namespace UniversitySchedule.Api.Authentication;

public static class InstallationPrincipal
{
    public static bool TryGetInstallationId(ClaimsPrincipal principal, out Guid installationId)
    {
        string? value = principal.FindFirstValue(JwtInstallationAccessTokenIssuer.InstallationIdClaim);
        return Guid.TryParseExact(value, "D", out installationId) && installationId != Guid.Empty;
    }
}
