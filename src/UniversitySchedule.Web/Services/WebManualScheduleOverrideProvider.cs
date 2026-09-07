using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Json;
using UniversitySchedule.Mobile.Core.Cfu;

namespace UniversitySchedule.Web.Services;

public sealed class WebManualScheduleOverrideProvider(HttpClient httpClient) : IManualScheduleOverrideProvider
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

        try
        {
            _document = await httpClient.GetFromJsonAsync<ManualScheduleOverrideDocument>(
                "data/cfu-manual-schedule.json",
                JsonOptions,
                cancellationToken);
            _loaded = true;
        }
        catch (HttpRequestException)
        {
            _document = null;
        }
        catch (JsonException)
        {
            _document = null;
        }

        return _document;
    }
}
