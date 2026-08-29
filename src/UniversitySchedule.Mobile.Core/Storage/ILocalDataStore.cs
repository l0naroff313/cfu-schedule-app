namespace UniversitySchedule.Mobile.Core.Storage;

public sealed record LocalDocument(
    string Key,
    string Content,
    DateTimeOffset UpdatedAtUtc);

public interface ILocalDataStore
{
    Task<LocalDocument?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task SaveAsync(LocalDocument document, CancellationToken cancellationToken = default);
}
