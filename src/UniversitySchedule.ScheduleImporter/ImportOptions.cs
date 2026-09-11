namespace UniversitySchedule.ScheduleImporter;

public sealed record ImportOptions(
    string OutputPath,
    string ReportsDirectory,
    string CacheDirectory,
    bool Refresh,
    bool SkipTeacherDetails,
    TimeSpan VuzopediaCrawlDelay,
    bool PublishPostgreSql = false,
    bool SeedPostgreSql = false,
    string? ExcelPath = null,
    string? ManualScheduleOutputPath = null,
    string? CatalogInputPath = null,
    int AcademicYear = 2026,
    bool ReplaceExcelGroups = false,
    bool OfficialSchedule = false)
{
    public static ImportOptions Parse(IReadOnlyList<string> args, string contentRoot)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);

        string repositoryRoot = Path.GetFullPath(Path.Combine(contentRoot, "..", ".."));
        string outputPath = Path.Combine(
            repositoryRoot,
            "src",
            "UniversitySchedule.Mobile",
            "Resources",
            "Raw",
            "cfu-reference-catalog.json");
        string reportsDirectory = Path.Combine(repositoryRoot, "docs", "data-quality");
        string cacheDirectory = Path.Combine(repositoryRoot, "artifacts", "reference-import");
        string manualScheduleOutputPath = Path.Combine(
            repositoryRoot,
            "src",
            "UniversitySchedule.Mobile",
            "Resources",
            "Raw",
            "cfu-manual-schedule.json");
        string catalogInputPath = outputPath;
        string? excelPath = null;
        bool refresh = false;
        bool skipTeacherDetails = false;
        bool publishPostgreSql = false;
        bool seedPostgreSql = false;
        double delaySeconds = 5;
        int academicYear = 2026;
        bool replaceExcelGroups = false;
        bool officialSchedule = false;

        for (int index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--output":
                    outputPath = RequireValue(args, ref index, "--output");
                    break;
                case "--reports":
                    reportsDirectory = RequireValue(args, ref index, "--reports");
                    break;
                case "--cache":
                    cacheDirectory = RequireValue(args, ref index, "--cache");
                    break;
                case "--refresh":
                    refresh = true;
                    break;
                case "--official-schedule":
                    officialSchedule = true;
                    refresh = true;
                    break;
                case "--skip-teacher-details":
                    skipTeacherDetails = true;
                    break;
                case "--vuzopedia-delay-seconds":
                    string value = RequireValue(args, ref index, "--vuzopedia-delay-seconds");
                    if (!double.TryParse(value, out delaySeconds) || delaySeconds < 5)
                    {
                        throw new ArgumentException("Vuzopedia delay must be at least 5 seconds.");
                    }

                    break;
                case "--publish-postgres":
                    publishPostgreSql = true;
                    break;
                case "--seed-postgres":
                    publishPostgreSql = true;
                    seedPostgreSql = true;
                    break;
                case "--excel":
                    excelPath = RequireValue(args, ref index, "--excel");
                    break;
                case "--replace-excel-groups":
                    replaceExcelGroups = true;
                    break;
                case "--manual-output":
                    manualScheduleOutputPath = RequireValue(args, ref index, "--manual-output");
                    break;
                case "--catalog":
                    catalogInputPath = RequireValue(args, ref index, "--catalog");
                    break;
                case "--academic-year":
                    string academicYearText = RequireValue(args, ref index, "--academic-year");
                    if (!int.TryParse(academicYearText, out academicYear) || academicYear < 2000 || academicYear > 2100)
                    {
                        throw new ArgumentException("Academic year must be a four digit calendar year.");
                    }
                    break;
                default:
                    throw new ArgumentException($"Unknown importer option: {args[index]}");
            }
        }

        if (replaceExcelGroups && excelPath is null)
            throw new ArgumentException("--replace-excel-groups requires --excel.");
        if (officialSchedule && (excelPath is not null || publishPostgreSql))
            throw new ArgumentException("--official-schedule cannot be combined with Excel or PostgreSQL import.");

        return new ImportOptions(
            Path.GetFullPath(outputPath),
            Path.GetFullPath(reportsDirectory),
            Path.GetFullPath(cacheDirectory),
            refresh,
            skipTeacherDetails,
            TimeSpan.FromSeconds(delaySeconds),
            publishPostgreSql,
            seedPostgreSql,
            excelPath is null ? null : Path.GetFullPath(excelPath),
            Path.GetFullPath(manualScheduleOutputPath),
            Path.GetFullPath(catalogInputPath),
            academicYear,
            replaceExcelGroups,
            officialSchedule);
    }

    private static string RequireValue(IReadOnlyList<string> args, ref int index, string option)
    {
        if (++index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new ArgumentException($"Option {option} requires a value.");
        }

        return args[index];
    }
}
