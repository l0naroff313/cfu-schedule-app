using Microsoft.Extensions.Logging;
using System.Reflection;
using UniversitySchedule.Mobile.Core.Assignments;
using UniversitySchedule.Mobile.Core.Catalog;
using UniversitySchedule.Mobile.Core.Cfu;
using UniversitySchedule.Mobile.Core.Identity;
using UniversitySchedule.Mobile.Core.Notes;
using UniversitySchedule.Mobile.Core.Profiles;
using UniversitySchedule.Mobile.Core.Scheduling;
using UniversitySchedule.Mobile.Core.Sync;
using UniversitySchedule.Mobile.Pages;
using UniversitySchedule.Mobile.Services;
using UniversitySchedule.Mobile.Storage;

namespace UniversitySchedule.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
#if IOS
        IosSearchBarAppearance.Configure();
#endif
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton<AppShell>();
#if VISUAL_SNAPSHOTS
        builder.Services.AddSingleton<TimeProvider, VisualSnapshotTimeProvider>();
        builder.Services.AddSingleton<VisualSnapshotService>();
#else
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
#endif
        builder.Services.AddSingleton<UniversitySchedule.Mobile.Core.Storage.ILocalDataStore, SqliteLocalDataStore>();
        builder.Services.AddSingleton<ISecureValueStore, MauiSecureValueStore>();
        builder.Services.AddSingleton<InstallationIdentityService>();
        UniversityScheduleApiOptions apiOptions = CreateApiOptions();
        builder.Services.AddSingleton(apiOptions);
        builder.Services.AddSingleton(services =>
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15),
            };
            if (apiOptions.BaseAddress is not null)
            {
                client.BaseAddress = apiOptions.BaseAddress;
            }

            client.DefaultRequestHeaders.UserAgent.ParseAdd("CFU-ElJournal/1.0.3");
            return new UniversityScheduleApiClient(
                client,
                apiOptions,
                services.GetRequiredService<InstallationIdentityService>(),
                services.GetRequiredService<ISecureValueStore>(),
                services.GetRequiredService<TimeProvider>());
        });
        builder.Services.AddSingleton<PersonalDataSyncQueue>();
        builder.Services.AddSingleton<PersonalDataSynchronizer>();
        builder.Services.AddSingleton<PersonalDataSnapshotRestorer>();
        builder.Services.AddSingleton<Func<PersonalDataSnapshotRestorer>>(services =>
            () => services.GetRequiredService<PersonalDataSnapshotRestorer>());
        builder.Services.AddSingleton<PersonalDataSyncCoordinator>();
        builder.Services.AddSingleton<PersonalDataConflictResolutionService>();
        builder.Services.AddSingleton<IPersonalDataChangeSink>(services =>
            services.GetRequiredService<PersonalDataSyncCoordinator>());
        builder.Services.AddSingleton<ConnectivitySyncService>();
        builder.Services.AddSingleton<IReferenceCatalogProvider, EmbeddedReferenceCatalogProvider>();
        builder.Services.AddSingleton<PersonalNoteStore>();
        builder.Services.AddSingleton<PersonalAssignmentStore>();
        builder.Services.AddSingleton<ThemeSettingsService>();
        builder.Services.AddSingleton<AcademicProfileStore>();
        builder.Services.AddSingleton(_ =>
        {
            var client = new HttpClient
            {
                BaseAddress = new Uri(CfuScheduleRepository.BaseAddress),
                Timeout = TimeSpan.FromSeconds(15),
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CFU-ElJournal/1.0.3");
            return client;
        });
        builder.Services.AddSingleton<CfuScheduleRepository>();
        builder.Services.AddSingleton<ScheduleSession>();
        builder.Services.AddSingleton<DailyScheduleRefreshService>();
        builder.Services.AddTransient<ProfileSetupViewModel>();
        builder.Services.AddTransient<TodayPageViewModel>();
        builder.Services.AddTransient<SchedulePageViewModel>();
        builder.Services.AddTransient<NotesPageViewModel>();
        builder.Services.AddTransient<AssignmentsPageViewModel>();
        builder.Services.AddTransient<SyncConflictsPageViewModel>();
        builder.Services.AddTransient<TodayPage>();
        builder.Services.AddTransient<SchedulePage>();
        builder.Services.AddTransient<AssignmentsPage>();
        builder.Services.AddTransient<NotesPage>();
        builder.Services.AddTransient<ProfilePage>();
        builder.Services.AddTransient<ProfileSetupPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<NoteEditorPage>();
        builder.Services.AddTransient<AssignmentEditorPage>();
        builder.Services.AddTransient<SyncConflictsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static UniversityScheduleApiOptions CreateApiOptions()
    {
        string? configuredAddress = typeof(MauiProgram).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "UniversityScheduleApiBaseUrl")
            ?.Value;
        Uri? baseAddress = null;
        if (!string.IsNullOrWhiteSpace(configuredAddress))
        {
            string normalizedAddress = configuredAddress.EndsWith("/", StringComparison.Ordinal)
                ? configuredAddress
                : $"{configuredAddress}/";
            Uri.TryCreate(normalizedAddress, UriKind.Absolute, out baseAddress);
        }

        string platform = DeviceInfo.Current.Platform == DevicePlatform.Android
            ? "android"
            : "ios";
        return new UniversityScheduleApiOptions(
            baseAddress,
            platform,
            AppInfo.Current.VersionString);
    }
}
