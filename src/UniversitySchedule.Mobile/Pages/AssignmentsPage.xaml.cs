using Microsoft.Extensions.DependencyInjection;
using UniversitySchedule.Mobile.Core.Assignments;

namespace UniversitySchedule.Mobile.Pages;

public partial class AssignmentsPage : ContentPage
{
    private readonly AssignmentsPageViewModel _viewModel;
    private readonly IServiceProvider _services;

    public AssignmentsPage(AssignmentsPageViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _services = services;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async void OnAddAssignmentClicked(object? sender, EventArgs e) => await OpenEditorAsync();

    private async void OnAssignmentSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is AssignmentListItem assignment)
        {
            AssignmentsCollection.SelectedItem = null;
            await OpenEditorAsync(assignment.Id);
        }
    }

    private async void OnEditAssignmentInvoked(object? sender, EventArgs e)
    {
        if (sender is SwipeItem { CommandParameter: Guid id })
        {
            await OpenEditorAsync(id);
        }
    }

    private async void OnDeleteAssignmentInvoked(object? sender, EventArgs e)
    {
        if (sender is not SwipeItem { CommandParameter: Guid id } ||
            !await DisplayAlertAsync("Удалить задание?", "Это действие нельзя отменить.", "Удалить", "Отмена"))
        {
            return;
        }

        await _viewModel.DeleteAsync(id);
    }

    private async void OnToggleCompletedClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: Guid id })
        {
            await _viewModel.ToggleCompletedAsync(id);
        }
    }

    public async Task OpenEditorAsync(Guid? assignmentId = null, Guid? lessonId = null)
    {
        var editor = _services.GetRequiredService<AssignmentEditorPage>();
        editor.Configure(assignmentId, lessonId);
        await Navigation.PushModalAsync(editor);
    }
}
