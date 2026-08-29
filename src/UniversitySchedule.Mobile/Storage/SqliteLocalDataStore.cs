using SQLite;
using UniversitySchedule.Mobile.Core.Storage;

namespace UniversitySchedule.Mobile.Storage;

public sealed class SqliteLocalDataStore : ILocalDataStore
{
    private readonly SQLiteAsyncConnection _connection;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public SqliteLocalDataStore()
    {
        string databasePath = Path.Combine(FileSystem.AppDataDirectory, "cfu-eljournal.db3");
        _connection = new SQLiteAsyncConnection(
            databasePath,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
    }

    public async Task<LocalDocument?> GetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        await InitializeAsync(cancellationToken);

        CacheDocumentEntity? entity = await _connection.Table<CacheDocumentEntity>()
            .Where(document => document.Key == key)
            .FirstOrDefaultAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return entity is null
            ? null
            : new LocalDocument(
                entity.Key,
                entity.Content,
                DateTimeOffset.FromUnixTimeMilliseconds(entity.UpdatedAtUnixMilliseconds));
    }

    public async Task SaveAsync(
        LocalDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.Key);
        ArgumentNullException.ThrowIfNull(document.Content);
        cancellationToken.ThrowIfCancellationRequested();
        await InitializeAsync(cancellationToken);

        await _connection.InsertOrReplaceAsync(new CacheDocumentEntity
        {
            Key = document.Key,
            Content = document.Content,
            UpdatedAtUnixMilliseconds = document.UpdatedAtUtc.ToUnixTimeMilliseconds(),
        });
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await _connection.CreateTableAsync<CacheDocumentEntity>();
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    [Table("cache_documents")]
    private sealed class CacheDocumentEntity
    {
        [PrimaryKey]
        [Column("key")]
        public string Key { get; set; } = string.Empty;

        [Column("content")]
        public string Content { get; set; } = string.Empty;

        [Column("updated_at_unix_ms")]
        public long UpdatedAtUnixMilliseconds { get; set; }
    }
}
