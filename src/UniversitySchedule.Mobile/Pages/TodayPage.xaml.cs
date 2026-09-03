using Microsoft.Extensions.DependencyInjection;
using UniversitySchedule.Mobile.Core.Scheduling;

namespace UniversitySchedule.Mobile.Pages;

public partial class TodayPage : ContentPage
{
    private readonly TodayPageViewModel _viewModel;
    private readonly IServiceProvider _services;
    private CancellationTokenSource? _loadCancellation;

    public TodayPage(TodayPageViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        MainTabSwipeNavigation.Attach(Content);
        _viewModel = viewModel;
        _services = services;
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

    private async void OnLessonCardTapped(object? sender, TappedEventArgs e)
    {
        if (sender is TapGestureRecognizer { CommandParameter: TodayLessonCard lesson })
        {
            await OpenLessonActionsAsync(lesson.Id, lesson.Subject, lesson.PairText);
        }
    }

    private async void OnScheduleRowTapped(object? sender, TappedEventArgs e)
    {
        if (sender is TapGestureRecognizer { CommandParameter: TodayScheduleRow lesson })
        {
            await OpenLessonActionsAsync(lesson.Id, lesson.Subject, $"{lesson.PairText} пара");
        }
    }

    private async void OnAssignmentTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not TapGestureRecognizer { CommandParameter: Guid id })
        {
            return;
        }

        var editor = _services.GetRequiredService<AssignmentEditorPage>();
        editor.Configure(assignmentId: id);
        await Navigation.PushModalAsync(editor);
    }

    private async Task OpenLessonActionsAsync(Guid lessonId, string subject, string pairText)
    {
        string? action = await DisplayActionSheetAsync(
            $"{pairText} • {subject}",
            "Отмена",
            null,
            "Добавить заметку",
            "Добавить задание");
        switch (action)
        {
            case "Добавить заметку":
                var noteEditor = _services.GetRequiredService<NoteEditorPage>();
                noteEditor.Configure(lessonId: lessonId);
                await Navigation.PushModalAsync(noteEditor);
                break;
            case "Добавить задание":
                var assignmentEditor = _services.GetRequiredService<AssignmentEditorPage>();
                assignmentEditor.Configure(lessonId: lessonId);
                await Navigation.PushModalAsync(assignmentEditor);
                break;
        }
    }
}
