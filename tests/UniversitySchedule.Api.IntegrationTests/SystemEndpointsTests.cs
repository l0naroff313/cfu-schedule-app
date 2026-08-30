using System.Net.Http.Json;
using UniversitySchedule.Contracts.System;

namespace UniversitySchedule.Api.IntegrationTests;

public sealed class SystemEndpointsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public SystemEndpointsTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_ReturnsOkStatus()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            "/api/v1/system/health",
            CancellationToken.None);
        SystemHealthResponse? payload = await response.Content.ReadFromJsonAsync<SystemHealthResponse>(
            CancellationToken.None);

        response.EnsureSuccessStatusCode();
        Assert.NotNull(payload);
        Assert.Equal("ok", payload.Status);
        Assert.Equal(TimeSpan.Zero, payload.ServerTimeUtc.Offset);
    }

    [Fact]
    public async Task Readiness_ChecksDatabase()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            "/health/ready",
            CancellationToken.None);

        response.EnsureSuccessStatusCode();
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
