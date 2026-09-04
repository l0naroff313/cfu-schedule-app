const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const { chromium, webkit } = require('playwright');
const { prepareBase } = require('../../tools/prepare-pwa-base.cjs');

const repository = path.resolve(__dirname, '../..');
const published = path.resolve(process.env.PWA_TEST_ROOT || path.join(repository, 'artifacts/pwa/wwwroot'));
const artifacts = path.join(repository, 'artifacts/offline-tests');
fs.mkdirSync(artifacts, { recursive: true });
const indexFixture = {
    bells: [{ 'пара': 1, 'начало': '08:00', 'конец': '09:30' }],
    weeks: { ch: ['2026-09-07'], nch: ['2026-09-14'] },
    tree: { 'ФТИ': { '09.03.04 Программная инженерия': { '2': ['ПИ-б-о-252'] } } }
};
const groupFixture = {
    'код': 'ПИ-б-о-252', 'fak': [],
    'занятия': [{
        'группа': 'ПИ-б-о-252', 'подгруппа': 1, 'день': 1, 'пара': 1,
        'чётность': 'чёт', 'предмет': 'Алгоритмы', 'вид': 'ЛК',
        'преподаватели': ['Иванова Н. П.'], 'аудитория': '305'
    }]
};
const mime = {
    '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.json': 'application/json',
    '.css': 'text/css', '.wasm': 'application/wasm', '.webmanifest': 'application/manifest+json',
    '.png': 'image/png', '.svg': 'image/svg+xml'
};

async function serve(directory, basePath, redirectIndex) {
    let online = true;
    let redirects = 0;
    const server = http.createServer((request, response) => {
        if (!online) { request.socket.destroy(); return; }
        const pathname = new URL(request.url, 'http://localhost').pathname;
        if (redirectIndex && pathname === `${basePath}index.html`) {
            redirects++;
            response.writeHead(308, { Location: basePath }).end();
            return;
        }
        const relative = pathname.slice(basePath.length) || 'index.html';
        const file = path.resolve(directory, relative);
        if (!pathname.startsWith(basePath) || !file.startsWith(directory + path.sep) || !fs.existsSync(file)) {
            response.writeHead(404).end(); return;
        }
        response.writeHead(200, {
            'Content-Type': mime[path.extname(file)] || 'application/octet-stream',
            'Cache-Control': 'no-store'
        });
        fs.createReadStream(file).pipe(response);
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    return {
        url: `http://127.0.0.1:${server.address().port}${basePath}`,
        setOnline(value) { online = value; },
        get redirects() { return redirects; },
        async close() {
            server.closeAllConnections();
            await new Promise(resolve => server.close(resolve));
        }
    };
}

async function ensureControlled(page) {
    // A newly installed worker can finish activation after the first document
    // has loaded. Reloading is deterministic and mirrors a real cold launch
    // after the user has opened the app once online.
    for (let attempt = 0; attempt < 3; attempt++) {
        const controlled = await page.evaluate(() => Boolean(navigator.serviceWorker?.controller));
        if (controlled) return;
        await page.evaluate(() => {
            if (!navigator.serviceWorker) return false;
            return Promise.race([
                navigator.serviceWorker.ready.then(() => true),
                new Promise(resolve => setTimeout(() => resolve(false), 3000))
            ]);
        });
        if (await page.evaluate(() => Boolean(navigator.serviceWorker?.controller))) return;
        await page.reload({ waitUntil: 'domcontentloaded', timeout: 10000 });
    }
    throw new Error('The page is not controlled by the service worker.');
}

for (const [browserName, browserType] of [['chromium', chromium], ['webkit', webkit]]) {
    for (const basePath of ['/', '/cfu-schedule-app/']) {
        const testOptions = browserName === 'webkit'
            ? { timeout: 180000, skip: 'Playwright WebKit cannot reliably activate service workers; verify the cold launch in iOS Safari.' }
            : { timeout: 180000 };
        test(`${browserName}: ${basePath} offline cold launch, cache repair and personal data`, testOptions, async t => {
            const root = fs.mkdtempSync(path.join(artifacts, `${browserName}-`));
            const site = path.join(root, 'site');
            fs.cpSync(published, site, { recursive: true });
            if (basePath !== '/') prepareBase(site, basePath);
            const server = await serve(site, basePath, basePath === '/');
            t.after(() => server.close());
            const context = await browserType.launchPersistentContext(path.join(root, 'browser'), {
                headless: true, viewport: { width: 390, height: 844 }, locale: 'ru-RU',
                serviceWorkers: 'allow'
            });
            t.after(() => context.close());
            context.setDefaultTimeout(30000);
            let online = true;
            await context.route('https://cfuv.ru/**', route => online
                ? route.fulfill({ json: route.request().url().includes('/index') ? indexFixture : groupFixture })
                : route.abort());
            let page = await context.newPage();
            page.on('pageerror', error => t.diagnostic(error.message));
            await page.goto(server.url);
            await page.getByRole('button', { name: 'Показать расписание', exact: true }).click();
            await page.locator('.profile-setup-layer').waitFor({ state: 'hidden' });
            await ensureControlled(page);
            await page.getByRole('navigation').getByRole('button', { name: 'Профиль', exact: true }).click();
            await page.locator('.offline-card button').click();
            await page.locator('.offline-card.ready').filter({ hasText: 'Готово' }).waitFor();
            assert.match(await page.locator('.offline-card').innerText(), /Готово.*1 занятий/s);
            const complete = await page.evaluate(() => cfuOffline.getStatus());
            assert.equal(complete.isReady, true);
            if (basePath === '/') assert.ok(server.redirects > 0, 'Exercise real Cloudflare-style index redirect');

            // A partial cache, even with index.html, must never receive green status.
            const removed = await page.evaluate(async () => {
                const registration = await navigator.serviceWorker.getRegistration();
                const cacheName = (await caches.keys()).find(name => name.includes('-v2-'));
                const cache = await caches.open(cacheName);
                const request = (await cache.keys()).find(request => request.url.endsWith('.dat'));
                if (!request) throw new Error('.NET globalization files were not cached.');
                await cache.delete(request);
                return request.url;
            });
            const incomplete = await page.evaluate(() => cfuOffline.getStatus());
            assert.equal(incomplete.isReady, false);
            assert.equal(incomplete.missingAssetCount, 1);
            const repaired = await page.evaluate(() => cfuOffline.prepare());
            assert.equal(repaired.isReady, true);
            assert.ok(await page.evaluate(url => caches.match(url).then(Boolean), removed));

            online = false;
            server.setOnline(false);
            await context.setOffline(true);
            await page.close();
            // A new document and runtime, with both network and HTTP cache unavailable.
            page = await context.newPage();
            await page.goto(`${server.url}?offline-test=1`);
            await page.getByRole('navigation').getByRole('button', { name: 'Профиль', exact: true }).click();
            await page.locator('.offline-card.ready').waitFor();
            assert.match(await page.locator('.student-card').innerText(), /ПИ-б-о-252/);
            const navigation = await page.evaluate(() => cfuOffline.getStatus());
            assert.equal(navigation.isReady, true);
            const cachedGroup = await page.evaluate(() => cfuStorage.getDocument('cfu:group:пи-б-о-252'));
            assert.match(cachedGroup.content, /Алгоритмы/);
            const missing = await page.evaluate(async () => {
                try { await fetch('missing-test.wasm'); return 'unexpected response'; }
                catch { return 'network error'; }
            });
            assert.equal(missing, 'network error', 'Missing binary must not return HTML');

            await page.getByRole('navigation').getByRole('button', { name: 'Заметки', exact: true }).click();
            await page.getByRole('button', { name: 'Новая заметка' }).click();
            await page.getByPlaceholder('Введите текст заметки').fill('Заметка из авиарежима');
            await page.getByRole('button', { name: 'Сохранить заметку', exact: true }).click();
            await page.locator('.editor-layer').waitFor({ state: 'hidden' });
            await page.reload();
            await page.getByRole('navigation').getByRole('button', { name: 'Заметки', exact: true }).click();
            await page.getByText('Заметка из авиарежима', { exact: true }).waitFor();
            await page.getByRole('navigation').getByRole('button', { name: 'Профиль', exact: true }).click();
            await page.locator('.offline-card.ready').waitFor();
            await page.screenshot({ path: path.join(artifacts, `${browserName}-${basePath === '/' ? 'root' : 'subpath'}-offline.png`), fullPage: true });
        });
    }
}
