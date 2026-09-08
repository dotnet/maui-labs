import { test } from "node:test";
import assert from "node:assert/strict";
import { createTransport, READ_ONLY, INTERACT, FULL } from "../src/transport.js";
import type { TransportClient } from "../src/transport.js";
import { ok } from "../src/types.js";
import type { DevFlowResult, ElementInfo } from "../src/types.js";

function fakeClient(): { client: TransportClient; calls: string[] } {
  const calls: string[] = [];
  const client: TransportClient = {
    target: { port: 1, platform: "test", appName: "t" },
    async getTree(depth?: number): Promise<DevFlowResult<ElementInfo[]>> {
      calls.push(`getTree:${depth ?? ""}`);
      return ok([{ id: "1", type: "Button", fullType: "X.Button" }]);
    },
    async screenshot(): Promise<DevFlowResult<Buffer>> {
      calls.push("screenshot");
      return ok(Buffer.from("png"));
    },
    async tap(t): Promise<DevFlowResult<void>> {
      calls.push(`tap:${JSON.stringify(t)}`);
      return ok(undefined);
    },
    async scroll(): Promise<DevFlowResult<void>> {
      calls.push("scroll");
      return ok(undefined);
    },
    async gesture(): Promise<DevFlowResult<void>> {
      calls.push("gesture");
      return ok(undefined);
    },
    async back(): Promise<DevFlowResult<void>> {
      calls.push("back");
      return ok(undefined);
    },
    async fill(id, text): Promise<DevFlowResult<void>> {
      calls.push(`fill:${id}:${text}`);
      return ok(undefined);
    },
    async key(k): Promise<DevFlowResult<void>> {
      calls.push(`key:${k}`);
      return ok(undefined);
    },
    async setProperty(id, name, value): Promise<DevFlowResult<void>> {
      calls.push(`setProperty:${id}:${name}:${value}`);
      return ok(undefined);
    },
    openEvents() {
      calls.push("openEvents");
      return { close: () => calls.push("close") };
    },
  };
  return { client, calls };
}

test("READ_ONLY permits reads, denies mutations/screenshot/setProperty", async () => {
  const { client, calls } = fakeClient();
  const t = createTransport(client, { permissions: READ_ONLY });

  assert.equal((await t.request({ kind: "getTree" })).ok, true);

  const tap = await t.request({ kind: "tap", elementId: "1" });
  assert.equal(tap.ok, false);
  assert.equal(!tap.ok && tap.error.kind, "permission-denied");

  const shot = await t.request({ kind: "getScreenshot" });
  assert.equal(!shot.ok && shot.error.kind, "permission-denied");

  const sp = await t.request({ kind: "setProperty", elementId: "1", name: "Text", value: "x" });
  assert.equal(!sp.ok && sp.error.kind, "permission-denied");

  assert.deepEqual(calls, ["getTree:"]); // only the read reached the client
});

test("getState returns the tree under read permission", async () => {
  const { client } = fakeClient();
  const t = createTransport(client, { permissions: READ_ONLY });
  const res = await t.request({ kind: "getState" });
  assert.equal(res.ok, true);
  assert.ok(res.ok && (res.value as { tree: unknown[] }).tree.length === 1);
});

test("INTERACT allows tap/scroll/gesture/back but not setProperty", async () => {
  const { client } = fakeClient();
  const t = createTransport(client, { permissions: INTERACT });

  assert.equal((await t.request({ kind: "tap", elementId: "1" })).ok, true);
  assert.equal((await t.request({ kind: "tap", x: 10, y: 20 })).ok, true);
  assert.equal((await t.request({ kind: "back" })).ok, true);
  assert.equal((await t.request({ kind: "scroll", x: 1, y: 2, deltaX: 0, deltaY: 40 })).ok, true);

  const sp = await t.request({ kind: "setProperty", elementId: "1", name: "Text", value: "x" });
  assert.equal(!sp.ok && sp.error.kind, "permission-denied");
});

test("tap requires elementId or finite coords", async () => {
  const { client } = fakeClient();
  const t = createTransport(client, { permissions: INTERACT });
  const bad = await t.request({ kind: "tap" });
  assert.equal(!bad.ok && bad.error.kind, "invalid-argument");
  const nan = await t.request({ kind: "tap", x: Number.NaN, y: 1 });
  assert.equal(!nan.ok && nan.error.kind, "invalid-argument");
});

test("scroll rejects non-finite values", async () => {
  const { client } = fakeClient();
  const t = createTransport(client, { permissions: INTERACT });
  const bad = await t.request({ kind: "scroll", x: 0, y: 0, deltaX: Number.POSITIVE_INFINITY, deltaY: 0 });
  assert.equal(!bad.ok && bad.error.kind, "invalid-argument");
});

test("gesture enforces point count bounds", async () => {
  const { client } = fakeClient();
  const t = createTransport(client, { permissions: INTERACT });
  const none = await t.request({ kind: "gesture", points: [] });
  assert.equal(!none.ok && none.error.kind, "invalid-argument");
  const many = await t.request({
    kind: "gesture",
    points: Array.from({ length: 513 }, () => ({ x: 1, y: 1 })),
  });
  assert.equal(!many.ok && many.error.kind, "invalid-argument");
  const okReq = await t.request({ kind: "gesture", points: [{ x: 1, y: 1 }, { x: 2, y: 2 }] });
  assert.equal(okReq.ok, true);
});

test("key allow-list: single chars and named keys only", async () => {
  const { client } = fakeClient();
  const t = createTransport(client, { permissions: INTERACT });
  assert.equal((await t.request({ kind: "key", key: "a" })).ok, true);
  assert.equal((await t.request({ kind: "key", key: "Enter" })).ok, true);
  const bad = await t.request({ kind: "key", key: "notakey" });
  assert.equal(!bad.ok && bad.error.kind, "invalid-argument");
});

test("fill caps text length", async () => {
  const { client } = fakeClient();
  const t = createTransport(client, { permissions: INTERACT, maxTextLength: 5 });
  const bad = await t.request({ kind: "fill", elementId: "1", text: "toolong" });
  assert.equal(!bad.ok && bad.error.kind, "invalid-argument");
  assert.equal((await t.request({ kind: "fill", elementId: "1", text: "ok" })).ok, true);
});

test("FULL allows setProperty; allow-list gates property names", async () => {
  const { client } = fakeClient();
  const t = createTransport(client, { permissions: FULL });
  assert.equal((await t.request({ kind: "setProperty", elementId: "1", name: "Text", value: "hi" })).ok, true);

  const gated = createTransport(fakeClient().client, {
    permissions: FULL,
    propertyAllowList: ["Text"],
  });
  assert.equal((await gated.request({ kind: "setProperty", elementId: "1", name: "Text", value: "hi" })).ok, true);
  const denied = await gated.request({ kind: "setProperty", elementId: "1", name: "IsVisible", value: "false" });
  assert.equal(!denied.ok && denied.error.kind, "permission-denied");
});

test("subscribe respects read permission", () => {
  const { client, calls } = fakeClient();
  const denied = createTransport(client, { permissions: { read: false, screenshot: false, mutate: false, setProperty: false } });
  denied.subscribe(() => {})();
  assert.equal(calls.includes("openEvents"), false);

  const allowed = createTransport(client, { permissions: READ_ONLY });
  const unsub = allowed.subscribe(() => {});
  assert.equal(calls.includes("openEvents"), true);
  unsub();
  assert.equal(calls.includes("close"), true);
});
