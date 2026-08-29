using Microsoft.Extensions.DependencyInjection;
using UniversitySchedule.Mobile.Services;

namespace UniversitySchedule.Mobile;

public partial class App : Application
{
    private readonly AppShell _appShell;

    public App(AppShell appShell, ThemeSettingsService themeSettings)
    {
        _appShell = appShell;
        InitializeComponent();
        themeSettings.ApplySavedTheme();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(_appShell);
    }
}
