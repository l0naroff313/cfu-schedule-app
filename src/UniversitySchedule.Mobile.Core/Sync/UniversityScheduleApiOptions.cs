namespace UniversitySchedule.Mobile.Core.Sync;

public sealed class UniversityScheduleApiOptions
{
    public UniversityScheduleApiOptions(Uri? baseAddress, string platform, string appVersion)
    {
        BaseAddress = baseAddress?.Scheme == Uri.UriSchemeHttps ? baseAddress : null;
        Platform = platform?.Trim().ToLowerInvariant() ?? string.Empty;
        AppVersion = appVersion?.Trim() ?? string.Empty;
    }

    public Uri? BaseAddress { get; }

    public string Platform { get; }

    public string AppVersion { get; }

    public bool IsEnabled =>
        BaseAddress is not null &&
        Platform is "android" or "ios" &&
        !string.IsNullOrWhiteSpace(AppVersion);
}
