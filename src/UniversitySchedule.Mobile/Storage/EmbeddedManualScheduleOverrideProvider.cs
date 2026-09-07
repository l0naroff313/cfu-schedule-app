using System.Text.Json;
using System.Text.Json.Serialization;
using UniversitySchedule.Mobile.Core.Cfu;

namespace UniversitySchedule.Mobile.Storage;

public sealed class EmbeddedManualScheduleOverrideProvider : IManualScheduleOverrideProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private ManualScheduleOverrideDocument? _document;
    private bool _loaded;

    public async Task<ManualScheduleOverrideDocument?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded)
        {
            return _document;
        }

        _loaded = true;
        try
        {
            await using Stream stream = await FileSystem.OpenAppPackageFileAsync("cfu-manual-schedule.json");
            _document = await JsonSerializer.DeserializeAsync<ManualScheduleOverrideDocument>(
                stream,
                JsonOptions,
                cancellationToken);
        }
        catch (FileNotFoundException)
        {
            _document = null;
        }

        return _document;
    }
}
