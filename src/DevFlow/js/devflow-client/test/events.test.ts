import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import http from "node:http";
import type { AddressInfo } from "node:net";
import type { Duplex } from "node:stream";
import test from "node:test";

import { openEventStream } from "../src/events.js";
import { decodeFrame, OPCODE } from "../src/ws-frame.js";

const webSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

function serverTextFrame(value: unknown): Buffer {
  const payload = Buffer.from(JSON.stringify(value));
  assert.ok(payload.length < 126, "test event must fit in a short WebSocket frame");
  return Buffer.concat([Buffer.from([0x80 | OPCODE.TEXT, payload.length]), payload]);
}

function withTimeout<T>(promise: Promise<T>, timeoutMs = 3000): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error("event-stream test timed out")), timeoutMs);
    promise.then(
      (value) => {
        clearTimeout(timer);
        resolve(value);
      },
      (error) => {
        clearTimeout(timer);
        reject(error);
      },
    );
  });
}

test("event stream upgrades, subscribes, and delivers tree changes", async () => {
  const subscriptions: unknown[] = [];
  const acceptedSockets = new Set<Duplex>();
  const server = http.createServer();
  server.on("upgrade", (request, socket) => {
    acceptedSockets.add(socket);
    socket.once("close", () => acceptedSockets.delete(socket));
    const key = String(request.headers["sec-websocket-key"] || "");
    const accept = createHash("sha1").update(key + webSocketGuid).digest("base64");
    socket.write(
      "HTTP/1.1 101 Switching Protocols\r\n" +
      "Upgrade: websocket\r\n" +
      "Connection: Upgrade\r\n" +
      `Sec-WebSocket-Accept: ${accept}\r\n\r\n`,
    );

    let buffered: Buffer<ArrayBufferLike> = Buffer.alloc(0);
    socket.on("data", (chunk: Buffer) => {
      buffered = buffered.length ? Buffer.concat([buffered, chunk]) : chunk;
      const frame = decodeFrame(buffered);
      if (!frame || frame.opcode !== OPCODE.TEXT) return;
      buffered = frame.rest;
      subscriptions.push(JSON.parse(frame.payload.toString("utf8")));
      socket.write(serverTextFrame({ type: "treeChange", data: { reason: "test" } }));
    });
  });

  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  const port = (server.address() as AddressInfo).port;
  let resolveConnected!: () => void;
  const connected = new Promise<void>((resolve) => { resolveConnected = resolve; });
  let resolveEvent!: (event: { type: string; data?: unknown }) => void;
  const received = new Promise<{ type: string; data?: unknown }>((resolve) => { resolveEvent = resolve; });
  const stream = openEventStream({
    resolvePort: async () => port,
    events: ["treeChange", "themeChange"],
    onStatus: (status) => { if (status.connected) resolveConnected(); },
    onEvent: resolveEvent,
  });

  try {
    await withTimeout(connected);
    const event = await withTimeout(received);

    assert.deepEqual(subscriptions, [
      { type: "subscribe", data: { events: ["treeChange", "themeChange"] } },
    ]);
    assert.equal(event.type, "treeChange");
    assert.deepEqual(event.data, { reason: "test" });
  } finally {
    stream.close();
    for (const socket of acceptedSockets) socket.destroy();
    server.closeAllConnections();
    await new Promise<void>((resolve, reject) => server.close((error) => error ? reject(error) : resolve()));
  }
});

test("event stream reports polling-only without opening a socket when unsupported", async () => {
  let resolveStatus!: (transport: string | undefined) => void;
  const status = new Promise<string | undefined>((resolve) => { resolveStatus = resolve; });
  let capabilityChecks = 0;
  const stream = openEventStream({
    resolvePort: async () => 65535,
    supportsEvents: async () => {
      capabilityChecks++;
      return false;
    },
    onStatus: (value) => resolveStatus(value.transport),
    onEvent: () => assert.fail("unsupported event stream must not deliver events"),
  });

  try {
    assert.equal(await withTimeout(status), "polling-only");
    assert.equal(capabilityChecks, 1);
    await new Promise((resolve) => setTimeout(resolve, 100));
    assert.equal(capabilityChecks, 1, "unsupported agents must not retry forever");
  } finally {
    stream.close();
  }
});

test("event stream retries when capability discovery is temporarily unavailable", async () => {
  let capabilityChecks = 0;
  let resolvePolling!: (transport: string | undefined) => void;
  const polling = new Promise<string | undefined>((resolve) => { resolvePolling = resolve; });
  const stream = openEventStream({
    resolvePort: async () => 65535,
    supportsEvents: async () => {
      capabilityChecks++;
      return capabilityChecks === 1 ? null : false;
    },
    onStatus: (value) => { if (value.transport === "polling-only") resolvePolling(value.transport); },
    onEvent: () => assert.fail("unsupported event stream must not deliver events"),
  });

  try {
    assert.equal(await withTimeout(polling), "polling-only");
    assert.equal(capabilityChecks, 2);
  } finally {
    stream.close();
  }
});

test("polling-only event stream periodically rechecks for an in-place agent upgrade", async () => {
  let capabilityChecks = 0;
  let resolveSupported!: () => void;
  const supported = new Promise<void>((resolve) => { resolveSupported = resolve; });
  const stream = openEventStream({
    resolvePort: async () => 65535,
    unsupportedRetryMs: 1000,
    supportsEvents: async () => {
      capabilityChecks++;
      if (capabilityChecks === 2) resolveSupported();
      return false;
    },
    onStatus: () => {},
    onEvent: () => assert.fail("unsupported event stream must not deliver events"),
  });

  try {
    await withTimeout(supported, 2500);
    assert.equal(capabilityChecks, 2);
  } finally {
    stream.close();
  }
});