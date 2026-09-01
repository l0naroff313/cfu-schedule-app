using UniversitySchedule.ScheduleImporter;

namespace UniversitySchedule.Importer.Tests;

public sealed class ImportOptionsTests
{
    [Fact]
    public void Parse_SeedPostgresEnablesDatabasePublishingWithoutRefresh()
    {
        ImportOptions options = ImportOptions.Parse(
            ["--seed-postgres"],
            Path.Combine(Path.GetTempPath(), "cfu-importer", "src"));

        Assert.True(options.PublishPostgreSql);
        Assert.True(options.SeedPostgreSql);
        Assert.False(options.Refresh);
    }

    [Fact]
    public void Parse_PublishPostgresKeepsNormalSourceImport()
    {
        ImportOptions options = ImportOptions.Parse(
            ["--publish-postgres", "--refresh"],
            Path.Combine(Path.GetTempPath(), "cfu-importer", "src"));

        Assert.True(options.PublishPostgreSql);
        Assert.False(options.SeedPostgreSql);
        Assert.True(options.Refresh);
    }
}
