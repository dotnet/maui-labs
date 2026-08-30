// A mutation must survive another DevFlow host briefly holding the global mutation lease.
//
// Running the Canvas, the browser Inspector, VS Code, the CLI and MCP against one app at the
// same time is a supported workflow, and the lease is a single global lock with a short TTL.
// The shared client reports `lease-held` with retriable:true but implemented no retry, so a
// neighbouring host holding the lock for a moment surfaced as a hard failure. Observed live
// four times during QA, each of which succeeded on an immediate manual retry.

import test from "node:test";
import assert from "node:assert/strict";
import { DevflowDevice } from "../devflow.mjs";

const leaseHeld = {
  ok: false,
  error: { kind: "lease-held", message: "Another DevFlow session is driving this app (Browser Inspector)." },
};

// Fails with lease-held `failures` times, then succeeds. Records how often it was called.
function flakyClient(failures) {
  const state = { calls: 0 };
  const attempt = async () => {
    state.calls++;
    return state.calls <= failures ? leaseHeld : { ok: true, value: undefined };
  };
  return {
    state,
    client: {
      tap: attempt,
      fill: attempt,
      setProperty: attempt,
      scroll: attempt,
      navigate: attempt,
      back: attempt,
      resize: attempt,
      setTheme: attempt,
    },
  };
}

function deviceWith(client) {
  const device = new DevflowDevice({});
  device._client = client;
  device.query = async () => ({ ok: true, elements: [{ id: "AddButton" }] });
  return device;
}

test("a tap blocked by another host's lease is retried and succeeds", async () => {
  const { state, client } = flakyClient(2);
  const device = deviceWith(client);

  const r = await device.tap({ id: "AddButton" });

  assert.equal(r.ok, true);
  assert.equal(state.calls, 3, "should retry until the neighbouring host releases the lease");
});

test("every mutation verb waits out lease contention", async () => {
  const verbs = [
    ["tap", (d) => d.tap({ id: "x" })],
    ["fill", (d) => d.fill({ id: "x" }, "v")],
    ["setProperty", (d) => d.setProperty("x", "Text", "v")],
    ["scroll", (d) => d.scroll({ element: "x", dy: 10 })],
    ["navigate", (d) => d.navigate("//home")],
    ["back", (d) => d.back()],
    ["resize", (d) => d.resize(800, 600)],
    ["themeSet", (d) => d.themeSet("dark")],
  ];

  for (const [name, run] of verbs) {
    const { client } = flakyClient(1);
    const r = await run(deviceWith(client));
    assert.equal(r.ok, true, `${name} should survive one lease-held response`);
  }
});

test("a non-lease error is returned immediately without retrying", async () => {
  const state = { calls: 0 };
  const device = deviceWith({
    tap: async () => {
      state.calls++;
      return { ok: false, error: { kind: "not-found", message: "Element not found" } };
    },
  });

  const r = await device.tap({ id: "Ghost" });

  assert.equal(r.ok, false);
  assert.equal(r.error, "Element not found");
  assert.equal(state.calls, 1, "only lease contention is worth waiting for");
});

test("persistent lease contention still gives up and reports the real holder", async () => {
  const state = { calls: 0 };
  const device = deviceWith({
    tap: async () => {
      state.calls++;
      return leaseHeld;
    },
  });

  const started = Date.now();
  const r = await device.tap({ id: "AddButton" });
  const elapsed = Date.now() - started;

  assert.equal(r.ok, false);
  assert.match(r.error, /Browser Inspector/, "the caller must still learn who holds the lease");
  assert.ok(state.calls > 1, "should have attempted more than once");
  assert.ok(elapsed < 15000, `must stay bounded, took ${elapsed}ms`);
});
