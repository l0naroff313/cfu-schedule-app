namespace UniversitySchedule.Contracts.Identity;

public sealed record RegisterInstallationResponse(
    Guid InstallationId,
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAtUtc,
    bool IsNewInstallation);
