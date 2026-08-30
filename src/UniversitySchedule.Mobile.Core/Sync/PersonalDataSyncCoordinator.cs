using UniversitySchedule.Mobile.Core.Assignments;
using UniversitySchedule.Mobile.Core.Notes;

namespace UniversitySchedule.Mobile.Core.Sync;

public sealed class PersonalDataSyncCoordinator(
    PersonalDataSyncQueue queue,
    PersonalDataSynchronizer synchronizer) : IPersonalDataChangeSink
{
    private int _backgroundSyncRunning;

    public async Task NoteUpsertedAsync(
        PersonalNote note,
        CancellationToken cancellationToken = default)
    {
        await queue.EnqueueNoteUpsertAsync(note, cancellationToken);
        StartBackgroundSynchronization();
    }

    public async Task NoteDeletedAsync(
        Guid noteId,
        DateTimeOffset deletedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await queue.EnqueueNoteDeleteAsync(noteId, deletedAtUtc, cancellationToken);
        StartBackgroundSynchronization();
    }

    public async Task AssignmentUpsertedAsync(
        PersonalAssignment assignment,
        CancellationToken cancellationToken = default)
    {
        await queue.EnqueueAssignmentUpsertAsync(assignment, cancellationToken);
        StartBackgroundSynchronization();
    }

    public async Task AssignmentDeletedAsync(
        Guid assignmentId,
        DateTimeOffset deletedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await queue.EnqueueAssignmentDeleteAsync(assignmentId, deletedAtUtc, cancellationToken);
        StartBackgroundSynchronization();
    }

    public void StartBackgroundSynchronization()
    {
        if (Interlocked.CompareExchange(ref _backgroundSyncRunning, 1, 0) != 0)
        {
            return;
        }

        _ = SynchronizeInBackgroundAsync();
    }

    private async Task SynchronizeInBackgroundAsync()
    {
        try
        {
            await synchronizer.SynchronizeAsync();
        }
        catch
        {
            // Offline and transient failures are expected; the durable queue is retried later.
        }
        finally
        {
            Interlocked.Exchange(ref _backgroundSyncRunning, 0);
        }
    }
}
