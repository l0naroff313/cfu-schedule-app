using System.Text.Json;
using System.Text.Json.Serialization;
using UniversitySchedule.Contracts.Catalog;
using UniversitySchedule.Mobile.Core.Catalog;

namespace UniversitySchedule.Mobile.Storage;

public sealed class EmbeddedReferenceCatalogProvider : IReferenceCatalogProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private ReferenceCatalogSnapshot? _snapshot;
    private bool _loaded;

    public async Task<ReferenceCatalogSnapshot?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded)
        {
            return _snapshot;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_loaded)
            {
                return _snapshot;
            }

            try
            {
                await using Stream stream = await FileSystem.OpenAppPackageFileAsync("cfu-reference-catalog.json");
                _snapshot = await JsonSerializer.DeserializeAsync<ReferenceCatalogSnapshot>(stream, JsonOptions, cancellationToken);
            }
            catch (FileNotFoundException)
            {
                _snapshot = null;
            }

            _loaded = true;
            return _snapshot;
        }
        finally
        {
            _lock.Release();
        }
    }
}
