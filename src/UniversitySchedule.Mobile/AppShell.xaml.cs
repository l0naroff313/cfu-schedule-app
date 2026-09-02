using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UniversitySchedule.Mobile.Core.Identity;
using UniversitySchedule.Mobile.Core.Scheduling;
using UniversitySchedule.Mobile.Core.Sync;
using UniversitySchedule.Mobile.Pages;
using UniversitySchedule.Mobile.Services;

namespace UniversitySchedule.Mobile;

public partial class AppShell : Shell
{
    private readonly IServiceProvider _services;
    private readonly ScheduleSession _scheduleSession;
    private readonly InstallationIdentityService _installationIdentity;
    private readonly PersonalDataSyncCoordinator _syncCoordinator;
    private readonly ConnectivitySyncService _connectivitySync;
    private readonly DailyScheduleRefreshService _dailyScheduleRefresh;
    private readonly ILogger<AppShell> _logger;
    private bool _startupChecked;

    public AppShell(
        IServiceProvider services,
        ScheduleSession scheduleSession,
        InstallationIdentityService installationIdentity,
        PersonalDataSyncCoordinator syncCoordinator,
        ConnectivitySyncService connectivitySync,
        DailyScheduleRefreshService dailyScheduleRefresh,
        ILogger<AppShell> logger)
    {
        _services = services;
        _scheduleSession = scheduleSession;
        _installationIdentity = installationIdentity;
        _syncCoordinator = syncCoordinator;
        _connectivitySync = connectivitySync;
        _dailyScheduleRefresh = dailyScheduleRefresh;
        _logger = logger;
        InitializeComponent();

        TodayTab.ContentTemplate = ResolvePage<TodayPage>(services);
        ScheduleTab.ContentTemplate = ResolvePage<SchedulePage>(services);
        AssignmentsTab.ContentTemplate = ResolvePage<AssignmentsPage>(services);
        NotesTab.ContentTemplate = ResolvePage<NotesPage>(services);
        ProfileTab.ContentTemplate = ResolvePage<ProfilePage>(services);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_startupChecked)
        {
            return;
        }

        _startupChecked = true;
#if VISUAL_SNAPSHOTS
        if (VisualSnapshotOptions.TryRead(out VisualSnapshotOptions visualOptions))
        {
            VisualSnapshotService visualSnapshots =
                _services.GetRequiredService<VisualSnapshotService>();
            await visualSnapshots.PrepareProfileAsync();
            try
            {
                await _scheduleSession.InitializeAsync();
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Visual snapshot schedule refresh failed");
            }

            await visualSnapshots.SeedPersonalDataAsync(_scheduleSession.Snapshot);
            await GoToAsync($"//{visualOptions.Route}", animate: false);
            await visualSnapshots.MarkReadyAsync(visualOptions);
            return;
        }
#endif
        try
        {
            await _installationIdentity.GetOrCreateAsync();
            _syncCoordinator.StartBackgroundSynchronization();
            _connectivitySync.Start();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Secure installation identity could not be initialized");
        }

        await _scheduleSession.InitializeAsync();
        _dailyScheduleRefresh.Start();
        if (_scheduleSession.Profile is null)
        {
            var setupPage = _services.GetRequiredService<ProfileSetupPage>();
            await Navigation.PushModalAsync(new NavigationPage(setupPage));
        }
    }

    private static DataTemplate ResolvePage<TPage>(IServiceProvider services)
        where TPage : Page
    {
        return new DataTemplate(() => services.GetRequiredService<TPage>());
    }
}
