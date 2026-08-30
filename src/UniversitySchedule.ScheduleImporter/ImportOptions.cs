namespace UniversitySchedule.ScheduleImporter;

public sealed record ImportOptions(
    string OutputPath,
    string ReportsDirectory,
    string CacheDirectory,
    bool Refresh,
    bool SkipTeacherDetails,
    TimeSpan VuzopediaCrawlDelay,
    bool PublishPostgreSql = false,
    bool SeedPostgreSql = false)
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
        bool refresh = false;
        bool skipTeacherDetails = false;
        bool publishPostgreSql = false;
        bool seedPostgreSql = false;
        double delaySeconds = 5;

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
                default:
                    throw new ArgumentException($"Unknown importer option: {args[index]}");
            }
        }

        return new ImportOptions(
            Path.GetFullPath(outputPath),
            Path.GetFullPath(reportsDirectory),
            Path.GetFullPath(cacheDirectory),
            refresh,
            skipTeacherDetails,
            TimeSpan.FromSeconds(delaySeconds),
            publishPostgreSql,
            seedPostgreSql);
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
