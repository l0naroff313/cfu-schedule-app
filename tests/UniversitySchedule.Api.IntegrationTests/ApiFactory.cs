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

    public CfuApiStubHandler CfuHandler { get; } = new();

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
            services.AddHttpClient<UniversitySchedule.Infrastructure.Cfu.CfuScheduleBackendClient>()
                .ConfigurePrimaryHttpMessageHandler(() => CfuHandler);
        });
    }
}

public sealed class CfuApiStubHandler : HttpMessageHandler
{
    public bool IsUnavailable { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (IsUnavailable)
        {
            throw new HttpRequestException("Simulated CFU outage.");
        }

        string path = request.RequestUri!.PathAndQuery;
        string content = path.Contains("/index", StringComparison.Ordinal)
            ? IndexJson
            : path.Contains("/group", StringComparison.Ordinal)
                ? GroupJson
                : path.Contains("/find", StringComparison.Ordinal)
                    ? TeacherJson
                    : throw new InvalidOperationException($"Unexpected CFU request: {path}");
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json"),
        });
    }

    private const string IndexJson = """
        {
          "bells": [{ "пара": 1, "начало": "08:00", "конец": "09:30" }],
          "weeks": { "ch": ["2026-08-31"], "nch": ["2026-09-07"] },
          "tree": {
            "Физико-технический институт": {
              "09.03.04 Программная инженерия": { "2": ["ПИ-б-о-252"] }
            }
          }
        }
        """;

    private const string GroupJson = """
        {
          "код": "ПИ-б-о-252",
          "занятия": [{
            "группа": "ПИ-б-о-252",
            "подгруппа": 1,
            "день": 1,
            "пара": 1,
            "чётность": "чет",
            "предмет": "Проектирование приложений",
            "вид": "ЛК",
            "преподаватели": ["Иванов И.И."],
            "аудитория": "301",
            "корпус": "А"
          }],
          "fak": []
        }
        """;

    private const string TeacherJson = """
        [{
          "группа": "ПИ-б-о-252",
          "подгруппа": 1,
          "день": 1,
          "пара": 1,
          "чётность": "чет",
          "предмет": "Проектирование приложений",
          "вид": "ЛК",
          "преподаватели": ["Иванов И.И."],
          "аудитория": "301",
          "корпус": "А"
        }]
        """;
}
