using UniversitySchedule.Mobile.Core.Sync;

namespace UniversitySchedule.Mobile.Services;

public sealed class ConnectivitySyncService(PersonalDataSyncCoordinator syncCoordinator)
{
    private int _started;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
        if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
        {
            syncCoordinator.StartBackgroundSynchronization();
        }
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs args)
    {
        if (args.NetworkAccess == NetworkAccess.Internet)
        {
            syncCoordinator.StartBackgroundSynchronization();
        }
    }
}
