using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using AView = Android.Views.View;

namespace UniversitySchedule.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private const int MaximumSwipeDurationMilliseconds = 700;
    private const float HorizontalDominanceRatio = 1.35f;
    private const float MinimumSwipeDistanceDip = 56f;
    private static WeakReference<MainActivity>? _current;
    private bool _trackingMainTabSwipe;
    private float _swipeStartX;
    private float _swipeStartY;
    private long _swipeStartedAt;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _current = new WeakReference<MainActivity>(this);
        ApplySystemBars(Microsoft.Maui.Controls.Application.Current?.RequestedTheme ?? AppTheme.Unspecified);
    }

    public override bool DispatchTouchEvent(MotionEvent? eventArgs)
    {
        bool dispatched = base.DispatchTouchEvent(eventArgs);
        if (eventArgs is not null)
        {
            ObserveMainTabSwipe(eventArgs);
        }

        return dispatched;
    }

    public static void ApplySystemBars(AppTheme theme)
    {
        if (_current?.TryGetTarget(out MainActivity? activity) == true)
        {
            activity.UpdateSystemBars(theme);
        }
    }

    private void UpdateSystemBars(AppTheme theme)
    {
        bool isLight = theme == AppTheme.Light ||
            (theme == AppTheme.Unspecified &&
             (Microsoft.Maui.Controls.Application.Current?.RequestedTheme ?? AppTheme.Light) == AppTheme.Light);
        string background = isLight ? "#FAFBFD" : "#031325";
        if (!OperatingSystem.IsAndroidVersionAtLeast(35))
        {
            Window?.SetStatusBarColor(Android.Graphics.Color.ParseColor(background));
            Window?.SetNavigationBarColor(Android.Graphics.Color.ParseColor(background));
        }

        if (Window is null)
        {
            return;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            const int AppearanceLightStatusBars = 8;
            const int AppearanceLightNavigationBars = 16;
            int mask = AppearanceLightStatusBars | AppearanceLightNavigationBars;
            Window.InsetsController?.SetSystemBarsAppearance(isLight ? mask : 0, mask);
            return;
        }

        if (Window.DecorView is null)
        {
            return;
        }

        SystemUiFlags flags = SystemUiFlags.Visible;
        if (isLight && OperatingSystem.IsAndroidVersionAtLeast(23))
        {
            flags |= SystemUiFlags.LightStatusBar;
        }

        if (isLight && OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            flags |= SystemUiFlags.LightNavigationBar;
        }

        Window.DecorView.SystemUiFlags = flags;
    }

    private void ObserveMainTabSwipe(MotionEvent eventArgs)
    {
        switch (eventArgs.ActionMasked)
        {
            case MotionEventActions.Down:
                _trackingMainTabSwipe = CanStartMainTabSwipe(eventArgs.RawX, eventArgs.RawY);
                _swipeStartX = eventArgs.RawX;
                _swipeStartY = eventArgs.RawY;
                _swipeStartedAt = eventArgs.EventTime;
                break;
            case MotionEventActions.Up when _trackingMainTabSwipe:
                float distanceX = eventArgs.RawX - _swipeStartX;
                float distanceY = eventArgs.RawY - _swipeStartY;
                long duration = eventArgs.EventTime - _swipeStartedAt;
                _trackingMainTabSwipe = false;
                float density = Resources?.DisplayMetrics?.Density ?? 1f;
                float minimumDistance = MinimumSwipeDistanceDip * density;
                bool isHorizontal = Math.Abs(distanceX) >= minimumDistance &&
                    Math.Abs(distanceX) > Math.Abs(distanceY) * HorizontalDominanceRatio;
                if (isHorizontal && duration <= MaximumSwipeDurationMilliseconds && Shell.Current is AppShell shell)
                {
                    _ = shell.NavigateMainTabAsync(distanceX < 0 ? 1 : -1);
                }

                break;
            case MotionEventActions.Cancel:
                _trackingMainTabSwipe = false;
                break;
        }
    }

    private bool CanStartMainTabSwipe(float rawX, float rawY)
    {
        if (Shell.Current is not AppShell shell || shell.Navigation.ModalStack.Count > 0 || Window?.DecorView is not AView decorView)
        {
            return false;
        }

        AView? target = FindTouchTarget(decorView, rawX, rawY);
        for (AView? view = target; view is not null; view = view.Parent as AView)
        {
            string className = view.Class?.Name ?? view.GetType().Name;
            if (view.Clickable || view is EditText or Android.Widget.Button or AbsSpinner or SeekBar or HorizontalScrollView ||
                className.Contains("SwipeView", StringComparison.OrdinalIgnoreCase) ||
                className.Contains("SearchView", StringComparison.OrdinalIgnoreCase) ||
                className.Contains("NavigationBarItem", StringComparison.OrdinalIgnoreCase) ||
                className.Contains("BottomNavigation", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (view is RecyclerView recyclerView && recyclerView.Parent is not AndroidX.ViewPager2.Widget.ViewPager2 &&
                recyclerView.GetLayoutManager() is LinearLayoutManager layoutManager &&
                layoutManager.Orientation == LinearLayoutManager.Horizontal)
            {
                return false;
            }

            if (ReferenceEquals(view, decorView))
            {
                break;
            }
        }

        return true;
    }

    private static AView? FindTouchTarget(AView view, float rawX, float rawY) =>
        FindDeepestTouchTarget(view, rawX, rawY).View;

    private static (AView? View, int Depth) FindDeepestTouchTarget(AView view, float rawX, float rawY)
    {
        if (!Contains(view, rawX, rawY))
        {
            return (null, -1);
        }

        AView bestView = view;
        int bestDepth = 0;
        if (view is ViewGroup group)
        {
            for (int index = 0; index < group.ChildCount; index++)
            {
                AView? child = group.GetChildAt(index);
                if (child is null || child.Visibility != ViewStates.Visible)
                {
                    continue;
                }

                (AView? candidate, int depth) = FindDeepestTouchTarget(child, rawX, rawY);
                if (candidate is not null && depth + 1 > bestDepth)
                {
                    bestView = candidate;
                    bestDepth = depth + 1;
                }
            }
        }

        return (bestView, bestDepth);
    }

    private static bool Contains(AView view, float rawX, float rawY)
    {
        var location = new int[2];
        view.GetLocationOnScreen(location);
        return rawX >= location[0] && rawX < location[0] + view.Width &&
            rawY >= location[1] && rawY < location[1] + view.Height;
    }
}
