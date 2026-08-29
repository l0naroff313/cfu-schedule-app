using Microsoft.Extensions.DependencyInjection;
using UniversitySchedule.Mobile.Core.Profiles;
using UniversitySchedule.Mobile.Core.Scheduling;

namespace UniversitySchedule.Mobile.Pages;

public partial class ProfilePage : ContentPage
{
    private readonly IServiceProvider _services;
    private readonly ScheduleSession _scheduleSession;

    public ProfilePage(IServiceProvider services, ScheduleSession scheduleSession)
    {
        _services = services;
        _scheduleSession = scheduleSession;
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _scheduleSession.InitializeAsync();
        ShowProfile(_scheduleSession.Profile);
    }

    private async void OnChangeProfileClicked(object? sender, EventArgs e)
    {
        var setupPage = _services.GetRequiredService<ProfileSetupPage>();
        await Navigation.PushModalAsync(new NavigationPage(setupPage));
    }

    private async void OnSettingsClicked(object? sender, EventArgs e)
    {
        var settingsPage = _services.GetRequiredService<SettingsPage>();
        await Navigation.PushModalAsync(new NavigationPage(settingsPage));
    }

    private void ShowProfile(AcademicProfile? profile)
    {
        if (profile is null)
        {
            InstituteLabel.Text = "Учебный профиль пока не настроен.";
            DirectionLabel.Text = string.Empty;
            GroupLabel.Text = string.Empty;
            SyncLabel.Text = string.Empty;
            return;
        }

        InstituteLabel.Text = profile.InstituteName;
        DirectionLabel.Text = $"{profile.DirectionName} • {profile.CourseNumber} курс";
        GroupLabel.Text = profile.SubgroupName is null
            ? profile.GroupName
            : $"{profile.GroupName} • {profile.SubgroupName}";
        SyncLabel.Text = _scheduleSession.UpdatedAtUtc is DateTimeOffset updatedAt
            ? $"Последнее обновление: {updatedAt.ToLocalTime():dd.MM.yyyy HH:mm}"
            : "Расписание ещё не синхронизировано.";
    }
}
