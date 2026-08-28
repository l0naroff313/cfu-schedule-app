namespace UniversitySchedule.Mobile.Core.Profiles;

public sealed record AcademicProfile(
    Guid InstituteId,
    string InstituteName,
    Guid DirectionId,
    string DirectionName,
    Guid GroupId,
    string GroupName,
    int CourseNumber,
    Guid? SubgroupId,
    string? SubgroupName);
