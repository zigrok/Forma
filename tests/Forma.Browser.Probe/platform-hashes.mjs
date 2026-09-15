import { createHash } from 'node:crypto';
import { readdir, readFile, writeFile } from 'node:fs/promises';
import { join, relative, resolve } from 'node:path';
import assert from 'node:assert/strict';

const [rootPath, manifestPath, mode] = process.argv.slice(2);
const root = resolve(rootPath);
const hashes = {};
async function visit(directory) {
    for (const entry of (await readdir(directory, {withFileTypes: true})).sort((a, b) => a.name.localeCompare(b.name))) {
        if (directory === root && ['modules', 'fonts', 'licenses'].includes(entry.name)) continue;
        const path = join(directory, entry.name);
        if (entry.isDirectory()) await visit(path);
        else if (entry.isFile()) hashes[relative(root, path)] = createHash('sha256').update(await readFile(path)).digest('hex');
    }
}
await visit(root);
if (mode === 'verify') {
    assert.deepEqual(hashes, JSON.parse(await readFile(manifestPath, 'utf8')), 'Published platform changed while compiling standalone modules');
    console.log(`PASS: ${Object.keys(hashes).length} frozen platform files unchanged.`);
} else {
    await writeFile(manifestPath, JSON.stringify(hashes, null, 2) + '\n');
}
