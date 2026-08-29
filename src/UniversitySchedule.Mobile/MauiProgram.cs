using Microsoft.Extensions.Logging;
using UniversitySchedule.Mobile.Core.Assignments;
using UniversitySchedule.Mobile.Core.Catalog;
using UniversitySchedule.Mobile.Core.Cfu;
using UniversitySchedule.Mobile.Core.Identity;
using UniversitySchedule.Mobile.Core.Notes;
using UniversitySchedule.Mobile.Core.Profiles;
using UniversitySchedule.Mobile.Core.Scheduling;
using UniversitySchedule.Mobile.Pages;
using UniversitySchedule.Mobile.Services;
using UniversitySchedule.Mobile.Storage;

namespace UniversitySchedule.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<UniversitySchedule.Mobile.Core.Storage.ILocalDataStore, SqliteLocalDataStore>();
        builder.Services.AddSingleton<ISecureValueStore, MauiSecureValueStore>();
        builder.Services.AddSingleton<InstallationIdentityService>();
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
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CFU-ElJournal/1.0");
            return client;
        });
        builder.Services.AddSingleton<CfuScheduleRepository>();
        builder.Services.AddSingleton<ScheduleSession>();
        builder.Services.AddTransient<ProfileSetupViewModel>();
        builder.Services.AddTransient<TodayPageViewModel>();
        builder.Services.AddTransient<SchedulePageViewModel>();
        builder.Services.AddTransient<NotesPageViewModel>();
        builder.Services.AddTransient<AssignmentsPageViewModel>();
        builder.Services.AddTransient<TodayPage>();
        builder.Services.AddTransient<SchedulePage>();
        builder.Services.AddTransient<AssignmentsPage>();
        builder.Services.AddTransient<NotesPage>();
        builder.Services.AddTransient<ProfilePage>();
        builder.Services.AddTransient<ProfileSetupPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<NoteEditorPage>();
        builder.Services.AddTransient<AssignmentEditorPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
