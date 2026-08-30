using UniversitySchedule.Domain.PersonalData;

namespace UniversitySchedule.Application.PersonalData;

public sealed class PersonalDataSyncService(
    IPersonalDataRepository repository,
    TimeProvider timeProvider)
{
    public Task<IReadOnlyList<SyncedNote>> GetNotesAsync(
        Guid installationId,
        bool includeDeleted,
        CancellationToken cancellationToken = default) =>
        repository.GetNotesAsync(installationId, includeDeleted, cancellationToken);

    public Task<IReadOnlyList<SyncedAssignment>> GetAssignmentsAsync(
        Guid installationId,
        bool includeDeleted,
        CancellationToken cancellationToken = default) =>
        repository.GetAssignmentsAsync(installationId, includeDeleted, cancellationToken);

    public async Task<PersonalDataSyncResult<SyncedNote>> UpsertNoteAsync(
        NoteSyncCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateNote(command);
        string text = command.Text.Trim();
        string? title = NormalizeOptional(command.Title);
        string? subject = NormalizeOptional(command.Subject);
        DateTimeOffset serverNow = timeProvider.GetUtcNow().ToUniversalTime();
        SyncedNote? note = await repository.FindNoteAsync(
            command.InstallationId,
            command.NoteId,
            cancellationToken);
        bool wasApplied;

        if (note is null)
        {
            note = SyncedNote.Create(
                command.InstallationId,
                command.NoteId,
                command.MutationId,
                command.LessonId,
                text,
                title,
                subject,
                command.IsPinned,
                command.CreatedAtUtc,
                command.UpdatedAtUtc,
                serverNow);
            repository.AddNote(note);
            wasApplied = true;
        }
        else
        {
            wasApplied = note.Apply(
                command.MutationId,
                command.LessonId,
                text,
                title,
                subject,
                command.IsPinned,
                command.CreatedAtUtc,
                command.UpdatedAtUtc,
                serverNow);
        }

        if (wasApplied)
        {
            await repository.SaveChangesAsync(cancellationToken);
        }

        return new PersonalDataSyncResult<SyncedNote>(note, wasApplied);
    }

    public async Task<PersonalDataSyncResult<SyncedNote>> DeleteNoteAsync(
        DeletePersonalDataCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateDelete(command);
        DateTimeOffset serverNow = timeProvider.GetUtcNow().ToUniversalTime();
        SyncedNote? note = await repository.FindNoteAsync(
            command.InstallationId,
            command.EntityId,
            cancellationToken);
        bool wasApplied;

        if (note is null)
        {
            note = SyncedNote.CreateTombstone(
                command.InstallationId,
                command.EntityId,
                command.MutationId,
                command.DeletedAtUtc,
                serverNow);
            repository.AddNote(note);
            wasApplied = true;
        }
        else
        {
            wasApplied = note.Delete(command.MutationId, command.DeletedAtUtc, serverNow);
        }

        if (wasApplied)
        {
            await repository.SaveChangesAsync(cancellationToken);
        }

        return new PersonalDataSyncResult<SyncedNote>(note, wasApplied);
    }

    public async Task<PersonalDataSyncResult<SyncedAssignment>> UpsertAssignmentAsync(
        AssignmentSyncCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateAssignment(command);
        string subject = string.IsNullOrWhiteSpace(command.Subject) ? "Без предмета" : command.Subject.Trim();
        string text = command.Text.Trim();
        DateTimeOffset serverNow = timeProvider.GetUtcNow().ToUniversalTime();
        SyncedAssignment? assignment = await repository.FindAssignmentAsync(
            command.InstallationId,
            command.AssignmentId,
            cancellationToken);
        bool wasApplied;

        if (assignment is null)
        {
            assignment = SyncedAssignment.Create(
                command.InstallationId,
                command.AssignmentId,
                command.MutationId,
                command.LessonId,
                subject,
                text,
                command.DeadlineUtc,
                command.Status,
                command.CreatedAtUtc,
                command.UpdatedAtUtc,
                serverNow);
            repository.AddAssignment(assignment);
            wasApplied = true;
        }
        else
        {
            wasApplied = assignment.Apply(
                command.MutationId,
                command.LessonId,
                subject,
                text,
                command.DeadlineUtc,
                command.Status,
                command.CreatedAtUtc,
                command.UpdatedAtUtc,
                serverNow);
        }

        if (wasApplied)
        {
            await repository.SaveChangesAsync(cancellationToken);
        }

        return new PersonalDataSyncResult<SyncedAssignment>(assignment, wasApplied);
    }

    public async Task<PersonalDataSyncResult<SyncedAssignment>> DeleteAssignmentAsync(
        DeletePersonalDataCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateDelete(command);
        DateTimeOffset serverNow = timeProvider.GetUtcNow().ToUniversalTime();
        SyncedAssignment? assignment = await repository.FindAssignmentAsync(
            command.InstallationId,
            command.EntityId,
            cancellationToken);
        bool wasApplied;

        if (assignment is null)
        {
            assignment = SyncedAssignment.CreateTombstone(
                command.InstallationId,
                command.EntityId,
                command.MutationId,
                command.DeletedAtUtc,
                serverNow);
            repository.AddAssignment(assignment);
            wasApplied = true;
        }
        else
        {
            wasApplied = assignment.Delete(command.MutationId, command.DeletedAtUtc, serverNow);
        }

        if (wasApplied)
        {
            await repository.SaveChangesAsync(cancellationToken);
        }

        return new PersonalDataSyncResult<SyncedAssignment>(assignment, wasApplied);
    }

    private static void ValidateNote(NoteSyncCommand command)
    {
        ValidateCommon(command.InstallationId, command.NoteId, command.MutationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Text);
        if (command.Text.Length > 8_000 || command.Title?.Length > 200 || command.Subject?.Length > 200)
        {
            throw new ArgumentException("Note fields exceed the allowed length.");
        }
    }

    private static void ValidateAssignment(AssignmentSyncCommand command)
    {
        ValidateCommon(command.InstallationId, command.AssignmentId, command.MutationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Text);
        if (command.Text.Length > 8_000 || command.Subject?.Length > 200 || !Enum.IsDefined(command.Status))
        {
            throw new ArgumentException("Assignment fields are invalid.");
        }
    }

    private static void ValidateDelete(DeletePersonalDataCommand command) =>
        ValidateCommon(command.InstallationId, command.EntityId, command.MutationId);

    private static void ValidateCommon(Guid installationId, Guid entityId, Guid mutationId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(installationId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(entityId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(mutationId, Guid.Empty);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
