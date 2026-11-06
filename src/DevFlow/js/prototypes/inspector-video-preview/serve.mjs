import { createReadStream } from "node:fs";
import { createServer } from "node:http";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = dirname(fileURLToPath(import.meta.url));
const files = new Map([
  ["/", ["probe.html", "text/html; charset=utf-8"]],
  ["/probe.html", ["probe.html", "text/html; charset=utf-8"]],
  ["/capability-probe.mjs", ["capability-probe.mjs", "text/javascript; charset=utf-8"]],
  ["/probe-page.mjs", ["probe-page.mjs", "text/javascript; charset=utf-8"]],
]);

const server = createServer((request, response) => {
  if (request.method !== "GET") {
    response.writeHead(405, {
      "Allow": "GET",
      "Content-Type": "text/plain; charset=utf-8",
    });
    response.end("Method not allowed");
    return;
  }

  const remoteAddress = request.socket.remoteAddress;
  if (remoteAddress !== "127.0.0.1" && remoteAddress !== "::1" && remoteAddress !== "::ffff:127.0.0.1") {
    response.writeHead(403, { "Content-Type": "text/plain; charset=utf-8" });
    response.end("Loopback only");
    return;
  }

  const address = server.address();
  if (!address || typeof address === "string") {
    response.writeHead(503, { "Content-Type": "text/plain; charset=utf-8" });
    response.end("Probe unavailable");
    return;
  }
  const allowedHosts = new Set([`127.0.0.1:${address.port}`, `localhost:${address.port}`]);
  if (!allowedHosts.has(request.headers.host ?? "")) {
    response.writeHead(403, { "Content-Type": "text/plain; charset=utf-8" });
    response.end("Invalid Host");
    return;
  }

  let pathname;
  try {
    pathname = new URL(request.url ?? "/", `http://${request.headers.host}`).pathname;
  } catch {
    response.writeHead(400, { "Content-Type": "text/plain; charset=utf-8" });
    response.end("Invalid URL");
    return;
  }
  const entry = files.get(pathname);
  if (!entry) {
    response.writeHead(404, { "Content-Type": "text/plain; charset=utf-8" });
    response.end("Not found");
    return;
  }
  response.writeHead(200, {
    "Cache-Control": "no-store",
    "Content-Security-Policy": "default-src 'self'; style-src 'unsafe-inline'",
    "Content-Type": entry[1],
    "Cross-Origin-Resource-Policy": "same-origin",
    "X-Content-Type-Options": "nosniff",
  });
  createReadStream(join(root, entry[0])).pipe(response);
});

server.listen(0, "127.0.0.1", () => {
  const address = server.address();
  if (!address || typeof address === "string") throw new Error("Probe server did not bind.");
  console.log(`DevFlow Preview media probe: http://127.0.0.1:${address.port}/`);
});

for (const signal of ["SIGINT", "SIGTERM"]) {
  process.on(signal, () => server.close(() => process.exit(0)));
}
