using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using UniversitySchedule.Application.Catalog;
using UniversitySchedule.Contracts.Catalog;
using UniversitySchedule.Domain.Catalog;

namespace UniversitySchedule.Api.Catalog;

public sealed record ReferenceCatalogLoadResult(
    ReferenceCatalogSnapshot Snapshot,
    string ContentHash,
    DateTimeOffset ImportedAtUtc);

public sealed class ReferenceCatalogQueryService(
    ReferenceCatalogPersistenceService persistenceService,
    IMemoryCache memoryCache)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public async Task<ReferenceCatalogLoadResult?> GetAsync(
        CancellationToken cancellationToken = default)
    {
        ReferenceCatalogDocument? document = await persistenceService.GetCurrentAsync(cancellationToken);
        if (document is null)
        {
            return null;
        }

        ReferenceCatalogSnapshot snapshot = memoryCache.GetOrCreate(
            $"reference-catalog:{document.ContentHash}",
            entry =>
            {
                entry.SetSlidingExpiration(TimeSpan.FromMinutes(30));
                return JsonSerializer.Deserialize<ReferenceCatalogSnapshot>(document.PayloadJson, JsonOptions)
                    ?? throw new InvalidDataException("The stored reference catalog is empty.");
            })!;
        return new ReferenceCatalogLoadResult(snapshot, document.ContentHash, document.ImportedAtUtc);
    }
}
