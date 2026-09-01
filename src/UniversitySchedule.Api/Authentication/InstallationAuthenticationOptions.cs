using System.ComponentModel.DataAnnotations;
using System.Text;

namespace UniversitySchedule.Api.Authentication;

public sealed class InstallationAuthenticationOptions
{
    public const string SectionName = "InstallationAuthentication";

    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    [Required]
    public string SecretPepper { get; init; } = string.Empty;

    [Required]
    public string JwtSigningKey { get; init; } = string.Empty;

    [Range(5, 60)]
    public int AccessTokenMinutes { get; init; } = 15;

    public bool HasSecureKeyLengths()
    {
        return Encoding.UTF8.GetByteCount(SecretPepper) >= 32 &&
            Encoding.UTF8.GetByteCount(JwtSigningKey) >= 32;
    }
}
