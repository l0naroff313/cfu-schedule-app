using UniversitySchedule.Mobile.Core.Sync;

namespace UniversitySchedule.Mobile.Pages;

public partial class SyncConflictsPage : ContentPage
{
    private readonly SyncConflictsPageViewModel _viewModel;

    public SyncConflictsPage(SyncConflictsPageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }

    private async void OnKeepLocalClicked(object? sender, EventArgs e)
    {
        if (!TryGetMutationId(sender, out Guid mutationId) ||
            !await DisplayAlertAsync(
                "Оставить версию с устройства?",
                "Она заменит серверную версию при следующей синхронизации.",
                "Оставить мою",
                "Отмена"))
        {
            return;
        }

        await ResolveAsync(() => _viewModel.KeepLocalAsync(mutationId));
    }

    private async void OnKeepServerClicked(object? sender, EventArgs e)
    {
        if (!TryGetMutationId(sender, out Guid mutationId) ||
            !await DisplayAlertAsync(
                "Принять серверную версию?",
                "Все ожидающие локальные изменения этой записи будут удалены.",
                "Принять",
                "Отмена"))
        {
            return;
        }

        await ResolveAsync(() => _viewModel.KeepServerAsync(mutationId));
    }

    private async Task ResolveAsync(Func<Task> resolve)
    {
        try
        {
            await resolve();
        }
        catch (Exception)
        {
            await DisplayAlertAsync(
                "Не удалось разрешить конфликт",
                "Данные сохранены. Попробуйте ещё раз позже.",
                "OK");
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            await _viewModel.LoadAsync();
        }
        catch (Exception)
        {
            await DisplayAlertAsync(
                "Не удалось открыть конфликты",
                "Локальные данные не изменены.",
                "OK");
        }
    }

    private static bool TryGetMutationId(object? sender, out Guid mutationId)
    {
        if (sender is Button { CommandParameter: Guid id })
        {
            mutationId = id;
            return true;
        }

        mutationId = Guid.Empty;
        return false;
    }
}
