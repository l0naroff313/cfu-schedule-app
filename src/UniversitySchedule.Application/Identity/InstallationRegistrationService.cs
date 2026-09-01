using System.Security.Cryptography;
using UniversitySchedule.Domain.Identity;

namespace UniversitySchedule.Application.Identity;

public sealed class InstallationRegistrationService(
    IInstallationRepository repository,
    IInstallationSecretHasher secretHasher,
    IInstallationAccessTokenIssuer accessTokenIssuer,
    TimeProvider timeProvider)
{
    private const int SecretByteCount = 32;
    private const int MaximumSecretTextLength = 128;
    private const int MaximumAppVersionLength = 64;

    public async Task<InstallationRegistrationResult> RegisterAsync(
        InstallationRegistrationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!TryNormalize(command, out string platform, out string appVersion, out byte[] secret))
        {
            return InstallationRegistrationResult.Failure(
                InstallationRegistrationStatus.InvalidRequest,
                command.InstallationId,
                "invalid_installation_credentials");
        }

        byte[] secretHash;
        try
        {
            secretHash = secretHasher.Hash(secret);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }

        DateTimeOffset now = timeProvider.GetUtcNow().ToUniversalTime();
        Installation? installation = await repository.FindAsync(command.InstallationId, cancellationToken);
        bool isNewInstallation = false;

        if (installation is null)
        {
            var candidate = Installation.Register(
                command.InstallationId,
                secretHash,
                platform,
                appVersion,
                now);

            isNewInstallation = await repository.TryAddAsync(candidate, cancellationToken);
            installation = isNewInstallation
                ? candidate
                : await repository.FindAsync(command.InstallationId, cancellationToken);

            if (installation is null)
            {
                throw new InvalidOperationException("The installation could not be loaded after a registration race.");
            }
        }

        if (!installation.MatchesSecretHash(secretHash))
        {
            return InstallationRegistrationResult.Failure(
                InstallationRegistrationStatus.InvalidCredentials,
                command.InstallationId,
                "invalid_installation_credentials");
        }

        if (installation.IsRevoked)
        {
            return InstallationRegistrationResult.Failure(
                InstallationRegistrationStatus.Revoked,
                command.InstallationId,
                "installation_revoked");
        }

        if (!isNewInstallation)
        {
            installation.RecordSeen(platform, appVersion, now);
            await repository.SaveChangesAsync(cancellationToken);
        }

        InstallationAccessToken token = accessTokenIssuer.Issue(installation, now);
        return InstallationRegistrationResult.Success(
            installation.Id,
            token,
            isNewInstallation);
    }

    private static bool TryNormalize(
        InstallationRegistrationCommand command,
        out string platform,
        out string appVersion,
        out byte[] secret)
    {
        platform = command.Platform?.Trim().ToLowerInvariant() ?? string.Empty;
        appVersion = command.AppVersion?.Trim() ?? string.Empty;
        secret = [];

        if (command.InstallationId == Guid.Empty ||
            platform is not ("android" or "ios" or "web") ||
            string.IsNullOrWhiteSpace(appVersion) ||
            appVersion.Length > MaximumAppVersionLength ||
            appVersion.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(command.InstallationSecret) ||
            command.InstallationSecret.Length > MaximumSecretTextLength)
        {
            return false;
        }

        try
        {
            secret = Convert.FromBase64String(command.InstallationSecret);
            if (secret.Length == SecretByteCount)
            {
                return true;
            }

            CryptographicOperations.ZeroMemory(secret);
            secret = [];
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
