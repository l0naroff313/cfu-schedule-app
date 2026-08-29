using UniversitySchedule.Mobile.Core.Profiles;

namespace UniversitySchedule.Mobile.Pages;

public partial class ProfileSetupPage : ContentPage
{
    private readonly ProfileSetupViewModel _viewModel;
    private CancellationTokenSource? _loadCancellation;

    public ProfileSetupPage(ProfileSetupViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        _ = LoadAsync(_loadCancellation.Token);
    }

    protected override void OnDisappearing()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        base.OnDisappearing();
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (await _viewModel.SaveAsync())
        {
            await Navigation.PopModalAsync();
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _viewModel.InitializeAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
