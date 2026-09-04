self.importScripts('./service-worker-assets.js');

const cachePrefix = 'cfu-eljournal-cache-';
const cacheName = `${cachePrefix}${self.assetsManifest.version}`;
const offlineAssets = self.assetsManifest.assets
    .filter(asset => /\.(dll|wasm|html|js|json|css|svg|png|woff2?)$/.test(asset.url))
    .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));

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
    if (requestUrl.origin !== self.location.origin) return;

    event.respondWith(caches.match(event.request).then(cached => {
        if (cached) return cached;
        return fetch(event.request).catch(() => caches.match('index.html'));
    }));
});

self.addEventListener('message', event => {
    if (event.data?.type !== 'CFU_PREPARE_OFFLINE' && event.data?.type !== 'CFU_CHECK_OFFLINE') return;

    const operation = event.data.type === 'CFU_PREPARE_OFFLINE'
        ? prepareOfflineCache().then(checkOfflineCache)
        : checkOfflineCache();
    event.waitUntil(operation
        .then(status => event.ports[0]?.postMessage(status))
        .catch(error => event.ports[0]?.postMessage({
            isReady: false,
            cachedAssetCount: 0,
            missingAssetCount: offlineAssets.length,
            error: error instanceof Error ? error.message : String(error)
        })));
});

async function prepareOfflineCache() {
    const cache = await caches.open(cacheName);
    const missingAssets = [];
    for (const request of offlineAssets) {
        if (!await cache.match(request)) missingAssets.push(request);
    }

    if (missingAssets.length > 0) await cache.addAll(missingAssets);
}

async function checkOfflineCache() {
    const cache = await caches.open(cacheName);
    let cachedAssetCount = 0;
    for (const request of offlineAssets) {
        if (await cache.match(request)) cachedAssetCount++;
    }

    const missingAssetCount = offlineAssets.length - cachedAssetCount;
    return {
        isReady: missingAssetCount === 0,
        cachedAssetCount,
        missingAssetCount,
        error: null
    };
}
