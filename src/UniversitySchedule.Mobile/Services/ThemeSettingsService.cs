namespace UniversitySchedule.Mobile.Services;

public sealed class ThemeSettingsService
{
    private const string PreferenceKey = "appearance-theme";

    public AppTheme CurrentTheme { get; private set; } = AppTheme.Unspecified;

    public void ApplySavedTheme()
    {
        string value = Preferences.Default.Get(PreferenceKey, "system");
        SetTheme(value switch
        {
            "light" => AppTheme.Light,
            "dark" => AppTheme.Dark,
            _ => AppTheme.Unspecified,
        });
    }

    public void SetTheme(AppTheme theme)
    {
        CurrentTheme = theme;
        if (Application.Current is not null)
        {
            Application.Current.UserAppTheme = theme;
        }

#if ANDROID
        MainActivity.ApplySystemBars(theme);
#endif
#if IOS
        IosAppearance.ApplySystemAppearance(theme);
#endif

        Preferences.Default.Set(PreferenceKey, theme switch
        {
            AppTheme.Light => "light",
            AppTheme.Dark => "dark",
            _ => "system",
        });
    }
}
