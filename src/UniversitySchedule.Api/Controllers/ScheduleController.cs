using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniversitySchedule.Api.Catalog;
using UniversitySchedule.Contracts.Catalog;
using UniversitySchedule.Contracts.Schedule;
using UniversitySchedule.Infrastructure.Cfu;

namespace UniversitySchedule.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/schedule")]
public sealed class ScheduleController(
    CfuScheduleBackendClient cfuClient,
    ReferenceCatalogQueryService catalogQuery,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("groups/{groupId:guid}")]
    public async Task<ActionResult<ScheduleSnapshot>> GetGroupSchedule(
        Guid groupId,
        [FromQuery] int? subgroup,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        if (subgroup is <= 0)
        {
            return BadRequest(CreateProblem(
                StatusCodes.Status400BadRequest,
                "Подгруппа должна быть положительным числом."));
        }

        if (!TryValidatePeriod(from, to, out ProblemDetails? periodProblem))
        {
            return BadRequest(periodProblem);
        }

        try
        {
            CfuDocumentLoadResult<CfuScheduleIndexDocument> index = await cfuClient.LoadIndexAsync(cancellationToken);
            CfuScheduleBackendCatalog catalog = CfuScheduleBackendMapper.MapCatalog(index.Value);
            StudyGroupSummary? group = catalog.Groups.SingleOrDefault(item => item.Id == groupId);
            if (group is null)
            {
                return NotFound();
            }

            CfuDocumentLoadResult<CfuGroupScheduleDocument> schedule = await cfuClient.LoadGroupAsync(
                group.Name,
                cancellationToken);
            AddSourceHeaders(Min(index.UpdatedAtUtc, schedule.UpdatedAtUtc), schedule.IsFromCache);
            return Ok(Filter(CfuScheduleBackendMapper.MapGroup(index.Value, schedule.Value, subgroup), from, to));
        }
        catch (CfuScheduleUnavailableException)
        {
            return SourceUnavailable();
        }
    }

    [HttpGet("teachers/{teacherId:guid}")]
    public async Task<ActionResult<ScheduleSnapshot>> GetTeacherSchedule(
        Guid teacherId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        if (!TryValidatePeriod(from, to, out ProblemDetails? periodProblem))
        {
            return BadRequest(periodProblem);
        }

        ReferenceCatalogLoadResult? catalog = await catalogQuery.GetAsync(cancellationToken);
        if (catalog is null)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                CreateProblem(
                    StatusCodes.Status503ServiceUnavailable,
                    "Справочник преподавателей ещё не опубликован в PostgreSQL."));
        }

        TeacherReference? teacher = catalog.Snapshot.Teachers.SingleOrDefault(item => item.Id == teacherId);
        if (teacher is null)
        {
            return NotFound();
        }

        try
        {
            CfuDocumentLoadResult<CfuScheduleIndexDocument> index = await cfuClient.LoadIndexAsync(cancellationToken);
            CfuDocumentLoadResult<IReadOnlyList<CfuLessonDocument>> source = await cfuClient.FindTeacherAsync(
                teacher.Surname,
                cancellationToken);
            CfuLessonDocument[] exactLessons = source.Value
                .Where(lesson => lesson.Teachers.Any(name => IsExactTeacher(name, teacher.IdentityKey)))
                .ToArray();
            AddSourceHeaders(Min(index.UpdatedAtUtc, source.UpdatedAtUtc), source.IsFromCache);
            ScheduleSnapshot snapshot = CfuScheduleBackendMapper.MapTeacher(
                index.Value,
                exactLessons,
                teacher.Id,
                teacher.FullName);
            return Ok(Filter(snapshot, from, to));
        }
        catch (CfuScheduleUnavailableException)
        {
            return SourceUnavailable();
        }
    }

    [HttpGet("groups/{groupId:guid}/current")]
    public async Task<ActionResult<CurrentScheduleResponse>> GetGroupCurrent(
        Guid groupId,
        [FromQuery] int? subgroup,
        [FromQuery] DateTimeOffset? atUtc,
        CancellationToken cancellationToken)
    {
        ActionResult<ScheduleSnapshot> result = await GetGroupSchedule(
            groupId,
            subgroup,
            from: null,
            to: null,
            cancellationToken);
        return result.Result is OkObjectResult { Value: ScheduleSnapshot snapshot }
            ? Ok(ResolveCurrent(snapshot, atUtc))
            : ConvertFailure(result.Result ?? new StatusCodeResult(StatusCodes.Status500InternalServerError));
    }

    [HttpGet("teachers/{teacherId:guid}/current")]
    public async Task<ActionResult<CurrentScheduleResponse>> GetTeacherCurrent(
        Guid teacherId,
        [FromQuery] DateTimeOffset? atUtc,
        CancellationToken cancellationToken)
    {
        ActionResult<ScheduleSnapshot> result = await GetTeacherSchedule(
            teacherId,
            from: null,
            to: null,
            cancellationToken);
        return result.Result is OkObjectResult { Value: ScheduleSnapshot snapshot }
            ? Ok(ResolveCurrent(snapshot, atUtc))
            : ConvertFailure(result.Result ?? new StatusCodeResult(StatusCodes.Status500InternalServerError));
    }

    private CurrentScheduleResponse ResolveCurrent(
        ScheduleSnapshot snapshot,
        DateTimeOffset? requestedAtUtc)
    {
        DateTimeOffset atUtc = (requestedAtUtc ?? timeProvider.GetUtcNow()).ToUniversalTime();
        ScheduleLesson? current = snapshot.Lessons
            .Where(lesson => !string.Equals(lesson.Status, "отменено", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(lesson => lesson.StartsAtUtc <= atUtc && atUtc < lesson.EndsAtUtc);
        ScheduleLesson? next = snapshot.Lessons
            .Where(lesson => !string.Equals(lesson.Status, "отменено", StringComparison.OrdinalIgnoreCase))
            .Where(lesson => lesson.StartsAtUtc > atUtc)
            .OrderBy(lesson => lesson.StartsAtUtc)
            .FirstOrDefault();
        return new CurrentScheduleResponse(atUtc, current, next);
    }

    private static ScheduleSnapshot Filter(
        ScheduleSnapshot snapshot,
        DateOnly? from,
        DateOnly? to)
    {
        if (!from.HasValue && !to.HasValue)
        {
            return snapshot;
        }

        DateOnly effectiveFrom = from ?? snapshot.From;
        DateOnly effectiveTo = to ?? snapshot.To;
        ScheduleLesson[] lessons = snapshot.Lessons
            .Where(lesson => lesson.Date >= effectiveFrom && lesson.Date <= effectiveTo)
            .ToArray();
        return snapshot with
        {
            From = effectiveFrom,
            To = effectiveTo,
            Lessons = lessons,
        };
    }

    private static bool TryValidatePeriod(
        DateOnly? from,
        DateOnly? to,
        out ProblemDetails? problem)
    {
        problem = null;
        if (from.HasValue && to.HasValue && to.Value < from.Value)
        {
            problem = CreateProblem(
                StatusCodes.Status400BadRequest,
                "Дата окончания не может быть раньше даты начала.");
            return false;
        }

        if (from.HasValue && to.HasValue && to.Value.DayNumber - from.Value.DayNumber > 180)
        {
            problem = CreateProblem(
                StatusCodes.Status400BadRequest,
                "За один запрос можно получить не более 180 дней расписания.");
            return false;
        }

        return true;
    }

    private static bool IsExactTeacher(string displayName, string identityKey) =>
        TeacherIdentityParser.TryParse(displayName, out TeacherIdentity identity) &&
        string.Equals(identity.Key, identityKey, StringComparison.OrdinalIgnoreCase);

    private void AddSourceHeaders(DateTimeOffset updatedAtUtc, bool isFromCache)
    {
        Response.Headers["X-Schedule-Source"] = isFromCache ? "postgresql-cache" : "cfu-live";
        Response.GetTypedHeaders().LastModified = updatedAtUtc;
    }

    private ObjectResult SourceUnavailable() =>
        StatusCode(
            StatusCodes.Status503ServiceUnavailable,
            CreateProblem(
                StatusCodes.Status503ServiceUnavailable,
                "Расписание КФУ временно недоступно, а серверный кэш ещё пуст."));

    private static ActionResult<CurrentScheduleResponse> ConvertFailure(IActionResult result) =>
        result switch
        {
            ObjectResult objectResult => new ObjectResult(objectResult.Value)
            {
                StatusCode = objectResult.StatusCode,
            },
            StatusCodeResult statusCode => new StatusCodeResult(statusCode.StatusCode),
            _ => new StatusCodeResult(StatusCodes.Status500InternalServerError),
        };

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second) =>
        first <= second ? first : second;

    private static ProblemDetails CreateProblem(int status, string detail) => new()
    {
        Status = status,
        Title = "Не удалось получить расписание",
        Detail = detail,
    };
}
