using UniversitySchedule.Contracts.Catalog;

namespace UniversitySchedule.Mobile.Core.Profiles;

public sealed class AcademicProfileDraft
{
    public InstituteSummary? Institute { get; private set; }

    public DirectionSummary? Direction { get; private set; }

    public StudyGroupSummary? Group { get; private set; }

    public SubgroupSummary? Subgroup { get; private set; }

    public void SelectInstitute(InstituteSummary institute)
    {
        ArgumentNullException.ThrowIfNull(institute);

        if (Institute?.Id != institute.Id)
        {
            Direction = null;
            Group = null;
            Subgroup = null;
        }

        Institute = institute;
    }

    public void SelectDirection(DirectionSummary direction)
    {
        ArgumentNullException.ThrowIfNull(direction);
        EnsureSelected(Institute, "Select an institute before selecting a direction.");

        if (direction.InstituteId != Institute!.Id)
        {
            throw new ArgumentException(
                "The direction does not belong to the selected institute.",
                nameof(direction));
        }

        if (Direction?.Id != direction.Id)
        {
            Group = null;
            Subgroup = null;
        }

        Direction = direction;
    }

    public void SelectGroup(StudyGroupSummary group)
    {
        ArgumentNullException.ThrowIfNull(group);
        EnsureSelected(Direction, "Select a direction before selecting a group.");

        if (group.DirectionId != Direction!.Id)
        {
            throw new ArgumentException(
                "The group does not belong to the selected direction.",
                nameof(group));
        }

        if (Group?.Id != group.Id)
        {
            Subgroup = null;
        }

        Group = group;
    }

    public void SelectSubgroup(SubgroupSummary? subgroup)
    {
        EnsureSelected(Group, "Select a group before selecting a subgroup.");

        if (subgroup is not null && subgroup.GroupId != Group!.Id)
        {
            throw new ArgumentException(
                "The subgroup does not belong to the selected group.",
                nameof(subgroup));
        }

        if (subgroup is not null && Group!.SubgroupPolicy == SubgroupSelectionPolicy.NotAvailable)
        {
            throw new InvalidOperationException("The selected group has no subgroups.");
        }

        Subgroup = subgroup;
    }

    public AcademicProfile Build()
    {
        InstituteSummary institute = Institute
            ?? throw new InvalidOperationException("Institute is not selected.");
        DirectionSummary direction = Direction
            ?? throw new InvalidOperationException("Direction is not selected.");
        StudyGroupSummary group = Group
            ?? throw new InvalidOperationException("Group is not selected.");

        if (group.SubgroupPolicy == SubgroupSelectionPolicy.Required && Subgroup is null)
        {
            throw new InvalidOperationException("Subgroup is required for the selected group.");
        }

        return new AcademicProfile(
            institute.Id,
            institute.Name,
            direction.Id,
            direction.Name,
            group.Id,
            group.Name,
            group.CourseNumber,
            Subgroup?.Id,
            Subgroup?.Name);
    }

    private static void EnsureSelected(object? value, string message)
    {
        if (value is null)
        {
            throw new InvalidOperationException(message);
        }
    }
}
