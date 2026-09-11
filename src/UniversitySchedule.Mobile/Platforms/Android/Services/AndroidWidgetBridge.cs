using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.OS;
using Microsoft.Maui.Storage;

namespace UniversitySchedule.Mobile;

internal static class AndroidWidgetBridge
{
    internal const string StateKey = "cfu_widget_state_v1";
    internal const string RefreshAction = "io.github.l0naroff313.cfuschedule.WIDGET_REFRESH";

    public static Task WriteAsync(string json, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Preferences.Default.Set(StateKey, json);
        UpdateAll(Android.App.Application.Context);
        return Task.CompletedTask;
    }

    internal static void UpdateAll(Context context)
    {
        AppWidgetManager manager = AppWidgetManager.GetInstance(context)!;
        ComponentName component = new(context, Java.Lang.Class.FromType(typeof(CfuScheduleWidgetProvider)));
        int[] ids = manager.GetAppWidgetIds(component) ?? [];
        if (ids.Length == 0) return;
        Intent update = new Intent(AppWidgetManager.ActionAppwidgetUpdate)
            .SetPackage(context.PackageName!)
            .PutExtra(AppWidgetManager.ExtraAppwidgetIds, ids);
        context.SendBroadcast(update);
    }

    internal static void ScheduleRefresh(Context context)
    {
        AlarmManager? alarm = context.GetSystemService(Context.AlarmService) as AlarmManager;
        if (alarm is null) return;
        Intent refreshIntent = new Intent(RefreshAction).SetPackage(context.PackageName!);
        PendingIntentFlags flags = OperatingSystem.IsAndroidVersionAtLeast(23)
            ? PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable
            : PendingIntentFlags.UpdateCurrent;
        PendingIntent pending = PendingIntent.GetBroadcast(
            context,
            1506,
            refreshIntent,
            flags)!;
        alarm.SetInexactRepeating(
            AlarmType.ElapsedRealtime,
            SystemClock.ElapsedRealtime() + 15 * 60 * 1000,
            15 * 60 * 1000,
            pending);
    }
}
