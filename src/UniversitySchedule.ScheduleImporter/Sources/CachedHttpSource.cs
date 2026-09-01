using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace UniversitySchedule.ScheduleImporter.Sources;

public sealed class CachedHttpSource(
    HttpClient httpClient,
    ImportOptions options,
    ILogger<CachedHttpSource> logger)
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly ImportOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<CachedHttpSource> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly SemaphoreSlim _vuzopediaGate = new(1, 1);
    private DateTimeOffset _lastVuzopediaRequestUtc = DateTimeOffset.MinValue;

    public async Task<string> GetAsync(
        Uri uri,
        string cacheCategory,
        bool applyVuzopediaDelay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheCategory);

        string cachePath = GetCachePath(uri, cacheCategory);
        if (!_options.Refresh && File.Exists(cachePath))
        {
            return await File.ReadAllTextAsync(cachePath, cancellationToken);
        }

        string content;
        if (applyVuzopediaDelay)
        {
            await _vuzopediaGate.WaitAsync(cancellationToken);
            try
            {
                TimeSpan wait = _options.VuzopediaCrawlDelay -
                                (DateTimeOffset.UtcNow - _lastVuzopediaRequestUtc);
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, cancellationToken);
                }

                content = await DownloadAsync(uri, cancellationToken);
                _lastVuzopediaRequestUtc = DateTimeOffset.UtcNow;
            }
            finally
            {
                _vuzopediaGate.Release();
            }
        }
        else
        {
            content = await DownloadAsync(uri, cancellationToken);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        string temporaryPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporaryPath, content, new UTF8Encoding(false), cancellationToken);
        File.Move(temporaryPath, cachePath, overwrite: true);
        return content;
    }

    public async Task<string?> TryGetCachedAsync(
        Uri uri,
        string cacheCategory,
        CancellationToken cancellationToken)
    {
        string cachePath = GetCachePath(uri, cacheCategory);
        return File.Exists(cachePath)
            ? await File.ReadAllTextAsync(cachePath, cancellationToken)
            : null;
    }

    private async Task<string> DownloadAsync(Uri uri, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Downloading {Uri}", uri);
        using HttpResponseMessage response = await _httpClient.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private string GetCachePath(Uri uri, string category)
    {
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)))
            .ToLowerInvariant();
        string extension = uri.AbsolutePath.EndsWith("index", StringComparison.OrdinalIgnoreCase) ||
                           uri.AbsolutePath.Contains("/sched/", StringComparison.OrdinalIgnoreCase)
            ? ".json"
            : ".html";
        return Path.Combine(_options.CacheDirectory, category, $"{hash}{extension}");
    }
}
