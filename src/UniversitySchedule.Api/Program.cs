using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using UniversitySchedule.Api.Authentication;
using UniversitySchedule.Application.Identity;
using UniversitySchedule.Application.PersonalData;
using UniversitySchedule.Infrastructure.Identity;
using UniversitySchedule.Infrastructure.PersonalData;
using UniversitySchedule.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
