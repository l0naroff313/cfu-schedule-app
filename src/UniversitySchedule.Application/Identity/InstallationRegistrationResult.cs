namespace UniversitySchedule.Application.Identity;

public enum InstallationRegistrationStatus
{
    Success,
    InvalidRequest,
    InvalidCredentials,
    Revoked,
}

public sealed record InstallationRegistrationResult(
    InstallationRegistrationStatus Status,
    Guid InstallationId,
    InstallationAccessToken? AccessToken,
    bool IsNewInstallation,
    string? ErrorCode)
{
    public static InstallationRegistrationResult Success(
        Guid installationId,
        InstallationAccessToken accessToken,
        bool isNewInstallation) =>
        new(
            InstallationRegistrationStatus.Success,
            installationId,
            accessToken,
            isNewInstallation,
            null);

    public static InstallationRegistrationResult Failure(
        InstallationRegistrationStatus status,
        Guid installationId,
        string errorCode) =>
        new(status, installationId, null, false, errorCode);
}
