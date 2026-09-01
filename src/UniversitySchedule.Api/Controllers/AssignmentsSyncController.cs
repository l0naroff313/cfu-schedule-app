using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniversitySchedule.Api.Authentication;
using UniversitySchedule.Application.PersonalData;
using UniversitySchedule.Contracts.PersonalData;
using UniversitySchedule.Domain.PersonalData;

namespace UniversitySchedule.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/sync/assignments")]
public sealed class AssignmentsSyncController(PersonalDataSyncService syncService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SyncedAssignmentResponse>>> GetAll(
        [FromQuery] bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        if (!InstallationPrincipal.TryGetInstallationId(User, out Guid installationId))
        {
            return Unauthorized();
        }

        IReadOnlyList<SyncedAssignment> assignments = await syncService.GetAssignmentsAsync(
            installationId,
            includeDeleted,
            cancellationToken);
        return Ok(assignments.Select(assignment => ToResponse(
            assignment,
            PersonalDataSyncDisposition.AlreadyApplied)));
    }

    [HttpPut("{assignmentId:guid}")]
    public async Task<ActionResult<SyncedAssignmentResponse>> Upsert(
        Guid assignmentId,
        SyncAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        if (!InstallationPrincipal.TryGetInstallationId(User, out Guid installationId))
        {
            return Unauthorized();
        }

        if (assignmentId == Guid.Empty || request.MutationId == Guid.Empty || !Enum.IsDefined(request.Status))
        {
            return BadRequest();
        }

        try
        {
            PersonalDataSyncResult<SyncedAssignment> result = await syncService.UpsertAssignmentAsync(
                new AssignmentSyncCommand(
                    installationId,
                    assignmentId,
                    request.MutationId,
                    request.LessonId,
                    request.Subject,
                    request.Text,
                    request.DeadlineUtc,
                    (SyncedAssignmentStatus)request.Status,
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

    [HttpDelete("{assignmentId:guid}")]
    public async Task<ActionResult<SyncedAssignmentResponse>> Delete(
        Guid assignmentId,
        [FromQuery] Guid mutationId,
        [FromQuery] DateTimeOffset deletedAtUtc,
        CancellationToken cancellationToken)
    {
        if (!InstallationPrincipal.TryGetInstallationId(User, out Guid installationId))
        {
            return Unauthorized();
        }

        if (assignmentId == Guid.Empty || mutationId == Guid.Empty || deletedAtUtc == default)
        {
            return BadRequest();
        }

        try
        {
            PersonalDataSyncResult<SyncedAssignment> result = await syncService.DeleteAssignmentAsync(
                new DeletePersonalDataCommand(
                    installationId,
                    assignmentId,
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

    private ActionResult<SyncedAssignmentResponse> ToMutationResponse(
        PersonalDataSyncResult<SyncedAssignment> result)
    {
        SyncedAssignmentResponse response = ToResponse(result.Entity, result.Disposition);
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

    private static SyncedAssignmentResponse ToResponse(
        SyncedAssignment assignment,
        PersonalDataSyncDisposition disposition) =>
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
            disposition == PersonalDataSyncDisposition.Applied,
            (SyncMutationDisposition)disposition);
}
