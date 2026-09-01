using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using UniversitySchedule.Contracts.Catalog;
using UniversitySchedule.Mobile.Core.Catalog;

namespace UniversitySchedule.Web.Services;

public sealed class WebReferenceCatalogProvider(HttpClient httpClient) : IReferenceCatalogProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
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

            _snapshot = await httpClient.GetFromJsonAsync<ReferenceCatalogSnapshot>(
                "data/cfu-reference-catalog.json",
                JsonOptions,
                cancellationToken)
                ?? throw new InvalidDataException("Справочник преподавателей КФУ пуст.");
            if (_snapshot.Teachers.Count == 0)
            {
                throw new InvalidDataException("Справочник преподавателей КФУ не содержит записей.");
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
