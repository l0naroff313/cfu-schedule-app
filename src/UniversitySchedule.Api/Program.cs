using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using UniversitySchedule.Api.Authentication;
using UniversitySchedule.Api.Catalog;
using UniversitySchedule.Application.Catalog;
using UniversitySchedule.Application.Identity;
using UniversitySchedule.Application.PersonalData;
using UniversitySchedule.Infrastructure.Catalog;
using UniversitySchedule.Infrastructure.Cfu;
using UniversitySchedule.Infrastructure.Identity;
using UniversitySchedule.Infrastructure.PersonalData;
using UniversitySchedule.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddMemoryCache();
builder.Services.AddResponseCompression(options => options.EnableForHttps = true);
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

builder.Services
    .AddOptions<InstallationAuthenticationOptions>()
    .Bind(builder.Configuration.GetSection(InstallationAuthenticationOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(
        options => options.HasSecureKeyLengths(),
        "SecretPepper and JwtSigningKey must each contain at least 32 UTF-8 bytes.")
    .ValidateOnStart();

string postgresConnection = builder.Configuration.GetConnectionString("PostgreSql")
    ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(postgresConnection));
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("postgresql");
builder.Services.AddOptions<CfuScheduleCacheOptions>()
    .Bind(builder.Configuration.GetSection(CfuScheduleCacheOptions.SectionName))
    .Validate(options => options.FreshCacheMinutes is >= 1 and <= 60, "FreshCacheMinutes must be between 1 and 60.")
    .ValidateOnStart();
builder.Services.AddHttpClient<CfuScheduleBackendClient>(client =>
{
    client.BaseAddress = new Uri(CfuScheduleBackendClient.BaseAddress);
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "CFU-Schedule-App-API/1.0 (+https://github.com/l0naroff313/cfu-schedule-app)");
});
builder.Services.AddScoped<IInstallationRepository, EfInstallationRepository>();
builder.Services.AddSingleton<IInstallationSecretHasher>(services =>
{
    InstallationAuthenticationOptions options = services
        .GetRequiredService<IOptions<InstallationAuthenticationOptions>>()
        .Value;
    return new HmacInstallationSecretHasher(options.SecretPepper);
});
builder.Services.AddSingleton<IInstallationAccessTokenIssuer, JwtInstallationAccessTokenIssuer>();
builder.Services.AddScoped<InstallationRegistrationService>();
builder.Services.AddScoped<IPersonalDataRepository, EfPersonalDataRepository>();
builder.Services.AddScoped<PersonalDataSyncService>();
builder.Services.AddScoped<IReferenceCatalogRepository, EfReferenceCatalogRepository>();
builder.Services.AddScoped<ReferenceCatalogPersistenceService>();
builder.Services.AddScoped<ReferenceCatalogQueryService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<InstallationAuthenticationOptions>>((jwtOptions, installationOptions) =>
    {
        InstallationAuthenticationOptions options = installationOptions.Value;
        jwtOptions.MapInboundClaims = false;
        jwtOptions.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateAudience = true,
            ValidAudience = options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.JwtSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    AppDbContext dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("OpenApi:Enabled"))
{
    app.MapOpenApi();
}

if (app.Configuration.GetValue("HttpsRedirection:Enabled", true))
{
    app.UseHttpsRedirection();
}
app.UseResponseCompression();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Name == "postgresql",
});

app.Run();

public partial class Program;
