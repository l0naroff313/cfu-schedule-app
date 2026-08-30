using Microsoft.EntityFrameworkCore;
using UniversitySchedule.Infrastructure.Persistence;
using UniversitySchedule.ScheduleImporter;
using UniversitySchedule.ScheduleImporter.Sources;

var builder = Host.CreateApplicationBuilder(args);
ImportOptions options = ImportOptions.Parse(args, builder.Environment.ContentRootPath);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddHttpClient<CachedHttpSource>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(45);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("CFU-Schedule-App/1.0 (+https://github.com/l0naroff313/cfu-schedule-app)");
});
builder.Services.AddSingleton<CfuScheduleSourceClient>();
builder.Services.AddSingleton<VuzopediaSourceClient>();
builder.Services.AddSingleton<ReferenceCatalogBuilder>();
builder.Services.AddSingleton<ReferenceCatalogReader>();
if (!options.SeedPostgreSql)
{
    builder.Services.AddSingleton<IReferenceCatalogSink, ReferenceCatalogWriter>();
}

if (options.PublishPostgreSql)
{
    string connectionString = builder.Configuration.GetConnectionString("PostgreSql")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:PostgreSql is required with --publish-postgres or --seed-postgres.");
    builder.Services.AddDbContextFactory<AppDbContext>(db => db.UseNpgsql(connectionString));
    builder.Services.AddSingleton<ReferenceCatalogDatabaseWriter>();
    builder.Services.AddSingleton<IReferenceCatalogSink>(services =>
        services.GetRequiredService<ReferenceCatalogDatabaseWriter>());
    builder.Services.AddSingleton<IReferenceCatalogFailureSink>(services =>
        services.GetRequiredService<ReferenceCatalogDatabaseWriter>());
}

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
