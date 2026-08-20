import { test } from "node:test";
import assert from "node:assert/strict";
import net from "node:net";
import { httpRaw, isConnError, parseJsonSafe } from "../src/http.js";

test("isConnError: genuine socket failures are true", () => {
  assert.equal(isConnError({ status: 0, error: "ECONNREFUSED" }), true);
  assert.equal(isConnError({ status: 0, error: "ECONNRESET" }), true);
  assert.equal(isConnError({ status: 0, error: "socket hang up" }), true);
  assert.equal(isConnError({ status: 0, error: "ETIMEDOUT" }), true);
});

test("isConnError: request timeout against a live socket is NOT a conn error", () => {
  // "timeout" (our httpRaw timeout) must not trigger a re-resolve.
  assert.equal(isConnError({ status: 0, error: "timeout" }), false);
});

test("isConnError: HTTP errors and success are not conn errors", () => {
  assert.equal(isConnError({ status: 500 }), false);
  assert.equal(isConnError({ status: 404, error: undefined }), false);
  assert.equal(isConnError({ status: 200 }), false);
  assert.equal(isConnError(null), false);
  assert.equal(isConnError(undefined), false);
});

test("parseJsonSafe: valid JSON", () => {
  assert.deepEqual(parseJsonSafe('{"a":1}'), { a: 1 });
  assert.deepEqual(parseJsonSafe("[1,2,3]"), [1, 2, 3]);
});

test("parseJsonSafe: salvages JSON with a stray preamble", () => {
  assert.deepEqual(parseJsonSafe('warning: something\n{"a":1}'), { a: 1 });
});

test("parseJsonSafe: empty / garbage → null", () => {
  assert.equal(parseJsonSafe(""), null);
  assert.equal(parseJsonSafe("   "), null);
  assert.equal(parseJsonSafe("not json at all"), null);
});

// ── Mid-response teardown ────────────────────────────────────────────────────
// A socket killed after headers but before the body completes must still settle.
// The response events are the only signal here: `req` never errors on a FIN or a
// plain destroy(), and the dead socket never reaches the request timeout — so a
// missing handler leaves the promise pending forever and wedges the resolver mutex.

/** Serve headers + a partial body, then tear the socket down with `kill`. */
function partialBodyServer(kill: (s: net.Socket) => void): Promise<net.Server> {
  return new Promise((ready) => {
    const srv = net.createServer((sock) => {
      sock.on("error", () => {
        /* client-side reset is expected */
      });
      sock.once("data", () => {
        sock.write(
          "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 100\r\n\r\n",
        );
        sock.write('{"partial":');
        setTimeout(() => kill(sock), 20);
      });
    });
    srv.listen(0, "127.0.0.1", () => ready(srv));
  });
}

const teardowns: Array<[string, (s: net.Socket) => void]> = [
  ["graceful FIN", (s) => s.end()],
  ["destroy", (s) => s.destroy()],
  ["reset", (s) => (s.resetAndDestroy ? s.resetAndDestroy() : s.destroy())],
];

for (const [name, kill] of teardowns) {
  test(`httpRaw: ${name} mid-response settles as a connection error`, async () => {
    const srv = await partialBodyServer(kill);
    const port = (srv.address() as net.AddressInfo).port;
    try {
      // A 30s timeout well beyond the test budget: if the fix regresses, this hangs
      // on the pending promise rather than passing via the timeout path.
      const r = await httpRaw(port, "GET", "/x", { timeoutMs: 30_000 });
      assert.equal(r.ok, false);
      assert.equal(r.status, 0);
      assert.ok(r.error, "expected an error code");
      assert.notEqual(r.error, "timeout", "must not settle via the request timeout");
      assert.equal(isConnError(r), true, `isConnError should recognize ${r.error}`);
    } finally {
      srv.close();
    }
  });
}

test("httpRaw: socket closed before any response settles as a connection error", async () => {
  const srv = net.createServer((sock) => sock.destroy());
  await new Promise<void>((r) => srv.listen(0, "127.0.0.1", () => r()));
  const port = (srv.address() as net.AddressInfo).port;
  try {
    const r = await httpRaw(port, "GET", "/x", { timeoutMs: 30_000 });
    assert.equal(r.ok, false);
    assert.equal(r.status, 0);
    assert.equal(isConnError(r), true);
  } finally {
    srv.close();
  }
});

test("httpRaw: a complete response still resolves with its body", async () => {
  const body = JSON.stringify({ hello: "world" });
  const srv = net.createServer((sock) => {
    sock.once("data", () => {
      sock.write(
        `HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: ${body.length}\r\n\r\n${body}`,
      );
      sock.end();
    });
  });
  await new Promise<void>((r) => srv.listen(0, "127.0.0.1", () => r()));
  const port = (srv.address() as net.AddressInfo).port;
  try {
    const r = await httpRaw(port, "GET", "/x", { timeoutMs: 30_000 });
    assert.equal(r.ok, true);
    assert.equal(r.status, 200);
    assert.equal(r.buffer?.toString("utf8"), body);
  } finally {
    srv.close();
  }
});

test("httpRaw: a body terminated by connection close is not truncated", async () => {
  // No Content-Length: the body ends when the socket does. Node emits `req.close`
  // BEFORE `res.end` here, so the request-close safety net must not settle early.
  const body = JSON.stringify({ hello: "close-delimited" });
  const srv = net.createServer((sock) => {
    sock.once("data", () => {
      sock.write(`HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n\r\n${body}`);
      sock.end();
    });
  });
  await new Promise<void>((r) => srv.listen(0, "127.0.0.1", () => r()));
  const port = (srv.address() as net.AddressInfo).port;
  try {
    const r = await httpRaw(port, "GET", "/x", { timeoutMs: 30_000 });
    assert.equal(r.ok, true);
    assert.equal(r.status, 200);
    assert.equal(r.buffer?.toString("utf8"), body);
  } finally {
    srv.close();
  }
});
