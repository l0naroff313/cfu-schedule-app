using Microsoft.Extensions.DependencyInjection;
using UniversitySchedule.Mobile.Core.Identity;
using UniversitySchedule.Mobile.Core.Sync;
using UniversitySchedule.Mobile.Services;

namespace UniversitySchedule.Mobile.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly ThemeSettingsService _themeSettings;
    private readonly InstallationIdentityService _installationIdentity;
    private readonly PersonalDataConflictResolutionService _conflictResolution;
    private readonly IServiceProvider _services;
    private string? _installationId;

    public SettingsPage(
        ThemeSettingsService themeSettings,
        InstallationIdentityService installationIdentity,
        PersonalDataConflictResolutionService conflictResolution,
        IServiceProvider services)
    {
        _themeSettings = themeSettings;
        _installationIdentity = installationIdentity;
        _conflictResolution = conflictResolution;
        _services = services;
        InitializeComponent();
        RefreshStatus();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadInstallationIdentityAsync();
        await LoadSyncConflictStatusAsync();
    }

    private void OnSystemThemeClicked(object? sender, EventArgs e) => SetTheme(AppTheme.Unspecified);

    private void OnLightThemeClicked(object? sender, EventArgs e) => SetTheme(AppTheme.Light);

    private void OnDarkThemeClicked(object? sender, EventArgs e) => SetTheme(AppTheme.Dark);

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }

    private async void OnCopyInstallationIdClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_installationId))
        {
            return;
        }

        await Clipboard.Default.SetTextAsync(_installationId);
        await DisplayAlertAsync("Готово", "Идентификатор установки скопирован.", "OK");
    }

    private async void OnOpenSyncConflictsClicked(object? sender, EventArgs e)
    {
        var page = _services.GetRequiredService<SyncConflictsPage>();
        await Navigation.PushModalAsync(page);
    }

    private void SetTheme(AppTheme theme)
    {
        _themeSettings.SetTheme(theme);
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        CurrentThemeLabel.Text = _themeSettings.CurrentTheme switch
        {
            AppTheme.Light => "Сейчас выбрана светлая тема",
            AppTheme.Dark => "Сейчас выбрана тёмная тема",
            _ => "Сейчас используется тема устройства",
        };
    }

    private async Task LoadInstallationIdentityAsync()
    {
        try
        {
            InstallationIdentity identity = await _installationIdentity.GetOrCreateAsync();
            _installationId = identity.DisplayId;
            InstallationIdLabel.Text = identity.DisplayId;
            CopyInstallationIdButton.IsEnabled = true;
        }
        catch (Exception)
        {
            _installationId = null;
            InstallationIdLabel.Text = "Защищённое хранилище недоступно";
            CopyInstallationIdButton.IsEnabled = false;
        }
    }

    private async Task LoadSyncConflictStatusAsync()
    {
        try
        {
            int count = (await _conflictResolution.GetConflictsAsync()).Count;
            SyncConflictStatusLabel.Text = count switch
            {
                0 => "Конфликтов нет",
                1 => "Найден 1 конфликт",
                _ => $"Найдено конфликтов: {count}",
            };
        }
        catch (Exception)
        {
            SyncConflictStatusLabel.Text = "Не удалось проверить состояние";
        }
    }
}
