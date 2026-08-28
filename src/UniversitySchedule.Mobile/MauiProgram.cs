using Microsoft.Extensions.Logging;
using UniversitySchedule.Mobile.Core.Scheduling;
using UniversitySchedule.Mobile.Pages;

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
        builder.Services.AddTransient<SchedulePageViewModel>();
        builder.Services.AddTransient<TodayPage>();
        builder.Services.AddTransient<SchedulePage>();
        builder.Services.AddTransient<AssignmentsPage>();
        builder.Services.AddTransient<NotesPage>();
        builder.Services.AddTransient<ProfilePage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
