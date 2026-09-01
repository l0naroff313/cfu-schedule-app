namespace UniversitySchedule.Contracts.Catalog;

public sealed record InstituteSummary(Guid Id, string Name);

public sealed record DirectionSummary(Guid Id, Guid InstituteId, string Name);

public sealed record StudyGroupSummary(
    Guid Id,
    Guid DirectionId,
    string Name,
    int CourseNumber,
    SubgroupSelectionPolicy SubgroupPolicy);

public sealed record SubgroupSummary(Guid Id, Guid GroupId, string Name);

public sealed record TeacherSummary(
    Guid Id,
    string DisplayName,
    string? SecondaryText = null)
{
    public override string ToString() => DisplayName;
}

public enum SubgroupSelectionPolicy
{
    NotAvailable = 0,
    Optional = 1,
    Required = 2,
}
