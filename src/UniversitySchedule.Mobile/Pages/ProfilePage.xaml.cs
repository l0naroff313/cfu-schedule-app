using Microsoft.Extensions.DependencyInjection;
using UniversitySchedule.Mobile.Controls;
using UniversitySchedule.Mobile.Core.Assignments;
using UniversitySchedule.Mobile.Core.Profiles;
using UniversitySchedule.Mobile.Core.Scheduling;

namespace UniversitySchedule.Mobile.Pages;

public partial class ProfilePage : ContentPage
{
    private readonly IServiceProvider _services;
    private readonly ScheduleSession _scheduleSession;
    private readonly PersonalAssignmentStore _assignmentStore;
    private bool _isPreparingOffline;

    public ProfilePage(
        IServiceProvider services,
        ScheduleSession scheduleSession,
        PersonalAssignmentStore assignmentStore)
    {
        _services = services;
        _scheduleSession = scheduleSession;
        _assignmentStore = assignmentStore;
        InitializeComponent();
        MainTabSwipeNavigation.Attach(Content);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _scheduleSession.InitializeAsync();
        ShowProfile(_scheduleSession.Profile);
        PersonalAssignment[] assignments = (await _assignmentStore.GetAllAsync()).ToArray();
        int completed = assignments.Count(item => item.Status == PersonalAssignmentStatus.Completed);
        double completion = assignments.Length == 0
            ? 0d
            : (double)completed / assignments.Length;
        AssignmentCountLabel.Text = assignments.Length.ToString();
        CompletionLabel.Text = $"{Math.Round(completion * 100):0}%";
        CompletionRing.Drawable = CreateCompletionRing(completion);
        CompletionRing.Invalidate();
        await RefreshOfflineStatusAsync();
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

    private async void OnDownloadOfflineClicked(object? sender, EventArgs e)
    {
        if (_isPreparingOffline)
        {
            return;
        }

        _isPreparingOffline = true;
        OfflineDownloadButton.IsEnabled = false;
        OfflineDownloadButton.Text = "Проверяем офлайн-версию…";
        OfflineStatusLabel.Text = "Сохраняем полное расписание выбранной группы…";
        try
        {
            OfflineSchedulePreparationResult result = await _scheduleSession.PrepareOfflineAsync();
            ShowOfflineStatus(result.Readiness);
            if (!result.DownloadedFromNetwork)
            {
                OfflineStatusLabel.Text += " • используется последняя сохранённая копия";
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            await RefreshOfflineStatusAsync();
            await DisplayAlertAsync(
                "Офлайн-версия",
                "Не удалось обновить офлайн-данные. Проверьте интернет и повторите.",
                "Хорошо");
        }
        finally
        {
            _isPreparingOffline = false;
            OfflineDownloadButton.IsEnabled = _scheduleSession.Profile is not null;
        }
    }

    private async Task RefreshOfflineStatusAsync()
    {
        OfflineScheduleReadiness readiness = await _scheduleSession.CheckOfflineReadinessAsync();
        ShowOfflineStatus(readiness);
    }

    private void ShowOfflineStatus(OfflineScheduleReadiness readiness)
    {
        OfflineStatusDot.BackgroundColor = Color.FromArgb(readiness.IsReady ? "#82D21E" : "#E5484D");
        OfflineStatusLabel.Text = readiness.IsReady
            ? $"Готово • {readiness.LessonCount} занятий • данные от {readiness.UpdatedAtUtc?.ToLocalTime():dd.MM.yyyy HH:mm}"
            : "Расписание выбранной группы ещё не сохранено";
        OfflineDownloadButton.Text = readiness.IsReady
            ? "Обновить офлайн-данные"
            : "Скачать данные для офлайна";
        OfflineDownloadButton.IsEnabled = !_isPreparingOffline && _scheduleSession.Profile is not null;
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

    private static CompletionRingDrawable CreateCompletionRing(double completion)
    {
        bool isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        Color trackColor = Color.FromArgb(isDark ? "#1A3855" : "#E8EDF4");
        return new CompletionRingDrawable(completion, trackColor, Color.FromArgb("#82D21E"));
    }
}
