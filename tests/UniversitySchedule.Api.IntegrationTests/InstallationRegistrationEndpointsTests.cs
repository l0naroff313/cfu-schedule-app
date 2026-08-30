using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UniversitySchedule.Contracts.Identity;
using UniversitySchedule.Infrastructure.Persistence;

namespace UniversitySchedule.Api.IntegrationTests;

public sealed class InstallationRegistrationEndpointsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public InstallationRegistrationEndpointsTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Register_NewInstallation_StoresHashAndReturnsBearerToken()
    {
        Guid installationId = Guid.NewGuid();
        byte[] secretBytes = RandomNumberGenerator.GetBytes(32);
        var request = CreateRequest(installationId, secretBytes);
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/installations/register",
            request);
        RegisterInstallationResponse? payload = await response.Content
            .ReadFromJsonAsync<RegisterInstallationResponse>();

        response.EnsureSuccessStatusCode();
        Assert.NotNull(payload);
        Assert.Equal(installationId, payload.InstallationId);
        Assert.Equal("Bearer", payload.TokenType);
        Assert.True(payload.IsNewInstallation);
        Assert.Equal(3, payload.AccessToken.Split('.').Length);
        Assert.True(payload.ExpiresAtUtc > DateTimeOffset.UtcNow);

        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        AppDbContext dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await dbContext.Installations.SingleAsync(item => item.Id == installationId);
        byte[] expectedHash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(ApiFactory.SecretPepper),
            secretBytes);
        Assert.Equal(expectedHash, stored.SecretHash);
        Assert.NotEqual(secretBytes, stored.SecretHash);
    }

    [Fact]
    public async Task Register_SameCredentials_IsIdempotent()
    {
        Guid installationId = Guid.NewGuid();
        byte[] secretBytes = RandomNumberGenerator.GetBytes(32);
        var request = CreateRequest(installationId, secretBytes);
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage firstResponse = await client.PostAsJsonAsync(
            "/api/v1/installations/register",
            request);
        using HttpResponseMessage secondResponse = await client.PostAsJsonAsync(
            "/api/v1/installations/register",
            request with { AppVersion = "1.1.0" });
        RegisterInstallationResponse? secondPayload = await secondResponse.Content
            .ReadFromJsonAsync<RegisterInstallationResponse>();

        firstResponse.EnsureSuccessStatusCode();
        secondResponse.EnsureSuccessStatusCode();
        Assert.NotNull(secondPayload);
        Assert.False(secondPayload.IsNewInstallation);

        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        AppDbContext dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await dbContext.Installations.CountAsync(item => item.Id == installationId));
        Assert.Equal(
            "1.1.0",
            (await dbContext.Installations.SingleAsync(item => item.Id == installationId)).AppVersion);
    }

    [Fact]
    public async Task Register_ExistingIdWithDifferentSecret_ReturnsUnauthorized()
    {
        Guid installationId = Guid.NewGuid();
        using HttpClient client = _factory.CreateClient();
        using HttpResponseMessage firstResponse = await client.PostAsJsonAsync(
            "/api/v1/installations/register",
            CreateRequest(installationId, RandomNumberGenerator.GetBytes(32)));
        firstResponse.EnsureSuccessStatusCode();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/installations/register",
            CreateRequest(installationId, RandomNumberGenerator.GetBytes(32)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("desktop", "AQID")]
    [InlineData("android", "not-base64")]
    public async Task Register_InvalidCredentials_ReturnsBadRequest(string platform, string secret)
    {
        using HttpClient client = _factory.CreateClient();
        var request = new RegisterInstallationRequest(
            Guid.NewGuid(),
            secret,
            platform,
            "1.0.0");

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/installations/register",
            request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static RegisterInstallationRequest CreateRequest(Guid installationId, byte[] secret)
    {
        return new RegisterInstallationRequest(
            installationId,
            Convert.ToBase64String(secret),
            "android",
            "1.0.0");
    }
}
