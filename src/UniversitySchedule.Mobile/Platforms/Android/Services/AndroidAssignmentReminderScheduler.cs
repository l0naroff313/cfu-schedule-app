using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using UniversitySchedule.Mobile.Core.Assignments;
using UniversitySchedule.Mobile.Core.Notifications;

namespace UniversitySchedule.Mobile;

internal sealed class AndroidAssignmentReminderScheduler : IAssignmentReminderScheduler
{
    internal const string ReminderIdsKey = "assignment-reminder-ids-v1";
    internal const string ReminderChannelId = "assignment-deadlines";
    internal const int ReminderPermissionRequestCode = 4102;
    private const string AssignmentIdExtra = "assignment_id";
    private const string SubjectExtra = "subject";
    private const string TextExtra = "text";
    private const string DeadlineExtra = "deadline";
    private readonly PersonalAssignmentStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public AndroidAssignmentReminderScheduler(PersonalAssignmentStore store, TimeProvider timeProvider)
    {
        _store = store;
        _timeProvider = timeProvider;
        _store.Changed += OnAssignmentsChanged;
    }

    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            Context context = Android.App.Application.Context;
            AlarmManager? alarm = context.GetSystemService(Context.AlarmService) as AlarmManager;
            if (alarm is null) return;

            CancelExisting(context, alarm);
            List<PersonalAssignment> assignments = (await _store.GetAllAsync(cancellationToken)).ToList();
            IReadOnlyList<PersonalAssignment> pending = assignments
                .Where(ShouldSchedule)
                .ToArray();
            if (pending.Count == 0)
            {
                Preferences.Default.Remove(ReminderIdsKey);
                return;
            }

            await RequestNotificationPermissionAsync();
            EnsureChannel(context);
            List<int> scheduledIds = [];
            foreach (PersonalAssignment assignment in pending)
            {
                int requestCode = GetRequestCode(assignment.Id);
                Schedule(context, alarm, assignment, requestCode);
                scheduledIds.Add(requestCode);
            }

            Preferences.Default.Set(ReminderIdsKey, string.Join(',', scheduledIds));
        }
        finally
        {
            _lock.Release();
        }
    }

    private void OnAssignmentsChanged(object? sender, EventArgs e) =>
        _ = SynchronizeAsync();

    private bool ShouldSchedule(PersonalAssignment assignment)
    {
        if (assignment.Status == PersonalAssignmentStatus.Completed ||
            assignment.DeadlineUtc is not DateTimeOffset deadline ||
            assignment.ReminderMinutesBefore is not > 0)
        {
            return false;
        }

        DateTimeOffset trigger = deadline.AddMinutes(-assignment.ReminderMinutesBefore.Value);
        return trigger > _timeProvider.GetUtcNow();
    }

    private static void CancelExisting(Context context, AlarmManager alarm)
    {
        string ids = Preferences.Default.Get(ReminderIdsKey, string.Empty);
        PendingIntentFlags flags = PendingIntentFlags.NoCreate |
            (OperatingSystem.IsAndroidVersionAtLeast(23) ? PendingIntentFlags.Immutable : 0);
        foreach (string value in ids.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(value, out int requestCode)) continue;
            Intent intent = new Intent(context, typeof(CfuAssignmentReminderReceiver));
            PendingIntent? pending = PendingIntent.GetBroadcast(context, requestCode, intent, flags);
            if (pending is not null)
            {
                alarm.Cancel(pending);
                pending.Cancel();
            }
        }
    }

    private static void Schedule(Context context, AlarmManager alarm, PersonalAssignment assignment, int requestCode)
    {
        DateTimeOffset trigger = assignment.DeadlineUtc!.Value.AddMinutes(-assignment.ReminderMinutesBefore!.Value);
        Intent intent = new Intent(context, typeof(CfuAssignmentReminderReceiver))
            .PutExtra(AssignmentIdExtra, assignment.Id.ToString("D"))
            .PutExtra(SubjectExtra, assignment.Subject)
            .PutExtra(TextExtra, assignment.Text)
            .PutExtra(DeadlineExtra, assignment.DeadlineUtc.Value.ToUnixTimeMilliseconds());
        PendingIntentFlags flags = PendingIntentFlags.UpdateCurrent |
            (OperatingSystem.IsAndroidVersionAtLeast(23) ? PendingIntentFlags.Immutable : 0);
        PendingIntent pending = PendingIntent.GetBroadcast(context, requestCode, intent, flags)!;
        long triggerMillis = trigger.ToUnixTimeMilliseconds();
        if (OperatingSystem.IsAndroidVersionAtLeast(31) && alarm.CanScheduleExactAlarms())
        {
            alarm.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerMillis, pending);
        }
        else
        {
            alarm.SetWindow(AlarmType.RtcWakeup, triggerMillis, 15 * 60 * 1000, pending);
        }
    }

    private static int GetRequestCode(Guid id)
    {
        int value = id.GetHashCode() & int.MaxValue;
        return value == 0 ? 1 : value;
    }

    private static void EnsureChannel(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        NotificationManager? manager = context.GetSystemService(Context.NotificationService) as NotificationManager;
        manager?.CreateNotificationChannel(new NotificationChannel(
            ReminderChannelId,
            "Дедлайны заданий",
            NotificationImportance.Default)
        {
            Description = "Напоминания о личных домашних заданиях",
        });
    }

    private static async Task RequestNotificationPermissionAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33) ||
            Platform.CurrentActivity is not Android.App.Activity activity ||
            ActivityCompat.CheckSelfPermission(activity, "android.permission.POST_NOTIFICATIONS") == Permission.Granted)
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
            ActivityCompat.RequestPermissions(
                activity,
                ["android.permission.POST_NOTIFICATIONS"],
                ReminderPermissionRequestCode));
    }
}
