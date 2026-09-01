using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniversitySchedule.Api.Catalog;
using UniversitySchedule.Contracts.Catalog;
using UniversitySchedule.Infrastructure.Cfu;

namespace UniversitySchedule.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/catalog")]
public sealed class CatalogController(
    CfuScheduleBackendClient cfuClient,
    ReferenceCatalogQueryService catalogQuery) : ControllerBase
{
    [HttpGet("snapshot")]
    [ProducesResponseType<ReferenceCatalogSnapshot>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ReferenceCatalogSnapshot>> GetSnapshot(
        CancellationToken cancellationToken)
    {
        ReferenceCatalogLoadResult? result = await catalogQuery.GetAsync(cancellationToken);
        if (result is null)
        {
            return CatalogUnavailable();
        }

        string etag = $"\"{result.ContentHash}\"";
        Response.Headers.ETag = etag;
        Response.GetTypedHeaders().LastModified = result.ImportedAtUtc;
        if (Request.Headers.IfNoneMatch.Any(value => string.Equals(value, etag, StringComparison.Ordinal)))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        return Ok(result.Snapshot);
    }

    [HttpGet("status")]
    public async Task<ActionResult<ReferenceCatalogStatusResponse>> GetStatus(
        CancellationToken cancellationToken)
    {
        ReferenceCatalogLoadResult? result = await catalogQuery.GetAsync(cancellationToken);
        return result is null
            ? CatalogUnavailable()
            : Ok(new ReferenceCatalogStatusResponse(
                result.Snapshot.SchemaVersion,
                result.Snapshot.GeneratedAtUtc,
                result.ImportedAtUtc,
                result.ContentHash,
                result.Snapshot.Statistics));
    }

    [HttpGet("institutes")]
    public async Task<ActionResult<IReadOnlyList<InstituteSummary>>> GetInstitutes(
        CancellationToken cancellationToken)
    {
        CfuScheduleBackendCatalog? catalog = await LoadCfuCatalogAsync(cancellationToken);
        return catalog is null ? SourceUnavailable() : Ok(catalog.Institutes);
    }

    [HttpGet("institutes/{instituteId:guid}/directions")]
    public async Task<ActionResult<IReadOnlyList<DirectionSummary>>> GetDirections(
        Guid instituteId,
        CancellationToken cancellationToken)
    {
        CfuScheduleBackendCatalog? catalog = await LoadCfuCatalogAsync(cancellationToken);
        if (catalog is null)
        {
            return SourceUnavailable();
        }

        if (!catalog.Institutes.Any(item => item.Id == instituteId))
        {
            return NotFound();
        }

        return Ok(catalog.Directions.Where(item => item.InstituteId == instituteId));
    }

    [HttpGet("directions/{directionId:guid}/groups")]
    public async Task<ActionResult<IReadOnlyList<StudyGroupSummary>>> GetGroups(
        Guid directionId,
        [FromQuery] int? course,
        CancellationToken cancellationToken)
    {
        if (course is <= 0)
        {
            return BadRequest(CreateProblem(
                StatusCodes.Status400BadRequest,
                "Курс должен быть положительным числом."));
        }

        CfuScheduleBackendCatalog? catalog = await LoadCfuCatalogAsync(cancellationToken);
        if (catalog is null)
        {
            return SourceUnavailable();
        }

        if (!catalog.Directions.Any(item => item.Id == directionId))
        {
            return NotFound();
        }

        IEnumerable<StudyGroupSummary> groups = catalog.Groups.Where(item => item.DirectionId == directionId);
        if (course.HasValue)
        {
            groups = groups.Where(item => item.CourseNumber == course.Value);
        }

        return Ok(groups);
    }

    [HttpGet("groups/search")]
    public async Task<ActionResult<IReadOnlyList<CatalogGroupSearchItem>>> SearchGroups(
        [FromQuery] string query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
        {
            return BadRequest(CreateProblem(
                StatusCodes.Status400BadRequest,
                "Введите минимум два символа номера группы."));
        }

        CfuScheduleBackendCatalog? catalog = await LoadCfuCatalogAsync(cancellationToken);
        if (catalog is null)
        {
            return SourceUnavailable();
        }

        string normalized = query.Trim();
        Dictionary<Guid, DirectionSummary> directions = catalog.Directions.ToDictionary(item => item.Id);
        Dictionary<Guid, InstituteSummary> institutes = catalog.Institutes.ToDictionary(item => item.Id);
        CatalogGroupSearchItem[] result = catalog.Groups
            .Where(group => group.Name.Contains(normalized, StringComparison.CurrentCultureIgnoreCase))
            .Take(50)
            .Select(group =>
            {
                DirectionSummary direction = directions[group.DirectionId];
                InstituteSummary institute = institutes[direction.InstituteId];
                return new CatalogGroupSearchItem(
                    group.Id,
                    direction.Id,
                    institute.Id,
                    institute.Name,
                    direction.Name,
                    group.Name,
                    group.CourseNumber);
            })
            .ToArray();
        return Ok(result);
    }

    [HttpGet("teachers/search")]
    public async Task<ActionResult<IReadOnlyList<TeacherSummary>>> SearchTeachers(
        [FromQuery] string query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
        {
            return BadRequest(CreateProblem(
                StatusCodes.Status400BadRequest,
                "Введите минимум два символа фамилии преподавателя."));
        }

        ReferenceCatalogLoadResult? result = await catalogQuery.GetAsync(cancellationToken);
        if (result is null)
        {
            return CatalogUnavailable();
        }

        string normalized = query.Trim();
        TeacherSummary[] teachers = result.Snapshot.Teachers
            .Where(teacher => teacher.FullName.Contains(normalized, StringComparison.CurrentCultureIgnoreCase))
            .Take(50)
            .Select(teacher => new TeacherSummary(teacher.Id, teacher.FullName, teacher.Position))
            .ToArray();
        return Ok(teachers);
    }

    [HttpGet("teachers/{teacherId:guid}")]
    public async Task<ActionResult<TeacherReference>> GetTeacher(
        Guid teacherId,
        CancellationToken cancellationToken)
    {
        ReferenceCatalogLoadResult? result = await catalogQuery.GetAsync(cancellationToken);
        if (result is null)
        {
            return CatalogUnavailable();
        }

        TeacherReference? teacher = result.Snapshot.Teachers.SingleOrDefault(item => item.Id == teacherId);
        return teacher is null ? NotFound() : Ok(teacher);
    }

    private async Task<CfuScheduleBackendCatalog?> LoadCfuCatalogAsync(CancellationToken cancellationToken)
    {
        try
        {
            CfuDocumentLoadResult<CfuScheduleIndexDocument> index = await cfuClient.LoadIndexAsync(cancellationToken);
            AddSourceHeaders(index.UpdatedAtUtc, index.IsFromCache);
            return CfuScheduleBackendMapper.MapCatalog(index.Value);
        }
        catch (CfuScheduleUnavailableException)
        {
            return null;
        }
    }

    private void AddSourceHeaders(DateTimeOffset updatedAtUtc, bool isFromCache)
    {
        Response.Headers["X-Schedule-Source"] = isFromCache ? "postgresql-cache" : "cfu-live";
        Response.GetTypedHeaders().LastModified = updatedAtUtc;
    }

    private ObjectResult CatalogUnavailable() =>
        StatusCode(
            StatusCodes.Status503ServiceUnavailable,
            CreateProblem(
                StatusCodes.Status503ServiceUnavailable,
                "Справочник ещё не опубликован в PostgreSQL."));

    private ObjectResult SourceUnavailable() =>
        StatusCode(
            StatusCodes.Status503ServiceUnavailable,
            CreateProblem(
                StatusCodes.Status503ServiceUnavailable,
                "Расписание КФУ временно недоступно, а серверный кэш ещё пуст."));

    private static ProblemDetails CreateProblem(int status, string detail) => new()
    {
        Status = status,
        Title = "Не удалось выполнить запрос",
        Detail = detail,
    };
}
