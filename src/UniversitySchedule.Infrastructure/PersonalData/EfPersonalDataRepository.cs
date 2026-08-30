using Microsoft.EntityFrameworkCore;
using UniversitySchedule.Application.PersonalData;
using UniversitySchedule.Domain.PersonalData;
using UniversitySchedule.Infrastructure.Persistence;

namespace UniversitySchedule.Infrastructure.PersonalData;

public sealed class EfPersonalDataRepository(AppDbContext dbContext) : IPersonalDataRepository
{
    public async Task<SyncedNote?> FindNoteAsync(
        Guid installationId,
        Guid noteId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Notes.FindAsync([installationId, noteId], cancellationToken);

    public async Task<IReadOnlyList<SyncedNote>> GetNotesAsync(
        Guid installationId,
        bool includeDeleted,
        CancellationToken cancellationToken = default)
    {
        IQueryable<SyncedNote> query = dbContext.Notes
            .AsNoTracking()
            .Where(note => note.InstallationId == installationId);
        if (!includeDeleted)
        {
            query = query.Where(note => note.DeletedAtUtc == null);
        }

        return await query
            .OrderBy(note => note.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public void AddNote(SyncedNote note) => dbContext.Notes.Add(note);

    public async Task<SyncedAssignment?> FindAssignmentAsync(
        Guid installationId,
        Guid assignmentId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Assignments.FindAsync([installationId, assignmentId], cancellationToken);

    public async Task<IReadOnlyList<SyncedAssignment>> GetAssignmentsAsync(
        Guid installationId,
        bool includeDeleted,
        CancellationToken cancellationToken = default)
    {
        IQueryable<SyncedAssignment> query = dbContext.Assignments
            .AsNoTracking()
            .Where(assignment => assignment.InstallationId == installationId);
        if (!includeDeleted)
        {
            query = query.Where(assignment => assignment.DeletedAtUtc == null);
        }

        return await query
            .OrderBy(assignment => assignment.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public void AddAssignment(SyncedAssignment assignment) => dbContext.Assignments.Add(assignment);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
