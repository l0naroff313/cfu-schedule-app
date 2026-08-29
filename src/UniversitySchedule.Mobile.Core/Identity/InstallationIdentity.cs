namespace UniversitySchedule.Mobile.Core.Identity;

public sealed record InstallationIdentity(
    Guid Id,
    string Secret,
    DateTimeOffset CreatedAtUtc)
{
    public string DisplayId => Id.ToString("D");
}
