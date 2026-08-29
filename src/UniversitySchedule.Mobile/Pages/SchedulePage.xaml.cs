using UniversitySchedule.Mobile.Core.Scheduling;

namespace UniversitySchedule.Mobile.Pages;

public partial class SchedulePage : ContentPage
{
    private readonly SchedulePageViewModel _viewModel;
    private CancellationTokenSource? _refreshCancellation;

    public SchedulePage(SchedulePageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        StopRefreshLoop();
        _viewModel.RefreshTeacherLocation();

        _refreshCancellation = new CancellationTokenSource();
        _ = LoadAsync(_refreshCancellation.Token);
        _ = RefreshTeacherLocationAsync(_refreshCancellation.Token);
    }

    protected override void OnDisappearing()
    {
        StopRefreshLoop();
        base.OnDisappearing();
    }

    private async Task RefreshTeacherLocationAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                Dispatcher.Dispatch(_viewModel.RefreshTeacherLocation);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _viewModel.LoadAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async void OnTeacherSearchButtonPressed(object? sender, EventArgs e)
    {
        try
        {
            await _viewModel.SearchTeachersAsync(_viewModel.TeacherQuery);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void StopRefreshLoop()
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = null;
    }
}
