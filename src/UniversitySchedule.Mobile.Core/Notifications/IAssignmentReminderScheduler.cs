using UniversitySchedule.Mobile.Core.Assignments;

namespace UniversitySchedule.Mobile.Core.Notifications;

public interface IAssignmentReminderScheduler
{
    Task SynchronizeAsync(CancellationToken cancellationToken = default);
}

public sealed class NoopAssignmentReminderScheduler : IAssignmentReminderScheduler
{
    public Task SynchronizeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
