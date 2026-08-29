using UniversitySchedule.Mobile.Services;

namespace UniversitySchedule.Mobile.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly ThemeSettingsService _themeSettings;

    public SettingsPage(ThemeSettingsService themeSettings)
    {
        _themeSettings = themeSettings;
        InitializeComponent();
        RefreshStatus();
    }

    private void OnSystemThemeClicked(object? sender, EventArgs e) => SetTheme(AppTheme.Unspecified);

    private void OnLightThemeClicked(object? sender, EventArgs e) => SetTheme(AppTheme.Light);

    private void OnDarkThemeClicked(object? sender, EventArgs e) => SetTheme(AppTheme.Dark);

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
}
