using UniversitySchedule.Mobile.Core.Assignments;
using UniversitySchedule.Mobile.Core.Notes;

namespace UniversitySchedule.Mobile.Core.Sync;

public interface IPersonalDataChangeSink
{
    Task NoteUpsertedAsync(PersonalNote note, CancellationToken cancellationToken = default);

    Task NoteDeletedAsync(Guid noteId, DateTimeOffset deletedAtUtc, CancellationToken cancellationToken = default);

    Task AssignmentUpsertedAsync(PersonalAssignment assignment, CancellationToken cancellationToken = default);

    Task AssignmentDeletedAsync(Guid assignmentId, DateTimeOffset deletedAtUtc, CancellationToken cancellationToken = default);
}

public sealed class NullPersonalDataChangeSink : IPersonalDataChangeSink
{
    public static NullPersonalDataChangeSink Instance { get; } = new();

    private NullPersonalDataChangeSink()
    {
    }

    public Task NoteUpsertedAsync(PersonalNote note, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task NoteDeletedAsync(Guid noteId, DateTimeOffset deletedAtUtc, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task AssignmentUpsertedAsync(PersonalAssignment assignment, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task AssignmentDeletedAsync(Guid assignmentId, DateTimeOffset deletedAtUtc, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
