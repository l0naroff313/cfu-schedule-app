using UniversitySchedule.ScheduleImporter;
using UniversitySchedule.ScheduleImporter.Sources;

var builder = Host.CreateApplicationBuilder(args);
ImportOptions options = ImportOptions.Parse(args, builder.Environment.ContentRootPath);
builder.Services.AddSingleton(options);
builder.Services.AddHttpClient<CachedHttpSource>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(45);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("CFU-Schedule-App/1.0 (+https://github.com/l0naroff313/cfu-schedule-app)");
});
builder.Services.AddSingleton<CfuScheduleSourceClient>();
builder.Services.AddSingleton<VuzopediaSourceClient>();
builder.Services.AddSingleton<ReferenceCatalogBuilder>();
builder.Services.AddSingleton<ReferenceCatalogWriter>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
