using Android.App;
using Android.Content;
using Android.Content.PM;
using AndroidX.Core.App;

namespace UniversitySchedule.Mobile;

[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class CfuAssignmentReminderReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null ||
            (OperatingSystem.IsAndroidVersionAtLeast(33) &&
             ActivityCompat.CheckSelfPermission(context, "android.permission.POST_NOTIFICATIONS") != Permission.Granted))
        {
            return;
        }

        Intent notificationIntent = intent;
        EnsureChannel(context);
        string subject = notificationIntent.GetStringExtra("subject") ?? "Домашнее задание";
        string text = notificationIntent.GetStringExtra("text") ?? "Проверьте дедлайн задания.";
        long deadlineMillis = notificationIntent.GetLongExtra("deadline", 0);
        string deadlineText = deadlineMillis > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(deadlineMillis).ToLocalTime().ToString("dd.MM, HH:mm")
            : string.Empty;
        string body = string.IsNullOrWhiteSpace(deadlineText)
            ? text
            : $"{text} • дедлайн {deadlineText}";
        Intent launch = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!)
            ?? new Intent(context, typeof(MainActivity));
        launch.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        PendingIntent pending = PendingIntent.GetActivity(
            context,
            4103,
            launch,
            PendingIntentFlags.UpdateCurrent |
                (OperatingSystem.IsAndroidVersionAtLeast(23) ? PendingIntentFlags.Immutable : 0))!;
        Notification.Builder builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(context, AndroidAssignmentReminderScheduler.ReminderChannelId)
            : new Notification.Builder(context);
        Notification notification = builder
            .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
            .SetContentTitle($"Дедлайн: {subject}")
            .SetContentText(body)
            .SetStyle(new Notification.BigTextStyle().BigText(body))
            .SetAutoCancel(true)
            .SetContentIntent(pending)
            .Build()!;
        NotificationManagerCompat? notificationManager = NotificationManagerCompat.From(context);
        notificationManager?.Notify(notificationIntent.GetIntExtra("notification_id", 4103), notification);
    }

    private static void EnsureChannel(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        NotificationManager? manager = context.GetSystemService(Context.NotificationService) as NotificationManager;
        manager?.CreateNotificationChannel(new NotificationChannel(
            AndroidAssignmentReminderScheduler.ReminderChannelId,
            "Дедлайны заданий",
            NotificationImportance.Default));
    }
}
