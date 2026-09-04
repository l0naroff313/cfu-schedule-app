using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace UniversitySchedule.Web.Services;

public sealed class WebOfflineShellService(IJSRuntime javaScript)
{
    public ValueTask<WebOfflineShellStatus> GetStatusAsync(
        CancellationToken cancellationToken = default) =>
        javaScript.InvokeAsync<WebOfflineShellStatus>(
            "cfuOffline.getStatus",
            cancellationToken);

    public ValueTask<WebOfflineShellStatus> PrepareAsync(
        CancellationToken cancellationToken = default) =>
        javaScript.InvokeAsync<WebOfflineShellStatus>(
            "cfuOffline.prepare",
            cancellationToken);
}

public sealed record WebOfflineShellStatus(
    [property: JsonPropertyName("isSupported")] bool IsSupported,
    [property: JsonPropertyName("isReady")] bool IsReady,
    [property: JsonPropertyName("cachedAssetCount")] int CachedAssetCount,
    [property: JsonPropertyName("missingAssetCount")] int MissingAssetCount,
    [property: JsonPropertyName("error")] string? Error);
