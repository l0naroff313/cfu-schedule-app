using System.Text.Json;
using UniversitySchedule.Mobile.Core.Storage;

namespace UniversitySchedule.Mobile.Core.Profiles;

public sealed class AcademicProfileStore(ILocalDataStore localDataStore)
{
    private const string ProfileKey = "profile:academic";
    private readonly ILocalDataStore _localDataStore = localDataStore
        ?? throw new ArgumentNullException(nameof(localDataStore));

    public async Task<AcademicProfile?> GetAsync(CancellationToken cancellationToken = default)
    {
        LocalDocument? document = await _localDataStore.GetAsync(ProfileKey, cancellationToken);
        return document is null
            ? null
            : JsonSerializer.Deserialize<AcademicProfile>(document.Content);
    }

    public Task SaveAsync(
        AcademicProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return _localDataStore.SaveAsync(
            new LocalDocument(
                ProfileKey,
                JsonSerializer.Serialize(profile),
                DateTimeOffset.UtcNow),
            cancellationToken);
    }
}
