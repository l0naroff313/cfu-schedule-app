using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UniversitySchedule.Infrastructure.Persistence;

namespace UniversitySchedule.Api.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string SecretPepper = "integration-tests-installation-pepper-2026";
    public const string JwtSigningKey = "integration-tests-jwt-signing-key-2026";

    private readonly string _databaseName = $"api-tests-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InstallationAuthentication:Issuer"] = "UniversitySchedule.Api.Tests",
                ["InstallationAuthentication:Audience"] = "UniversitySchedule.Api.Tests.Client",
                ["InstallationAuthentication:SecretPepper"] = SecretPepper,
                ["InstallationAuthentication:JwtSigningKey"] = JwtSigningKey,
                ["InstallationAuthentication:AccessTokenMinutes"] = "15",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }
}
