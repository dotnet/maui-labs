import test from "node:test";
import assert from "node:assert/strict";
import { DeviceLeaseClient } from "../lease.mjs";

test("canvas mutation claims, begins, ends, and releases through the broker", async () => {
  const calls = [];
  const client = new DeviceLeaseClient({
    readState: async () => ({ port: 19223 }),
    fetch: async (_url, options) => {
      const body = JSON.parse(options.body);
      calls.push(body);
      return {
        ok: true,
        json: async () => ({
          ok: true,
          allowed: body.action !== "release",
          youHold: body.action !== "release",
          heldByOther: false,
          transactionId: body.action === "begin" || body.action === "heartbeat"
            ? body.transactionId
            : null,
        }),
      };
    },
  });

  const result = await client.run(
    { instanceId: "canvas-1" },
    { deviceId: "ios:simulator:A1B2" },
    async () => "done",
  );

  assert.equal(result, "done");
  assert.deepEqual(calls.map((call) => call.action), ["claim", "begin", "end", "release"]);
  assert.equal(new Set(calls.map((call) => call.leaseId)).size, 1);
  assert.ok(calls.every((call) => call.deviceId === "ios:simulator:A1B2"));
});

test("canvas mutation refuses to run without a broker", async () => {
  let ran = false;
  const client = new DeviceLeaseClient({ readState: async () => null, fetch: async () => null });

  await assert.rejects(
    client.run(
      { instanceId: "canvas-1" },
      { deviceId: "ios:simulator:A1B2" },
      async () => { ran = true; },
    ),
    /broker is required/i,
  );
  assert.equal(ran, false);
});

test("unknown completion leaves the broker transaction open", async () => {
  const calls = [];
  const client = new DeviceLeaseClient({
    readState: async () => ({ port: 19223 }),
    fetch: async (_url, options) => {
      const body = JSON.parse(options.body);
      calls.push(body);
      return {
        ok: true,
        json: async () => ({
          ok: true,
          allowed: true,
          youHold: true,
          heldByOther: false,
          transactionId: body.action === "begin" ? body.transactionId : null,
        }),
      };
    },
  });

  const failure = new Error("timed out");
  failure.unknownCompletion = true;

  await assert.rejects(
    client.run(
      { instanceId: "canvas-1" },
      { catalog: true },
      async () => { throw failure; },
    ),
    /timed out/,
  );

  assert.deepEqual(calls.map((call) => call.action), ["claim", "begin"]);
  assert.ok(calls.every((call) => call.catalog === true));
});

test("post-success cleanup failure never changes a completed mutation into a failure", async () => {
  const calls = [];
  const warnings = [];
  const originalWarn = console.warn;
  console.warn = (message) => warnings.push(String(message));
  const client = new DeviceLeaseClient({
    readState: async () => ({ port: 19223 }),
    fetch: async (_url, options) => {
      const body = JSON.parse(options.body);
      calls.push(body);
      if (body.action === "end") throw new Error("broker restarted");
      return {
        ok: true,
        json: async () => ({
          ok: true,
          allowed: body.action !== "release",
          youHold: body.action !== "release",
          heldByOther: false,
          transactionId: body.action === "begin" ? body.transactionId : null,
        }),
      };
    },
  });

  try {
    const result = await client.run(
      { instanceId: "canvas-1" },
      { deviceId: "ios:simulator:A1B2" },
      async () => "completed",
    );

    assert.equal(result, "completed");
    assert.deepEqual(calls.map((call) => call.action), ["claim", "begin", "end", "release"]);
    assert.equal(warnings.length, 1);
    assert.match(warnings[0], /cleanup was incomplete/i);
  } finally {
    console.warn = originalWarn;
  }
});

test("lost heartbeat aborts the active canvas command and retains the transaction", async () => {
  const calls = [];
  const client = new DeviceLeaseClient({
    heartbeatMs: 5,
    readState: async () => ({ port: 19223 }),
    fetch: async (_url, options) => {
      const body = JSON.parse(options.body);
      calls.push(body);
      const allowed = body.action !== "heartbeat";
      return {
        ok: true,
        json: async () => ({
          ok: true,
          allowed,
          youHold: allowed,
          heldByOther: !allowed,
          transactionId: body.action === "begin" ? body.transactionId : null,
        }),
      };
    },
  });

  await assert.rejects(
    client.run(
      { instanceId: "canvas-1" },
      { deviceId: "ios:simulator:A1B2" },
      (signal) => new Promise((_resolve, reject) => {
        signal.addEventListener("abort", () => reject(signal.reason), { once: true });
      }),
    ),
    /lease was lost/,
  );

  assert.deepEqual(calls.map((call) => call.action), ["claim", "begin", "heartbeat"]);
});

test("concurrent canvas mutations use operation-scoped lease identities", async () => {
  const calls = [];
  const deferred = () => {
    let resolve;
    const promise = new Promise((completion) => { resolve = completion; });
    return { promise, resolve };
  };
  const gates = [deferred(), deferred()];
  const client = new DeviceLeaseClient({
    readState: async () => ({ port: 19223 }),
    fetch: async (_url, options) => {
      const body = JSON.parse(options.body);
      calls.push(body);
      return {
        ok: true,
        json: async () => ({
          ok: true,
          allowed: body.action !== "release",
          youHold: body.action !== "release",
          heldByOther: false,
          transactionId: body.action === "begin" || body.action === "heartbeat"
            ? body.transactionId
            : null,
        }),
      };
    },
  });

  const operations = gates.map((gate) => client.run(
    { instanceId: "canvas-1" },
    { deviceId: "ios:simulator:A1B2" },
    async () => gate.promise,
  ));
  await Promise.all(gates.map(async (_gate, index) => {
    while (calls.filter((call) => call.action === "begin").length <= index)
      await new Promise((resolve) => setTimeout(resolve, 1));
  }));
  gates.forEach((gate, index) => gate.resolve(`done-${index}`));

  assert.deepEqual(await Promise.all(operations), ["done-0", "done-1"]);
  const claims = calls.filter((call) => call.action === "claim");
  assert.equal(claims.length, 2);
  assert.equal(new Set(claims.map((call) => call.leaseId)).size, 2);
});
