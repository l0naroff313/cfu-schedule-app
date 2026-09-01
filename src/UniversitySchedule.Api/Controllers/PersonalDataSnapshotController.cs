using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniversitySchedule.Api.Authentication;
using UniversitySchedule.Application.PersonalData;
using UniversitySchedule.Contracts.PersonalData;
using UniversitySchedule.Domain.PersonalData;

namespace UniversitySchedule.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/sync/snapshot")]
public sealed class PersonalDataSnapshotController(
    PersonalDataSyncService syncService,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PersonalDataSnapshotResponse>> Get(
        CancellationToken cancellationToken = default)
    {
        if (!InstallationPrincipal.TryGetInstallationId(User, out Guid installationId))
        {
            return Unauthorized();
        }

        IReadOnlyList<SyncedNote> notes = await syncService.GetNotesAsync(
            installationId,
            includeDeleted: true,
            cancellationToken);
        IReadOnlyList<SyncedAssignment> assignments = await syncService.GetAssignmentsAsync(
            installationId,
            includeDeleted: true,
            cancellationToken);
        return Ok(new PersonalDataSnapshotResponse(
            timeProvider.GetUtcNow().ToUniversalTime(),
            notes.Select(ToResponse).ToArray(),
            assignments.Select(ToResponse).ToArray()));
    }

    private static SyncedNoteResponse ToResponse(SyncedNote note) =>
        new(
            note.Id,
            note.LessonId,
            note.Text,
            note.Title,
            note.Subject,
            note.IsPinned,
            note.CreatedAtUtc,
            note.ClientUpdatedAtUtc,
            note.ServerUpdatedAtUtc,
            note.DeletedAtUtc,
            note.Revision,
            false,
            SyncMutationDisposition.AlreadyApplied);

    private static SyncedAssignmentResponse ToResponse(SyncedAssignment assignment) =>
        new(
            assignment.Id,
            assignment.LessonId,
            assignment.Subject,
            assignment.Text,
            assignment.DeadlineUtc,
            (AssignmentSyncStatus)assignment.Status,
            assignment.CreatedAtUtc,
            assignment.ClientUpdatedAtUtc,
            assignment.ServerUpdatedAtUtc,
            assignment.DeletedAtUtc,
            assignment.Revision,
            false,
            SyncMutationDisposition.AlreadyApplied);
}
