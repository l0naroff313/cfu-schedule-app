#if VISUAL_SNAPSHOTS
namespace UniversitySchedule.Mobile.Services;

internal sealed record VisualSnapshotOptions(string Route, AppTheme Theme)
{
    private static readonly HashSet<string> Routes =
    [
        "today",
        "schedule",
        "assignments",
        "notes",
        "profile",
    ];

    public string MarkerFileName => $"visual-snapshot-ready-{ThemeName}-{Route}";

    public string ThemeName => Theme == AppTheme.Dark ? "dark" : "light";

    public static bool TryRead(out VisualSnapshotOptions options)
    {
        string[] arguments = Environment.GetCommandLineArgs();
        string? route = ReadValue(arguments, "--visual-snapshot=")?.ToLowerInvariant();
        if (route is null || !Routes.Contains(route))
        {
            options = null!;
            return false;
        }

        string? theme = ReadValue(arguments, "--visual-theme=")?.ToLowerInvariant();
        options = new VisualSnapshotOptions(
            route,
            theme == "dark" ? AppTheme.Dark : AppTheme.Light);
        return true;
    }

    private static string? ReadValue(IEnumerable<string> arguments, string prefix) =>
        arguments.FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.Ordinal))
            ?[prefix.Length..];
}
#endif
