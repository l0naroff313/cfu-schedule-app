using UniversitySchedule.Contracts.Catalog;

namespace UniversitySchedule.Mobile.Core.Cfu;

public sealed record CfuScheduleCatalog(
    IReadOnlyList<InstituteSummary> Institutes,
    IReadOnlyList<DirectionSummary> Directions,
    IReadOnlyList<StudyGroupSummary> Groups)
{
    public IReadOnlyList<int> GetCourses(Guid directionId) => Groups
        .Where(group => group.DirectionId == directionId)
        .Select(group => group.CourseNumber)
        .Distinct()
        .OrderBy(course => course)
        .ToArray();
}

public static class CfuScheduleCatalogMapper
{
    public static CfuScheduleCatalog Map(CfuScheduleIndexDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var institutes = new List<InstituteSummary>();
        var directions = new List<DirectionSummary>();
        var groups = new List<StudyGroupSummary>();

        foreach ((string instituteName, IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> directionTree) in
                 document.Tree.OrderBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            Guid instituteId = CfuStableId.Create("institute", instituteName);
            institutes.Add(new InstituteSummary(instituteId, instituteName));

            foreach ((string directionName, IReadOnlyDictionary<string, IReadOnlyList<string>> courseTree) in
                     directionTree.OrderBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                if (!IsAcademicDirection(directionName))
                {
                    continue;
                }

                Guid directionId = CfuStableId.Create("direction", instituteName, directionName);
                directions.Add(new DirectionSummary(directionId, instituteId, directionName));

                foreach ((string courseText, IReadOnlyList<string> groupCodes) in courseTree)
                {
                    if (!int.TryParse(courseText, out int courseNumber) || courseNumber <= 0)
                    {
                        continue;
                    }

                    foreach (string groupCode in groupCodes
                                 .Where(code => !string.IsNullOrWhiteSpace(code))
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .OrderBy(code => code, StringComparer.CurrentCultureIgnoreCase))
                    {
                        groups.Add(new StudyGroupSummary(
                            CfuStableId.Create("group", groupCode),
                            directionId,
                            groupCode.Trim(),
                            courseNumber,
                            SubgroupSelectionPolicy.Optional));
                    }
                }
            }
        }

        return new CfuScheduleCatalog(institutes, directions, groups);
    }

    private static bool IsAcademicDirection(string value)
    {
        string code = value.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;
        string[] parts = code.Trim('.').Split('.');
        return parts.Length == 3 && parts.All(part => int.TryParse(part, out _));
    }
}
