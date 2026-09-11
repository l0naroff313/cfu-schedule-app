using System.Text.Json;
using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Microsoft.Maui.Storage;
using UniversitySchedule.Mobile.Core.Widgets;

namespace UniversitySchedule.Mobile;

[BroadcastReceiver(
    Enabled = true,
    Exported = false,
    Label = "КФУ ЭлЖур",
    Permission = "android.permission.BIND_APPWIDGET")]
[IntentFilter(new[] {
    AppWidgetManager.ActionAppwidgetUpdate,
    Intent.ActionBootCompleted,
    AndroidWidgetBridge.RefreshAction
})]
[MetaData("android.appwidget.provider", Resource = "@xml/cfu_schedule_widget_info")]
public sealed class CfuScheduleWidgetProvider : AppWidgetProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public override void OnEnabled(Context? context)
    {
        if (context is not null) AndroidWidgetBridge.ScheduleRefresh(context);
        base.OnEnabled(context);
    }

    public override void OnUpdate(Context? context, AppWidgetManager? manager, int[]? appWidgetIds)
    {
        if (context is null || manager is null || appWidgetIds is null) return;
        ScheduleWidgetState state = ReadState();
        foreach (int id in appWidgetIds) manager.UpdateAppWidget(id, CreateRemoteViews(context, state));
        AndroidWidgetBridge.ScheduleRefresh(context);
    }

    public override void OnReceive(Context? context, Intent? intent)
    {
        base.OnReceive(context, intent);
        if (context is null) return;
        if (intent?.Action == Intent.ActionBootCompleted)
        {
            AndroidWidgetBridge.ScheduleRefresh(context);
            AndroidWidgetBridge.UpdateAll(context);
            return;
        }

        if (intent?.Action == AndroidWidgetBridge.RefreshAction)
        {
            AndroidWidgetBridge.UpdateAll(context);
        }
    }

    private static ScheduleWidgetState ReadState()
    {
        string? json = Preferences.Default.Get<string?>(AndroidWidgetBridge.StateKey, null);
        if (string.IsNullOrWhiteSpace(json)) return ScheduleWidgetState.Empty(DateTimeOffset.UtcNow);
        try { return JsonSerializer.Deserialize<ScheduleWidgetState>(json, JsonOptions) ?? ScheduleWidgetState.Empty(DateTimeOffset.UtcNow); }
        catch (JsonException) { return ScheduleWidgetState.Empty(DateTimeOffset.UtcNow); }
    }

    private static RemoteViews CreateRemoteViews(Context context, ScheduleWidgetState state)
    {
        var views = new RemoteViews(context.PackageName, Resource.Layout.cfu_schedule_widget);
        views.SetTextViewText(Resource.Id.widget_group, state.GroupText);
        views.SetTextViewText(Resource.Id.widget_header_title, state.HeaderTitle);
        views.SetTextViewText(Resource.Id.widget_header_subtitle, state.HeaderSubtitle);
        views.SetTextViewText(Resource.Id.widget_header_time, state.HeaderTime);
        views.SetTextViewText(Resource.Id.widget_header_classroom, state.HeaderClassroom);
        views.SetTextViewText(Resource.Id.widget_day_label, state.DayLabel);
        views.SetProgressBar(Resource.Id.widget_progress, 100, (int)Math.Round(state.Progress * 100), false);
        views.SetOnClickPendingIntent(Resource.Id.widget_root, OpenApp(context));

        int[] rowIds = [Resource.Id.widget_row_1, Resource.Id.widget_row_2, Resource.Id.widget_row_3,
            Resource.Id.widget_row_4, Resource.Id.widget_row_5, Resource.Id.widget_row_6];
        int[] pairIds = [Resource.Id.widget_pair_1, Resource.Id.widget_pair_2, Resource.Id.widget_pair_3,
            Resource.Id.widget_pair_4, Resource.Id.widget_pair_5, Resource.Id.widget_pair_6];
        int[] subjectIds = [Resource.Id.widget_subject_1, Resource.Id.widget_subject_2, Resource.Id.widget_subject_3,
            Resource.Id.widget_subject_4, Resource.Id.widget_subject_5, Resource.Id.widget_subject_6];
        int[] timeIds = [Resource.Id.widget_time_1, Resource.Id.widget_time_2, Resource.Id.widget_time_3,
            Resource.Id.widget_time_4, Resource.Id.widget_time_5, Resource.Id.widget_time_6];
        int[] roomIds = [Resource.Id.widget_room_1, Resource.Id.widget_room_2, Resource.Id.widget_room_3,
            Resource.Id.widget_room_4, Resource.Id.widget_room_5, Resource.Id.widget_room_6];
        int[] noteIds = [Resource.Id.widget_note_1, Resource.Id.widget_note_2, Resource.Id.widget_note_3,
            Resource.Id.widget_note_4, Resource.Id.widget_note_5, Resource.Id.widget_note_6];
        for (int i = 0; i < rowIds.Length; i++)
        {
            bool visible = i < state.Lessons.Count;
            views.SetViewVisibility(rowIds[i], visible ? ViewStates.Visible : ViewStates.Gone);
            if (!visible) continue;
            ScheduleWidgetLesson lesson = state.Lessons[i];
            views.SetTextViewText(pairIds[i], lesson.PairNumber.ToString());
            views.SetTextViewText(subjectIds[i], lesson.Subject);
            views.SetTextViewText(timeIds[i], lesson.TimeText);
            views.SetTextViewText(roomIds[i], lesson.Classroom);
            views.SetViewVisibility(noteIds[i], lesson.HasNote ? ViewStates.Visible : ViewStates.Invisible);
        }
        return views;
    }

    private static PendingIntent OpenApp(Context context)
    {
        Intent launch = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!)
            ?? new Intent(context, typeof(MainActivity));
        launch.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        PendingIntentFlags flags = OperatingSystem.IsAndroidVersionAtLeast(23)
            ? PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable
            : PendingIntentFlags.UpdateCurrent;
        return PendingIntent.GetActivity(context, 1507, launch,
            flags)!;
    }
}
