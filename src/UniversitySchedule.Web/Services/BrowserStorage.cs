using System.Globalization;
using Microsoft.JSInterop;
using UniversitySchedule.Mobile.Core.Identity;
using UniversitySchedule.Mobile.Core.Storage;

namespace UniversitySchedule.Web.Services;

public sealed class BrowserStorage(IJSRuntime javaScript) : ILocalDataStore, ISecureValueStore
{
    public async Task<LocalDocument?> GetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        BrowserDocument? document = await javaScript.InvokeAsync<BrowserDocument?>(
            "cfuStorage.getDocument",
            cancellationToken,
            key);
        if (document is null || string.IsNullOrWhiteSpace(document.Content) ||
            !DateTimeOffset.TryParse(
                document.UpdatedAtUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset updatedAtUtc))
        {
            return null;
        }

        return new LocalDocument(key, document.Content, updatedAtUtc.ToUniversalTime());
    }

    public async Task SaveAsync(
        LocalDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        await javaScript.InvokeVoidAsync(
            "cfuStorage.saveDocument",
            cancellationToken,
            new BrowserDocument(
                document.Key,
                document.Content,
                document.UpdatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
    }

    async Task<string?> ISecureValueStore.GetAsync(
        string key,
        CancellationToken cancellationToken) =>
        await javaScript.InvokeAsync<string?>("cfuStorage.getSecret", cancellationToken, key);

    async Task ISecureValueStore.SetAsync(
        string key,
        string value,
        CancellationToken cancellationToken) =>
        await javaScript.InvokeVoidAsync("cfuStorage.setSecret", cancellationToken, key, value);

    async Task ISecureValueStore.RemoveAsync(
        string key,
        CancellationToken cancellationToken) =>
        await javaScript.InvokeVoidAsync("cfuStorage.removeSecret", cancellationToken, key);

    private sealed record BrowserDocument(string Key, string Content, string UpdatedAtUtc);
}
