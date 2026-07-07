import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';

const root = path.resolve(process.argv[2] || process.cwd());
const port = Number(process.argv[3] || 8787);
const logFile = process.argv[4] ? path.resolve(process.argv[4]) : null;

const log = (message) => {
  const line = `${new Date().toISOString()} ${message}\n`;
  console.log(line.trimEnd());
  if (logFile) {
    fs.mkdirSync(path.dirname(logFile), { recursive: true });
    fs.appendFileSync(logFile, line);
  }
};

const types = new Map([
  ['.json', 'application/json; charset=utf-8'],
  ['.webp', 'image/webp'],
  ['.txt', 'text/plain; charset=utf-8'],
  ['.html', 'text/html; charset=utf-8']
]);

const sendHeaders = (res, status, extra = {}) => {
  res.writeHead(status, {
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Methods': 'GET, HEAD, OPTIONS',
    'Access-Control-Allow-Headers': '*',
    'Access-Control-Allow-Private-Network': 'true',
    'Cross-Origin-Resource-Policy': 'cross-origin',
    'Cache-Control': 'no-store',
    ...extra
  });
};

const server = http.createServer((req, res) => {
  log(`${req.method} ${req.url}`);

  if (req.method === 'OPTIONS') {
    sendHeaders(res, 204);
    res.end();
    return;
  }

  if (req.method !== 'GET' && req.method !== 'HEAD') {
    sendHeaders(res, 405);
    res.end('Method not allowed');
    return;
  }

  let pathname;
  try {
    pathname = decodeURIComponent(new URL(req.url, 'http://127.0.0.1').pathname);
  } catch {
    sendHeaders(res, 400);
    res.end('Bad request');
    return;
  }

  const file = path.resolve(root, `.${pathname}`);
  if (file !== root && !file.startsWith(`${root}${path.sep}`)) {
    sendHeaders(res, 403);
    res.end('Forbidden');
    return;
  }

  fs.stat(file, (statErr, stat) => {
    if (statErr || !stat.isFile()) {
      sendHeaders(res, 404);
      res.end('Not found');
      return;
    }

    sendHeaders(res, 200, {
      'Content-Type': types.get(path.extname(file).toLowerCase()) || 'application/octet-stream',
      'Content-Length': stat.size
    });

    if (req.method === 'HEAD') {
      res.end();
      return;
    }

    fs.createReadStream(file).pipe(res);
  });
});

server.listen(port, '127.0.0.1', () => {
  log(`Serving ${root} at http://127.0.0.1:${port}/`);
});
