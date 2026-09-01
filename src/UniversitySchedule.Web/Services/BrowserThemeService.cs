using Microsoft.JSInterop;

namespace UniversitySchedule.Web.Services;

public sealed class BrowserThemeService(IJSRuntime javaScript)
{
    public async Task<string> GetAsync(CancellationToken cancellationToken = default)
    {
        string? theme = await javaScript.InvokeAsync<string?>("cfuTheme.get", cancellationToken);
        return theme is "light" or "dark" ? theme : "light";
    }

    public ValueTask ApplyAsync(string theme, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(theme is "light" or "dark", true);
        return javaScript.InvokeVoidAsync("cfuTheme.apply", cancellationToken, theme);
    }

    public ValueTask<bool> IsStandaloneAsync(CancellationToken cancellationToken = default) =>
        javaScript.InvokeAsync<bool>("cfuPwa.isStandalone", cancellationToken);
}
