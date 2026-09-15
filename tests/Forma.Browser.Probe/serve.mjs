import http from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, resolve, sep } from 'node:path';

const root = resolve(process.argv[2]);
const port = Number(process.argv[3] ?? 5196);
const types = { '.html': 'text/html', '.js': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm' };
http.createServer(async (request, response) => {
    const url = new URL(request.url, 'http://localhost');
    const file = resolve(root, '.' + decodeURIComponent(url.pathname === '/' ? '/index.html' : url.pathname));
    if (!file.startsWith(root + sep)) {
        response.writeHead(403).end();
        return;
    }
    try {
        const data = await readFile(file);
        response.writeHead(200, { 'Content-Type': types[extname(file)] ?? 'application/octet-stream', 'Cache-Control': 'no-store' });
        response.end(data);
    } catch (error) {
        if (error.code !== 'ENOENT') console.error(error);
        response.writeHead(error.code === 'ENOENT' ? 404 : 500).end();
    }
}).listen(port, '127.0.0.1', () => console.log(`Forma proof: http://127.0.0.1:${port}/`));
