using UIKit;

namespace UniversitySchedule.Mobile;

public static class IosAppearance
{
    public static void ApplySystemAppearance(AppTheme theme)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UIUserInterfaceStyle style = theme switch
            {
                AppTheme.Light => UIUserInterfaceStyle.Light,
                AppTheme.Dark => UIUserInterfaceStyle.Dark,
                _ => UIUserInterfaceStyle.Unspecified,
            };

            foreach (UIWindowScene scene in UIApplication.SharedApplication.ConnectedScenes.OfType<UIWindowScene>())
            {
                foreach (UIWindow window in scene.Windows)
                {
                    window.OverrideUserInterfaceStyle = style;
                }
            }
        });
    }
}
