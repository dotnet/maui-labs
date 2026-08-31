import assert from "node:assert/strict";
import test from "node:test";
import { LiveStore } from "../store.mjs";

test("LiveStore uses the canonical activeVisual snapshot without re-filtering it", async () => {
  const roots = [
    {
      id: "root",
      type: "ContentPage",
      isVisible: true,
      windowBounds: { x: 0, y: 0, width: 400, height: 800 },
      children: [
        {
          id: "retained-zero-size",
          type: "Layout",
          isVisible: false,
          windowBounds: { x: 0, y: 0, width: 0, height: 0 },
        },
      ],
    },
  ];
  const store = new LiveStore({
    inspectSnapshot: async () => ({
      ok: true,
      projection: "activeVisual",
      snapshotId: "snapshot-1",
      revision: "snapshot-1",
      viewport: { width: 400, height: 800 },
      roots,
    }),
  });
  store.device._ensureConnection = async () => ({ transport: "http", port: 1234 });
  store.device.getRoots = async () => ({ ok: false, error: "direct tree should not be used", roots: [] });

  const snapshot = await store.refresh({ shot: false, info: false });

  assert.equal(snapshot.projection, "activeVisual");
  assert.equal(snapshot.revision, "snapshot-1");
  assert.equal(snapshot.roots[0].children[0].id, "retained-zero-size");
  assert.equal(store.getElement("retained-zero-size")?.id, "retained-zero-size");
});

test("LiveStore reports its explicit direct-agent projection fallback", async () => {
  const store = new LiveStore({
    inspectSnapshot: async () => ({
      ok: false,
      error: { code: "broker-unavailable", message: "broker unavailable" },
    }),
  });
  store.device._ensureConnection = async () => ({ transport: "http", port: 1234 });
  store.device.getRoots = async () => ({
    ok: true,
    roots: [{
      id: "fallback",
      type: "ContentPage",
      isVisible: true,
      windowBounds: { x: 0, y: 0, width: 400, height: 800 },
    }],
  });

  const snapshot = await store.refresh({ shot: false, info: false });

  assert.equal(snapshot.projection, "agentFallback");
  assert.equal(snapshot.projectionWarning, "broker unavailable");
  assert.equal(snapshot.roots[0].id, "fallback");
});

test("LiveStore routes queries through the canonical Inspector query provider", async () => {
  const store = new LiveStore({
    inspectQuery: async (query) => ({
      ok: true,
      elements: [{ id: "canonical", type: query.type }],
    }),
  });
  store.device.query = async () => ({ ok: false, error: "direct query should not be used", elements: [] });

  const result = await store.query({ type: "Button" });

  assert.equal(result.ok, true);
  assert.deepEqual(result.elements, [{ id: "canonical", type: "Button" }]);
});

test("LiveStore routes mutations through the Inspector with no duplicate-side-effect retry", async () => {
  const requests = [];
  let directCalls = 0;
  const store = new LiveStore({
    inspectAction: async (path, body) => {
      requests.push({ path, body });
      return { ok: true };
    },
  });
  store.device.setProperty = async () => {
    directCalls++;
    return { ok: true };
  };
  store.refresh = async () => store.snapshot();

  const result = await store.setProperty("button", "Text", "Saved");

  assert.equal(result.ok, true);
  assert.equal(directCalls, 0);
  assert.deepEqual(requests[0], {
    path: "/api/setProperty",
    body: { elementId: "button", name: "Text", value: "Saved" },
  });
});

test("LiveStore falls back before dispatch but never retries an attempted mutation", async () => {
  let directCalls = 0;
  const unavailable = new LiveStore({
    inspectAction: async () => ({
      ok: false,
      attempted: false,
      error: { code: "broker-unavailable", message: "broker unavailable" },
    }),
  });
  unavailable.device.back = async () => {
    directCalls++;
    return { ok: true };
  };
  unavailable.refresh = async () => unavailable.snapshot();
  assert.equal((await unavailable.back()).ok, true);
  assert.equal(directCalls, 1);

  const uncertain = new LiveStore({
    inspectAction: async () => ({
      ok: false,
      attempted: true,
      error: { code: "timeout", message: "completion unknown" },
    }),
  });
  uncertain.device.back = async () => {
    directCalls++;
    return { ok: true };
  };
  uncertain.refresh = async () => uncertain.snapshot();
  const result = await uncertain.back();
  assert.equal(result.ok, false);
  assert.equal(result.code, "timeout");
  assert.equal(directCalls, 1);
});
