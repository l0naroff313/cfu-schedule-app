using Microsoft.Extensions.DependencyInjection;
using UniversitySchedule.Mobile.Core.Assignments;
using UniversitySchedule.Mobile.Core.Profiles;
using UniversitySchedule.Mobile.Core.Scheduling;

namespace UniversitySchedule.Mobile.Pages;

public partial class ProfilePage : ContentPage
{
    private readonly IServiceProvider _services;
    private readonly ScheduleSession _scheduleSession;
    private readonly PersonalAssignmentStore _assignmentStore;

    public ProfilePage(
        IServiceProvider services,
        ScheduleSession scheduleSession,
        PersonalAssignmentStore assignmentStore)
    {
        _services = services;
        _scheduleSession = scheduleSession;
        _assignmentStore = assignmentStore;
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _scheduleSession.InitializeAsync();
        ShowProfile(_scheduleSession.Profile);
        PersonalAssignment[] assignments = (await _assignmentStore.GetAllAsync()).ToArray();
        int completed = assignments.Count(item => item.Status == PersonalAssignmentStatus.Completed);
        AssignmentCountLabel.Text = assignments.Length.ToString();
        CompletionLabel.Text = assignments.Length == 0
            ? "0%"
            : $"{Math.Round((double)completed / assignments.Length * 100):0}%";
    }

    private async void OnChangeProfileClicked(object? sender, EventArgs e)
    {
        var setupPage = _services.GetRequiredService<ProfileSetupPage>();
        await Navigation.PushModalAsync(new NavigationPage(setupPage));
    }

    private async void OnSettingsClicked(object? sender, EventArgs e)
    {
        var settingsPage = _services.GetRequiredService<SettingsPage>();
        await Navigation.PushModalAsync(settingsPage);
    }

    private async void OnAcademicProfileClicked(object? sender, TappedEventArgs e)
    {
        var setupPage = _services.GetRequiredService<ProfileSetupPage>();
        await Navigation.PushModalAsync(new NavigationPage(setupPage));
    }

    private async void OnAppearanceClicked(object? sender, TappedEventArgs e)
    {
        var settingsPage = _services.GetRequiredService<SettingsPage>();
        await Navigation.PushModalAsync(settingsPage);
    }

    private void ShowProfile(AcademicProfile? profile)
    {
        if (profile is null)
        {
            ProfileNameLabel.Text = "Учебный профиль";
            InstituteLabel.Text = "Учебный профиль пока не настроен.";
            DirectionLabel.Text = string.Empty;
            GroupLabel.Text = string.Empty;
            SyncLabel.Text = string.Empty;
            return;
        }

        ProfileNameLabel.Text = profile.GroupName;
        InstituteLabel.Text = profile.InstituteName;
        DirectionLabel.Text = $"{profile.CourseNumber} курс • {profile.DirectionName}";
        GroupLabel.Text = profile.SubgroupName is null
            ? "Без подгруппы"
            : profile.SubgroupName;
        SyncLabel.Text = _scheduleSession.UpdatedAtUtc is DateTimeOffset updatedAt
            ? $"Последнее обновление: {updatedAt.ToLocalTime():dd.MM.yyyy HH:mm}"
            : "Расписание ещё не синхронизировано.";
    }
}
