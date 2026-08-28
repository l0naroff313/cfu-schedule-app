using System.Xml.Linq;

namespace UniversitySchedule.Infrastructure.IntegrationTests;

public sealed class ProjectDependencyTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedDependencies =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["UniversitySchedule.Domain"] = new HashSet<string>(StringComparer.Ordinal),
            ["UniversitySchedule.Application"] = Set("UniversitySchedule.Domain"),
            ["UniversitySchedule.Contracts"] = new HashSet<string>(StringComparer.Ordinal),
            ["UniversitySchedule.Infrastructure"] = Set(
                "UniversitySchedule.Application",
                "UniversitySchedule.Domain"),
            ["UniversitySchedule.Api"] = Set(
                "UniversitySchedule.Application",
                "UniversitySchedule.Infrastructure",
                "UniversitySchedule.Contracts"),
            ["UniversitySchedule.ScheduleImporter"] = Set(
                "UniversitySchedule.Application",
                "UniversitySchedule.Infrastructure",
                "UniversitySchedule.Contracts"),
            ["UniversitySchedule.Mobile.Core"] = Set("UniversitySchedule.Contracts"),
            ["UniversitySchedule.Mobile"] = Set("UniversitySchedule.Mobile.Core"),
        };

    [Fact]
    public void SourceProjectReferences_FollowAcceptedArchitecture()
    {
        string sourceDirectory = Path.Combine(FindRepositoryRoot(), "src");
        var violations = new List<string>();

        foreach (string projectPath in Directory.EnumerateFiles(
                     sourceDirectory,
                     "*.csproj",
                     SearchOption.AllDirectories))
        {
            string projectName = Path.GetFileNameWithoutExtension(projectPath);
            IReadOnlySet<string> allowed = AllowedDependencies[projectName];
            XDocument project = XDocument.Load(projectPath);

            foreach (XElement reference in project.Descendants("ProjectReference"))
            {
                string include = reference.Attribute("Include")?.Value
                    ?? throw new InvalidOperationException($"ProjectReference without Include in {projectPath}.");
                string dependencyName = Path.GetFileNameWithoutExtension(include);

                if (!allowed.Contains(dependencyName))
                {
                    violations.Add($"{projectName} -> {dependencyName}");
                }
            }
        }

        Assert.Empty(violations);
    }

    private static HashSet<string> Set(params string[] values)
    {
        return new HashSet<string>(values, StringComparer.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
