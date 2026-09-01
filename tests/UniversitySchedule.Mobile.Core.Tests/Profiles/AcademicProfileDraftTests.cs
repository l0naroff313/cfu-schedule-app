using UniversitySchedule.Contracts.Catalog;
using UniversitySchedule.Mobile.Core.Profiles;

namespace UniversitySchedule.Mobile.Core.Tests.Profiles;

public sealed class AcademicProfileDraftTests
{
    [Fact]
    public void ChangingInstitute_ClearsDependentSelections()
    {
        var draft = CreateCompleteDraft();

        draft.SelectInstitute(new InstituteSummary(Guid.NewGuid(), "Другой институт"));

        Assert.Null(draft.Direction);
        Assert.Null(draft.Group);
        Assert.Null(draft.Subgroup);
    }

    [Fact]
    public void Build_AllowsGroupWithoutSubgroups()
    {
        var draft = new AcademicProfileDraft();
        var institute = new InstituteSummary(Guid.NewGuid(), "ФТИ");
        var direction = new DirectionSummary(Guid.NewGuid(), institute.Id, "Прикладная математика");
        var group = new StudyGroupSummary(
            Guid.NewGuid(),
            direction.Id,
            "ПМИ-б-о-251",
            1,
            SubgroupSelectionPolicy.NotAvailable);

        draft.SelectInstitute(institute);
        draft.SelectDirection(direction);
        draft.SelectGroup(group);

        AcademicProfile profile = draft.Build();

        Assert.Null(profile.SubgroupId);
        Assert.Equal("ПМИ-б-о-251", profile.GroupName);
    }

    [Fact]
    public void SelectDirection_RequiresInstitute()
    {
        var draft = new AcademicProfileDraft();

        Assert.Throws<InvalidOperationException>(() => draft.SelectDirection(
            new DirectionSummary(Guid.NewGuid(), Guid.NewGuid(), "Математика")));
    }

    [Fact]
    public void SelectDirection_RejectsDifferentInstitute()
    {
        var draft = new AcademicProfileDraft();
        draft.SelectInstitute(new InstituteSummary(Guid.NewGuid(), "ФТИ"));

        Assert.Throws<ArgumentException>(() => draft.SelectDirection(
            new DirectionSummary(Guid.NewGuid(), Guid.NewGuid(), "Математика")));
    }

    [Fact]
    public void SelectGroup_RejectsDifferentDirection()
    {
        var draft = new AcademicProfileDraft();
        var institute = new InstituteSummary(Guid.NewGuid(), "ФТИ");
        var direction = new DirectionSummary(Guid.NewGuid(), institute.Id, "Математика");
        draft.SelectInstitute(institute);
        draft.SelectDirection(direction);

        Assert.Throws<ArgumentException>(() => draft.SelectGroup(new StudyGroupSummary(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "МАТ-б-о-251",
            1,
            SubgroupSelectionPolicy.Optional)));
    }

    [Fact]
    public void Build_RequiresSubgroupForSplitGroup()
    {
        var draft = CreateDraft(SubgroupSelectionPolicy.Required);

        Assert.Throws<InvalidOperationException>(() => draft.Build());
    }

    [Fact]
    public void SelectSubgroup_RejectsDifferentGroup()
    {
        var draft = CreateDraft(SubgroupSelectionPolicy.Optional);

        Assert.Throws<ArgumentException>(() => draft.SelectSubgroup(
            new SubgroupSummary(Guid.NewGuid(), Guid.NewGuid(), "1 подгруппа")));
    }

    private static AcademicProfileDraft CreateCompleteDraft()
    {
        AcademicProfileDraft draft = CreateDraft(SubgroupSelectionPolicy.Required);
        draft.SelectSubgroup(new SubgroupSummary(
            Guid.NewGuid(),
            draft.Group!.Id,
            "1 подгруппа"));
        return draft;
    }

    private static AcademicProfileDraft CreateDraft(SubgroupSelectionPolicy policy)
    {
        var draft = new AcademicProfileDraft();
        var institute = new InstituteSummary(Guid.NewGuid(), "ФТИ");
        var direction = new DirectionSummary(Guid.NewGuid(), institute.Id, "Прикладная математика");
        var group = new StudyGroupSummary(
            Guid.NewGuid(),
            direction.Id,
            "ПМИ-б-о-251",
            1,
            policy);

        draft.SelectInstitute(institute);
        draft.SelectDirection(direction);
        draft.SelectGroup(group);
        return draft;
    }
}
