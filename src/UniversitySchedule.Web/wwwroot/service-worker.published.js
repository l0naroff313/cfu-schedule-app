self.importScripts('./service-worker-assets.js');

const cachePrefix = 'cfu-eljournal-cache-';
// The schema suffix prevents old redirected responses from being reused.
const cacheName = `${cachePrefix}v2-${self.assetsManifest.version}`;
const appShellUrl = new URL('index.html', self.registration.scope).href;
const offlineAssets = self.assetsManifest.assets
    .filter(asset => !/^(service-worker(\.published)?\.js|service-worker-assets\.js)$/.test(asset.url))
    .map(asset => new Request(new URL(asset.url, self.registration.scope), {
        integrity: asset.hash, cache: 'no-cache'
    }));

self.addEventListener('install', event => {
    event.waitUntil(prepareOfflineCache());
});

self.addEventListener('activate', event => {
    event.waitUntil(Promise.all([
        caches.keys().then(keys => Promise.all(
            keys.filter(key => key.startsWith(cachePrefix) && key !== cacheName).map(key => caches.delete(key))
        )),
        self.clients.claim()
    ]));
});

self.addEventListener('fetch', event => {
    if (event.request.method !== 'GET') return;
    const requestUrl = new URL(event.request.url);
    if (requestUrl.origin !== self.location.origin ||
        !requestUrl.pathname.startsWith(new URL(self.registration.scope).pathname)) return;

    event.respondWith((async () => {
        const cache = await caches.open(cacheName);
        if (event.request.mode === 'navigate') {
            const shell = await cache.match(appShellUrl);
            if (shell) return cleanResponse(shell);
        }

        const cached = await cache.match(event.request);
        if (cached) return cached;
        // Missing scripts, data and API responses must never receive index.html.
        return fetch(event.request);
    })());
});

self.addEventListener('message', event => {
    if (event.data?.type !== 'CFU_PREPARE_OFFLINE' && event.data?.type !== 'CFU_CHECK_OFFLINE') return;

    const operation = event.data.type === 'CFU_PREPARE_OFFLINE'
        ? prepareOfflineCache().then(checkOfflineCache)
        : checkOfflineCache();
    event.waitUntil(operation
        .then(status => event.ports[0]?.postMessage(status))
        .catch(error => event.ports[0]?.postMessage({
            protocolVersion: 2,
            isReady: false,
            cachedAssetCount: 0,
            missingAssetCount: offlineAssets.length,
            error: error instanceof Error ? error.message : String(error)
        })));
});

async function prepareOfflineCache() {
    const cache = await caches.open(cacheName);
    // Limit parallel downloads, including the .NET globalization .dat files.
    for (let offset = 0; offset < offlineAssets.length; offset += 6) {
        await Promise.all(offlineAssets.slice(offset, offset + 6).map(async request => {
            const cached = await cache.match(request);
            if (cached?.ok && !cached.redirected) return;
            const response = await fetch(request);
            if (!response.ok) throw new Error(`Не удалось сохранить файл: ${new URL(request.url).pathname}`);
            // Cloudflare redirects /index.html to /. Reconstructing the response
            // discards its redirect URL list, which Safari rejects for navigation.
            await cache.put(request, response.redirected || request.url === appShellUrl
                ? cleanResponse(response)
                : response);
        }));
    }
}

function cleanResponse(response) {
    const headers = new Headers(response.headers);
    headers.delete('content-encoding');
    headers.delete('content-length');
    return new Response(response.body, {
        status: response.status, statusText: response.statusText, headers
    });
}

async function checkOfflineCache() {
    const cache = await caches.open(cacheName);
    let cachedAssetCount = 0;
    for (const request of offlineAssets) {
        const response = await cache.match(request);
        if (response?.ok && !response.redirected) cachedAssetCount++;
    }

    const shell = await cache.match(appShellUrl);
    const hasUsableShell = shell?.ok && !shell.redirected &&
        shell.headers.get('content-type')?.includes('text/html') &&
        (await shell.text()).trim().length > 0;
    const missingAssetCount = offlineAssets.length - cachedAssetCount;
    return {
        protocolVersion: 2,
        isReady: offlineAssets.length > 0 && missingAssetCount === 0 && !!hasUsableShell,
        cachedAssetCount,
        missingAssetCount,
        error: null
    };
}
