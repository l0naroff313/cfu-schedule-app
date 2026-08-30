using Foundation;
using UIKit;

namespace UniversitySchedule.Mobile;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        ConfigureTabBarAppearance();
        return base.FinishedLaunching(application, launchOptions);
    }

    private static void ConfigureTabBarAppearance()
    {
        var appearance = new UITabBarAppearance();
        appearance.ConfigureWithOpaqueBackground();
        appearance.BackgroundColor = UIColor.FromDynamicProvider(traits =>
            traits.UserInterfaceStyle == UIUserInterfaceStyle.Dark
                ? UIColor.FromRGB(6, 26, 47)
                : UIColor.White);
        appearance.ShadowColor = UIColor.FromDynamicProvider(traits =>
            traits.UserInterfaceStyle == UIUserInterfaceStyle.Dark
                ? UIColor.FromRGB(16, 43, 70)
                : UIColor.FromRGB(232, 237, 244));

        UITabBar.Appearance.StandardAppearance = appearance;
        UITabBar.Appearance.ScrollEdgeAppearance = appearance;
    }
}
