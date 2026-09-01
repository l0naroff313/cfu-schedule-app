namespace UniversitySchedule.Application.Identity;

public sealed record InstallationAccessToken(string Value, DateTimeOffset ExpiresAtUtc);
