using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace UniversitySchedule.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private static WeakReference<MainActivity>? _current;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _current = new WeakReference<MainActivity>(this);
        ApplySystemBars(Microsoft.Maui.Controls.Application.Current?.RequestedTheme ?? AppTheme.Unspecified);
    }

    public static void ApplySystemBars(AppTheme theme)
    {
        if (_current?.TryGetTarget(out MainActivity? activity) == true)
        {
            activity.UpdateSystemBars(theme);
        }
    }

    private void UpdateSystemBars(AppTheme theme)
    {
        bool isLight = theme == AppTheme.Light ||
            (theme == AppTheme.Unspecified &&
             (Microsoft.Maui.Controls.Application.Current?.RequestedTheme ?? AppTheme.Light) == AppTheme.Light);
        string background = isLight ? "#FAFBFD" : "#031325";
        if (!OperatingSystem.IsAndroidVersionAtLeast(35))
        {
            Window?.SetStatusBarColor(Android.Graphics.Color.ParseColor(background));
            Window?.SetNavigationBarColor(Android.Graphics.Color.ParseColor(background));
        }

        if (Window is null)
        {
            return;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            const int AppearanceLightStatusBars = 8;
            const int AppearanceLightNavigationBars = 16;
            int mask = AppearanceLightStatusBars | AppearanceLightNavigationBars;
            Window.InsetsController?.SetSystemBarsAppearance(isLight ? mask : 0, mask);
            return;
        }

        if (Window.DecorView is null)
        {
            return;
        }

        SystemUiFlags flags = SystemUiFlags.Visible;
        if (isLight && OperatingSystem.IsAndroidVersionAtLeast(23))
        {
            flags |= SystemUiFlags.LightStatusBar;
        }

        if (isLight && OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            flags |= SystemUiFlags.LightNavigationBar;
        }

        Window.DecorView.SystemUiFlags = flags;
    }
}
