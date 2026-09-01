using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using UniversitySchedule.Application.Catalog;
using UniversitySchedule.Contracts.Catalog;
using UniversitySchedule.Contracts.Schedule;

namespace UniversitySchedule.Api.IntegrationTests;

public sealed class CatalogAndScheduleEndpointsTests
{
    [Fact]
    public async Task CatalogAndGroupSchedule_ReturnLiveDataThenPostgreSqlFallback()
    {
        await using var factory = new ApiFactory();
        await SeedCatalogAsync(factory);
        using HttpClient client = factory.CreateClient();

        InstituteSummary[]? institutes = await client.GetFromJsonAsync<InstituteSummary[]>(
            "/api/v1/catalog/institutes");
        InstituteSummary institute = Assert.Single(institutes!);
        DirectionSummary[]? directions = await client.GetFromJsonAsync<DirectionSummary[]>(
            $"/api/v1/catalog/institutes/{institute.Id:D}/directions");
        DirectionSummary direction = Assert.Single(directions!);
        StudyGroupSummary[]? groups = await client.GetFromJsonAsync<StudyGroupSummary[]>(
            $"/api/v1/catalog/directions/{direction.Id:D}/groups?course=2");
        StudyGroupSummary group = Assert.Single(groups!);

        using HttpResponseMessage liveResponse = await client.GetAsync(
            $"/api/v1/schedule/groups/{group.Id:D}?subgroup=1");
        ScheduleSnapshot? live = await liveResponse.Content.ReadFromJsonAsync<ScheduleSnapshot>();
        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        Assert.Equal("cfu-live", liveResponse.Headers.GetValues("X-Schedule-Source").Single());
        Assert.Equal("301", Assert.Single(live!.Lessons).Classroom);

        factory.CfuHandler.IsUnavailable = true;
        using HttpResponseMessage cachedResponse = await client.GetAsync(
            $"/api/v1/schedule/groups/{group.Id:D}?subgroup=1");
        ScheduleSnapshot? cached = await cachedResponse.Content.ReadFromJsonAsync<ScheduleSnapshot>();
        Assert.Equal(HttpStatusCode.OK, cachedResponse.StatusCode);
        Assert.Equal("postgresql-cache", cachedResponse.Headers.GetValues("X-Schedule-Source").Single());
        Assert.Equal(live.Version, cached!.Version);
    }

    [Fact]
    public async Task TeacherSearchScheduleAndCurrent_UseExactTeacherIdentity()
    {
        await using var factory = new ApiFactory();
        TeacherReference teacher = await SeedCatalogAsync(factory);
        using HttpClient client = factory.CreateClient();

        TeacherSummary[]? found = await client.GetFromJsonAsync<TeacherSummary[]>(
            "/api/v1/catalog/teachers/search?query=Иванов");
        Assert.Equal(teacher.Id, Assert.Single(found!).Id);

        ScheduleSnapshot? schedule = await client.GetFromJsonAsync<ScheduleSnapshot>(
            $"/api/v1/schedule/teachers/{teacher.Id:D}");
        ScheduleLesson lesson = Assert.Single(schedule!.Lessons);
        Assert.Equal("ПИ-б-о-252, подгруппа 1", Assert.Single(lesson.Groups).DisplayName);

        CurrentScheduleResponse? current = await client.GetFromJsonAsync<CurrentScheduleResponse>(
            $"/api/v1/schedule/teachers/{teacher.Id:D}/current?atUtc=2026-08-31T05:30:00Z");
        Assert.Equal(lesson.Id, current!.Current!.Id);
    }

    [Fact]
    public async Task Snapshot_SupportsEtagAndNotModified()
    {
        await using var factory = new ApiFactory();
        await SeedCatalogAsync(factory);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage first = await client.GetAsync("/api/v1/catalog/snapshot");
        string etag = first.Headers.ETag!.Tag;
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/catalog/snapshot");
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        using HttpResponseMessage second = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    private static async Task<TeacherReference> SeedCatalogAsync(ApiFactory factory)
    {
        TeacherIdentityParser.TryParse("Иванов Иван Иванович", out TeacherIdentity identity);
        Guid instituteId = CatalogStableId.Create("institute", "Физико-технический институт");
        Guid directionId = CatalogStableId.Create(
            "direction",
            "Физико-технический институт",
            "09.03.04 Программная инженерия");
        var teacher = new TeacherReference(
            CatalogStableId.Create("teacher", identity.Key),
            identity.Key,
            "Иванов Иван Иванович",
            "Иванов И.И.",
            "Иванов",
            "доцент",
            ["Проектирование приложений"],
            [],
            [],
            TeacherScheduleMatchStatus.Exact,
            null);
        var statistics = new ReferenceCatalogStatistics(1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, true);
        var snapshot = new ReferenceCatalogSnapshot(
            1,
            new DateTimeOffset(2026, 8, 30, 5, 0, 0, TimeSpan.Zero),
            new ReferenceCatalogSources("index", "api", "teachers", "specialties"),
            new ReferenceScheduleCalendar(
                [new ReferenceBell(1, "08:00", "09:30")],
                ["2026-08-31"],
                ["2026-09-07"]),
            [new AcademicProgramReference(
                directionId,
                instituteId,
                "Физико-технический институт",
                "09.03.04 Программная инженерия",
                "09.03.04",
                "Программная инженерия",
                EducationLevel.Bachelor,
                [StudyForm.FullTime],
                ["ПИ-б-о-252"],
                true,
                null)],
            [teacher],
            [],
            statistics);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };

        using IServiceScope scope = factory.Services.CreateScope();
        ReferenceCatalogPersistenceService service = scope.ServiceProvider
            .GetRequiredService<ReferenceCatalogPersistenceService>();
        await service.PublishAsync(new ReferenceCatalogPublishCommand(
            JsonSerializer.Serialize(snapshot, jsonOptions),
            snapshot.SchemaVersion,
            snapshot.GeneratedAtUtc,
            statistics.ProgramCount,
            statistics.GroupCount,
            statistics.TeacherCount));
        return teacher;
    }
}
