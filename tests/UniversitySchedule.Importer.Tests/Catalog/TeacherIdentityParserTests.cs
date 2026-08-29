using UniversitySchedule.Contracts.Catalog;

namespace UniversitySchedule.Importer.Tests.Catalog;

public sealed class TeacherIdentityParserTests
{
    [Fact]
    public void TryParse_FullNameAndScheduleInitialsProduceSameKey()
    {
        Assert.True(TeacherIdentityParser.TryParse("Атик Аниса Ахмедовна", out TeacherIdentity full));
        Assert.True(TeacherIdentityParser.TryParse("Атик А.А.", out TeacherIdentity abbreviated));

        Assert.Equal(full.Key, abbreviated.Key);
        Assert.Equal("Атик А.А.", full.ScheduleDisplayName);
    }

    [Fact]
    public void TryParse_DoesNotTreatSubstringSurnameAsExactTeacher()
    {
        Assert.True(TeacherIdentityParser.TryParse("Атик А.А.", out TeacherIdentity atik));
        Assert.True(TeacherIdentityParser.TryParse("Богатикова Н.П.", out TeacherIdentity bogatikova));

        Assert.NotEqual(atik.Key, bogatikova.Key);
    }
}
