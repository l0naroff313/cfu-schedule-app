using Microsoft.Extensions.DependencyInjection;
using UniversitySchedule.Contracts.Catalog;
using UniversitySchedule.Mobile.Core.Scheduling;

namespace UniversitySchedule.Mobile.Pages;

public partial class SchedulePage : ContentPage
{
    private readonly SchedulePageViewModel _viewModel;
    private readonly IServiceProvider _services;
    private CancellationTokenSource? _refreshCancellation;

    public SchedulePage(SchedulePageViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _services = services;
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

    private void OnTeacherResultClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: TeacherSummary teacher })
        {
            _viewModel.ChooseTeacher(teacher);
            TeacherSearchBar.Unfocus();
        }
    }

    private async void OnLessonTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not TapGestureRecognizer { CommandParameter: ScheduleLessonListItem lesson })
        {
            return;
        }

        await OpenLessonActionsAsync(lesson);
    }

    private async void OnLessonActionClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: ScheduleLessonListItem lesson })
        {
            await OpenLessonActionsAsync(lesson);
        }
    }

    private async Task OpenLessonActionsAsync(ScheduleLessonListItem lesson)
    {

        string? action = await DisplayActionSheetAsync(
            $"{lesson.PairText} • {lesson.Subject}",
            "Отмена",
            null,
            "Добавить заметку",
            "Добавить задание");
        switch (action)
        {
            case "Добавить заметку":
                var noteEditor = _services.GetRequiredService<NoteEditorPage>();
                noteEditor.Configure(lessonId: lesson.Id);
                await Navigation.PushModalAsync(noteEditor);
                break;
            case "Добавить задание":
                var assignmentEditor = _services.GetRequiredService<AssignmentEditorPage>();
                assignmentEditor.Configure(lessonId: lesson.Id);
                await Navigation.PushModalAsync(assignmentEditor);
                break;
        }
    }

    private void OnWeekDateSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is ScheduleDateItem date)
        {
            _viewModel.SelectedDateItem = date;
        }

        if (sender is CollectionView collectionView)
        {
            collectionView.SelectedItem = null;
        }
    }

    private void StopRefreshLoop()
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = null;
    }
}
