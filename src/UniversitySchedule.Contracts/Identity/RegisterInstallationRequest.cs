using System.ComponentModel.DataAnnotations;

namespace UniversitySchedule.Contracts.Identity;

public sealed record RegisterInstallationRequest(
    [Required] Guid InstallationId,
    [Required, MaxLength(128)] string InstallationSecret,
    [Required, MaxLength(16)] string Platform,
    [Required, MaxLength(64)] string AppVersion);
