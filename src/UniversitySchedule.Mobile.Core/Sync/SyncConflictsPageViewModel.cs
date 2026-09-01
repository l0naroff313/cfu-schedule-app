using System.Collections.ObjectModel;
using UniversitySchedule.Mobile.Core.Presentation;

namespace UniversitySchedule.Mobile.Core.Sync;

public sealed class SyncConflictsPageViewModel(
    PersonalDataConflictResolutionService resolutionService,
    PersonalDataSyncCoordinator syncCoordinator) : ObservableObject
{
    private bool _isLoading;
    private bool _isResolving;
    private string? _statusMessage;

    public ObservableCollection<PersonalDataConflictItem> Conflicts { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool IsResolving
    {
        get => _isResolving;
        private set => SetProperty(ref _isResolving, value);
    }

    public bool HasConflicts => Conflicts.Count > 0;

    public bool HasNoConflicts => !HasConflicts && !IsLoading;

    public string ConflictCountText => Conflicts.Count switch
    {
        0 => "Все изменения согласованы",
        1 => "Требует решения 1 конфликт",
        _ => $"Требуют решения: {Conflicts.Count}",
    };

    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            IReadOnlyList<PersonalDataConflictItem> conflicts =
                await resolutionService.GetConflictsAsync(cancellationToken);
            Conflicts.Clear();
            foreach (PersonalDataConflictItem conflict in conflicts)
            {
                Conflicts.Add(conflict);
            }

            StatusMessage = null;
            NotifyCollectionState();
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasNoConflicts));
        }
    }

    public Task KeepLocalAsync(Guid mutationId, CancellationToken cancellationToken = default) =>
        ResolveAsync(
            mutationId,
            resolutionService.KeepLocalAsync,
            "Локальная версия поставлена в очередь синхронизации.",
            cancellationToken);

    public Task KeepServerAsync(Guid mutationId, CancellationToken cancellationToken = default) =>
        ResolveAsync(
            mutationId,
            resolutionService.KeepServerAsync,
            "Серверная версия сохранена на устройстве.",
            cancellationToken);

    private async Task ResolveAsync(
        Guid mutationId,
        Func<Guid, CancellationToken, Task> resolution,
        string successMessage,
        CancellationToken cancellationToken)
    {
        if (IsResolving)
        {
            return;
        }

        IsResolving = true;
        try
        {
            await resolution(mutationId, cancellationToken);
            PersonalDataConflictItem? item = Conflicts.FirstOrDefault(conflict =>
                conflict.MutationId == mutationId);
            if (item is not null)
            {
                Conflicts.Remove(item);
            }

            StatusMessage = successMessage;
            NotifyCollectionState();
            syncCoordinator.StartBackgroundSynchronization();
        }
        finally
        {
            IsResolving = false;
        }
    }

    private void NotifyCollectionState()
    {
        OnPropertyChanged(nameof(HasConflicts));
        OnPropertyChanged(nameof(HasNoConflicts));
        OnPropertyChanged(nameof(ConflictCountText));
    }
}
