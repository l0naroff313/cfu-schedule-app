using UniversitySchedule.Domain.PersonalData;

namespace UniversitySchedule.Application.PersonalData;

public interface IPersonalDataRepository
{
    Task<SyncedNote?> FindNoteAsync(Guid installationId, Guid noteId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SyncedNote>> GetNotesAsync(Guid installationId, bool includeDeleted, CancellationToken cancellationToken = default);

    void AddNote(SyncedNote note);

    Task<SyncedAssignment?> FindAssignmentAsync(Guid installationId, Guid assignmentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SyncedAssignment>> GetAssignmentsAsync(Guid installationId, bool includeDeleted, CancellationToken cancellationToken = default);

    void AddAssignment(SyncedAssignment assignment);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
