using Microsoft.Extensions.DependencyInjection;
using UniversitySchedule.Mobile.Pages;

namespace UniversitySchedule.Mobile;

public partial class AppShell : Shell
{
    public AppShell(IServiceProvider services)
    {
        InitializeComponent();

        TodayTab.ContentTemplate = ResolvePage<TodayPage>(services);
        ScheduleTab.ContentTemplate = ResolvePage<SchedulePage>(services);
        AssignmentsTab.ContentTemplate = ResolvePage<AssignmentsPage>(services);
        NotesTab.ContentTemplate = ResolvePage<NotesPage>(services);
        ProfileTab.ContentTemplate = ResolvePage<ProfilePage>(services);
    }

    private static DataTemplate ResolvePage<TPage>(IServiceProvider services)
        where TPage : Page
    {
        return new DataTemplate(() => services.GetRequiredService<TPage>());
    }
}
