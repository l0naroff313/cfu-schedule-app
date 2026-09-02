using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UniversitySchedule.Mobile.Core.Scheduling;
using UniversitySchedule.Mobile.Services;

namespace UniversitySchedule.Mobile;

public partial class App : Application
{
    private readonly AppShell _appShell;
    private readonly DailyScheduleRefreshService _dailyScheduleRefresh;
    private readonly ILogger<App> _logger;

    public App(
        AppShell appShell,
        ThemeSettingsService themeSettings,
        DailyScheduleRefreshService dailyScheduleRefresh,
        ILogger<App> logger)
    {
        _appShell = appShell;
        _dailyScheduleRefresh = dailyScheduleRefresh;
        _logger = logger;
        InitializeComponent();
        themeSettings.ApplySavedTheme();
#if VISUAL_SNAPSHOTS
        if (VisualSnapshotOptions.TryRead(out VisualSnapshotOptions options))
        {
            UserAppTheme = options.Theme;
        }
#endif
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_appShell);
        window.Activated += OnWindowActivated;
        return window;
    }

    private async void OnWindowActivated(object? sender, EventArgs eventArgs)
    {
        try
        {
            await _dailyScheduleRefresh.CheckNowAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            _logger.LogWarning(exception, "Automatic schedule refresh on activation failed");
        }
    }
}
