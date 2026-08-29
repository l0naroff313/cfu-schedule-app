using Microsoft.Extensions.DependencyInjection;
using UniversitySchedule.Mobile.Core.Scheduling;
using UniversitySchedule.Mobile.Pages;

namespace UniversitySchedule.Mobile;

public partial class AppShell : Shell
{
    private readonly IServiceProvider _services;
    private readonly ScheduleSession _scheduleSession;
    private bool _startupChecked;

    public AppShell(IServiceProvider services, ScheduleSession scheduleSession)
    {
        _services = services;
        _scheduleSession = scheduleSession;
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
        await _scheduleSession.InitializeAsync();
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
