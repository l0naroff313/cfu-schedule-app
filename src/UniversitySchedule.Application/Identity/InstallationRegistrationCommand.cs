namespace UniversitySchedule.Application.Identity;

public sealed record InstallationRegistrationCommand(
    Guid InstallationId,
    string InstallationSecret,
    string Platform,
    string AppVersion);
