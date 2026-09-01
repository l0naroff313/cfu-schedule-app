using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniversitySchedule.Api.Authentication;
using UniversitySchedule.Application.PersonalData;
using UniversitySchedule.Contracts.PersonalData;
using UniversitySchedule.Domain.PersonalData;

namespace UniversitySchedule.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/sync/notes")]
public sealed class NotesSyncController(PersonalDataSyncService syncService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SyncedNoteResponse>>> GetAll(
        [FromQuery] bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        if (!InstallationPrincipal.TryGetInstallationId(User, out Guid installationId))
        {
            return Unauthorized();
        }

        IReadOnlyList<SyncedNote> notes = await syncService.GetNotesAsync(
            installationId,
            includeDeleted,
            cancellationToken);
        return Ok(notes.Select(note => ToResponse(
            note,
            PersonalDataSyncDisposition.AlreadyApplied)));
    }

    [HttpPut("{noteId:guid}")]
    public async Task<ActionResult<SyncedNoteResponse>> Upsert(
        Guid noteId,
        SyncNoteRequest request,
        CancellationToken cancellationToken)
    {
        if (!InstallationPrincipal.TryGetInstallationId(User, out Guid installationId))
        {
            return Unauthorized();
        }

        if (noteId == Guid.Empty || request.MutationId == Guid.Empty)
        {
            return BadRequest();
        }

        try
        {
            PersonalDataSyncResult<SyncedNote> result = await syncService.UpsertNoteAsync(
                new NoteSyncCommand(
                    installationId,
                    noteId,
                    request.MutationId,
                    request.LessonId,
                    request.Text,
                    request.Title,
                    request.Subject,
                    request.IsPinned,
                    request.CreatedAtUtc,
                    request.UpdatedAtUtc),
                cancellationToken);
            return ToMutationResponse(result);
        }
        catch (MutationIdReuseException)
        {
            return Conflict(CreateMutationReuseProblem());
        }
    }

    [HttpDelete("{noteId:guid}")]
    public async Task<ActionResult<SyncedNoteResponse>> Delete(
        Guid noteId,
        [FromQuery] Guid mutationId,
        [FromQuery] DateTimeOffset deletedAtUtc,
        CancellationToken cancellationToken)
    {
        if (!InstallationPrincipal.TryGetInstallationId(User, out Guid installationId))
        {
            return Unauthorized();
        }

        if (noteId == Guid.Empty || mutationId == Guid.Empty || deletedAtUtc == default)
        {
            return BadRequest();
        }

        try
        {
            PersonalDataSyncResult<SyncedNote> result = await syncService.DeleteNoteAsync(
                new DeletePersonalDataCommand(
                    installationId,
                    noteId,
                    mutationId,
                    deletedAtUtc),
                cancellationToken);
            return ToMutationResponse(result);
        }
        catch (MutationIdReuseException)
        {
            return Conflict(CreateMutationReuseProblem());
        }
    }

    private ActionResult<SyncedNoteResponse> ToMutationResponse(
        PersonalDataSyncResult<SyncedNote> result)
    {
        SyncedNoteResponse response = ToResponse(result.Entity, result.Disposition);
        return result.Disposition == PersonalDataSyncDisposition.Conflict
            ? Conflict(response)
            : Ok(response);
    }

    private static ProblemDetails CreateMutationReuseProblem() =>
        new()
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Идентификатор изменения уже использован",
            Detail = "Повторите операцию с новым MutationId.",
        };

    private static SyncedNoteResponse ToResponse(
        SyncedNote note,
        PersonalDataSyncDisposition disposition) =>
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
            disposition == PersonalDataSyncDisposition.Applied,
            (SyncMutationDisposition)disposition);
}
