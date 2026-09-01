using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using UniversitySchedule.Application.Identity;
using UniversitySchedule.Domain.Identity;

namespace UniversitySchedule.Api.Authentication;

public sealed class JwtInstallationAccessTokenIssuer(
    IOptions<InstallationAuthenticationOptions> options) : IInstallationAccessTokenIssuer
{
    public const string InstallationIdClaim = "installation_id";
    public const string PrincipalTypeClaim = "principal_type";
    public const string InstallationPrincipalType = "installation";

    private readonly InstallationAuthenticationOptions _options = options.Value;

    public InstallationAccessToken Issue(Installation installation, DateTimeOffset issuedAtUtc)
    {
        DateTimeOffset normalizedIssuedAt = issuedAtUtc.ToUniversalTime();
        DateTimeOffset expiresAt = normalizedIssuedAt.AddMinutes(_options.AccessTokenMinutes);
        string installationId = installation.Id.ToString("D");
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, installationId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("D")),
            new Claim(InstallationIdClaim, installationId),
            new Claim(PrincipalTypeClaim, InstallationPrincipalType),
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.JwtSigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: normalizedIssuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new InstallationAccessToken(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt);
    }
}
