import http from "node:http";
import https from "node:https";
import { createReadStream } from "node:fs";
import { access, stat } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../dist");
const targetValue = process.env.ATENDEE_API_PROXY_TARGET;
const target = targetValue ? new URL(targetValue) : null;
const port = Number(process.env.PORT ?? "4173");
const mime = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".svg": "image/svg+xml",
  ".png": "image/png",
  ".ico": "image/x-icon"
};

function securityHeaders(response) {
  response.setHeader("X-Content-Type-Options", "nosniff");
  response.setHeader("X-Frame-Options", "DENY");
  response.setHeader("Referrer-Policy", "no-referrer");
  response.setHeader("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
  response.setHeader("Content-Security-Policy", "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'");
  response.setHeader("Cache-Control", "no-store");
}

const server = http.createServer(async (request, response) => {
  securityHeaders(response);

  if (request.url?.startsWith("/api/")) {
    if (!target) {
      response.writeHead(503, { "Content-Type": "application/json" });
      response.end(JSON.stringify({ error: "development_api_proxy_not_configured" }));
      return;
    }

    const upstreamUrl = new URL(request.url, target);
    const transport = upstreamUrl.protocol === "https:" ? https : http;
    const headers = { ...request.headers, host: upstreamUrl.host };
    const upstream = transport.request(upstreamUrl, {
      method: request.method,
      headers,
      rejectUnauthorized: false
    }, upstreamResponse => {
      response.writeHead(upstreamResponse.statusCode ?? 502, upstreamResponse.headers);
      upstreamResponse.pipe(response);
    });
    upstream.on("error", () => {
      if (!response.headersSent) response.writeHead(502);
      response.end();
    });
    request.pipe(upstream);
    return;
  }

  try {
    const pathname = decodeURIComponent(new URL(request.url ?? "/", "http://localhost").pathname);
    const requested = pathname === "/" ? "index.html" : pathname.replace(/^\/+/, "");
    let file = path.resolve(root, requested);
    if (!file.startsWith(root + path.sep) && file !== path.join(root, "index.html")) {
      response.writeHead(404);
      response.end();
      return;
    }
    try {
      await access(file);
      if (!(await stat(file)).isFile()) file = path.join(root, "index.html");
    } catch {
      file = path.join(root, "index.html");
    }
    response.setHeader("Content-Type", mime[path.extname(file)] ?? "application/octet-stream");
    createReadStream(file).pipe(response);
  } catch {
    response.writeHead(400);
    response.end();
  }
});

server.listen(port, "127.0.0.1", () => {
  console.log(`Atendee portal development server: http://127.0.0.1:${port}`);
  if (!target) console.log("API proxy not configured; set ATENDEE_API_PROXY_TARGET to the local ASP.NET API origin.");
});