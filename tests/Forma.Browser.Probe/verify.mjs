import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const [playwrightModule, url = 'http://127.0.0.1:5196/'] = process.argv.slice(2);
assert.ok(playwrightModule, 'Pass the path to an already installed Playwright module; this probe never installs tools.');
const playwright = await import(pathToFileURL(resolve(playwrightModule)).href);
const artifacts = resolve(dirname(fileURLToPath(import.meta.url)), '../../artifacts/browser');
await mkdir(artifacts, {recursive: true});
const results = [];
for (const name of ['chromium', 'firefox', 'webkit']) {
    const browser = await playwright[name].launch();
    try {
        const page = await browser.newPage();
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        await page.goto(url);
        const status = page.locator('#status:not([data-state="loading"])');
        await status.waitFor({timeout: 120000});
        const text = await status.innerText();
        assert.equal(await status.getAttribute('data-state'), 'passed', `${name}: ${text}`);
        assert.deepEqual(errors, [], `${name} browser errors`);
        await page.screenshot({path: join(artifacts, `probe-${name}.png`)});
        results.push({browser: name, version: browser.version(), status: 'pass', result: text});
        console.log(`${name}: ${text}`);
    } finally {
        await browser.close();
    }
}
await writeFile(join(artifacts, 'probe-results.json'), JSON.stringify({
    url, verifiedAt: new Date().toISOString(), gpuRenderingVerified: true, results
}, null, 2) + '\n');
