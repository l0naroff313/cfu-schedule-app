namespace UniversitySchedule.Mobile.Pages;

internal static class MainTabSwipeNavigation
{
    public static void Attach(View? surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

#if ANDROID
        // Android observes gestures before nested ScrollView/CollectionView controls consume them.
        return;
#else
        var gesture = new SwipeGestureRecognizer
        {
            Direction = SwipeDirection.Left | SwipeDirection.Right,
        };
        gesture.Swiped += OnSwiped;
        surface.GestureRecognizers.Add(gesture);
#endif
    }

    private static async void OnSwiped(object? sender, SwipedEventArgs eventArgs)
    {
        int direction = eventArgs.Direction switch
        {
            SwipeDirection.Left => 1,
            SwipeDirection.Right => -1,
            _ => 0,
        };

        if (direction != 0 && Shell.Current is AppShell shell)
        {
            await shell.NavigateMainTabAsync(direction);
        }
    }
}
