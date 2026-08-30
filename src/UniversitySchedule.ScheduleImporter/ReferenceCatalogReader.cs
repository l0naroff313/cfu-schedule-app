using System.Text.Json;
using System.Text.Json.Serialization;
using UniversitySchedule.Contracts.Catalog;

namespace UniversitySchedule.ScheduleImporter;

public sealed class ReferenceCatalogReader(ImportOptions options)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public async Task<ReferenceCatalogSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(options.OutputPath))
        {
            throw new FileNotFoundException(
                "The existing reference catalog was not found for PostgreSQL seeding.",
                options.OutputPath);
        }

        await using FileStream stream = File.OpenRead(options.OutputPath);
        return await JsonSerializer.DeserializeAsync<ReferenceCatalogSnapshot>(
                   stream,
                   JsonOptions,
                   cancellationToken)
               ?? throw new InvalidDataException("The existing reference catalog is empty.");
    }
}
