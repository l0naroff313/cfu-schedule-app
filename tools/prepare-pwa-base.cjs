const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const vm = require('node:vm');

function prepareBase(directory, basePath) {
    if (!/^\/[a-zA-Z0-9/_-]*\/$/.test(basePath) && basePath !== '/') {
        throw new Error('Expected an absolute base path ending in /.');
    }
    const indexPath = path.join(directory, 'index.html');
    const index = fs.readFileSync(indexPath, 'utf8').replace(/<base href="[^"]*"\s*\/>/, `<base href="${basePath}" />`);
    fs.writeFileSync(indexPath, index);
    const manifestPath = path.join(directory, 'service-worker-assets.js');
    const context = { self: {} };
    vm.runInNewContext(fs.readFileSync(manifestPath, 'utf8'), context);
    const manifest = context.self.assetsManifest;
    const entry = manifest.assets.find(asset => asset.url === 'index.html');
    if (!entry) throw new Error('index.html is absent from the offline manifest.');
    entry.hash = `sha256-${crypto.createHash('sha256').update(index).digest('base64')}`;
    manifest.version = crypto.createHash('sha256').update(JSON.stringify(manifest.assets)).digest('hex').slice(0, 16);
    fs.writeFileSync(manifestPath, `self.assetsManifest = ${JSON.stringify(manifest, null, 2)};\n`);
    // Precompressed copies would otherwise serve stale content and fail integrity.
    for (const file of [indexPath, manifestPath]) {
        for (const extension of ['.br', '.gz']) fs.rmSync(file + extension, { force: true });
    }
    fs.copyFileSync(indexPath, path.join(directory, '404.html'));
    fs.writeFileSync(path.join(directory, '.nojekyll'), '');
}

module.exports = { prepareBase };
if (require.main === module) prepareBase(process.argv[2], process.argv[3]);
