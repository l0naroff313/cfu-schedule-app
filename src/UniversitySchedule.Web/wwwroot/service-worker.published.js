self.importScripts('./service-worker-assets.js');

const cachePrefix = 'cfu-eljournal-cache-';
const cacheName = `${cachePrefix}${self.assetsManifest.version}`;
const offlineAssets = self.assetsManifest.assets
    .filter(asset => /\.(dll|wasm|html|js|json|css|svg|png|woff2?)$/.test(asset.url))
    .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));

self.addEventListener('install', event => {
    event.waitUntil(caches.open(cacheName).then(cache => cache.addAll(offlineAssets)));
});

self.addEventListener('activate', event => {
    event.waitUntil(caches.keys().then(keys => Promise.all(
        keys.filter(key => key.startsWith(cachePrefix) && key !== cacheName).map(key => caches.delete(key))
    )));
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
