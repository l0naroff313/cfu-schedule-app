using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using UniversitySchedule.Mobile.Core.Assignments;
using UniversitySchedule.Mobile.Core.Catalog;
using UniversitySchedule.Mobile.Core.Cfu;
using UniversitySchedule.Mobile.Core.Identity;
using UniversitySchedule.Mobile.Core.Notes;
using UniversitySchedule.Mobile.Core.Profiles;
using UniversitySchedule.Mobile.Core.Scheduling;
using UniversitySchedule.Mobile.Core.Storage;
using UniversitySchedule.Mobile.Core.Sync;
using UniversitySchedule.Web;
using UniversitySchedule.Web.Services;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    Timeout = TimeSpan.FromSeconds(30),
});
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddScoped<BrowserStorage>();
builder.Services.AddScoped<ILocalDataStore>(services => services.GetRequiredService<BrowserStorage>());
builder.Services.AddScoped<ISecureValueStore>(services => services.GetRequiredService<BrowserStorage>());
builder.Services.AddScoped<BrowserThemeService>();
builder.Services.AddScoped<WebOfflineShellService>();
builder.Services.AddScoped<IReferenceCatalogProvider, WebReferenceCatalogProvider>();
builder.Services.AddScoped(services => new CfuScheduleRepository(
    new HttpClient
    {
        BaseAddress = new Uri(CfuScheduleRepository.BaseAddress),
        Timeout = TimeSpan.FromSeconds(20),
    },
    services.GetRequiredService<ILocalDataStore>()));
builder.Services.AddScoped<AcademicProfileStore>();
builder.Services.AddScoped<ScheduleSession>();
builder.Services.AddScoped<DailyScheduleRefreshService>();
builder.Services.AddScoped<InstallationIdentityService>();

Uri? apiBaseAddress = TryGetHttpsUri(builder.Configuration["UniversityScheduleApi:BaseUrl"]);
var apiOptions = new UniversityScheduleApiOptions(apiBaseAddress, "web", "1.0.2");
builder.Services.AddSingleton(apiOptions);
builder.Services.AddScoped(services => new UniversityScheduleApiClient(
    new HttpClient
    {
        BaseAddress = apiBaseAddress,
        Timeout = TimeSpan.FromSeconds(20),
    },
    apiOptions,
    services.GetRequiredService<InstallationIdentityService>(),
    services.GetRequiredService<ISecureValueStore>(),
    services.GetRequiredService<TimeProvider>()));
builder.Services.AddScoped<PersonalDataSyncQueue>();
builder.Services.AddScoped<PersonalDataSynchronizer>();
builder.Services.AddScoped<PersonalDataSnapshotRestorer>();
builder.Services.AddScoped<Func<PersonalDataSnapshotRestorer>>(services =>
    services.GetRequiredService<PersonalDataSnapshotRestorer>);
builder.Services.AddScoped<PersonalDataSyncCoordinator>();
builder.Services.AddScoped<IPersonalDataChangeSink>(services =>
    services.GetRequiredService<PersonalDataSyncCoordinator>());
builder.Services.AddScoped<PersonalNoteStore>();
builder.Services.AddScoped<PersonalAssignmentStore>();
builder.Services.AddScoped<WebAppState>();

await builder.Build().RunAsync();

static Uri? TryGetHttpsUri(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    string normalized = value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/";
    return Uri.TryCreate(normalized, UriKind.Absolute, out Uri? result) &&
           result.Scheme == Uri.UriSchemeHttps
        ? result
        : null;
}
