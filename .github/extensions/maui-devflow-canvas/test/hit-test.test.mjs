// hit_test must report "nothing here" for a coordinate that hits nothing.
// Regression guard: hitTestSelect() used to end in an unconditional
// `return this.state.selectedElement`, so a miss handed back the PREVIOUS selection.
// The canvas action only raises not_found on a falsy value, so the miss looked like a
// successful hit and the caller went on to tap/fill an element it never pointed at.

import test from "node:test";
import assert from "node:assert/strict";
import { LiveStore } from "../store.mjs";

const bounds = (x, y, width, height) => ({ x, y, width, height });

function storeWithTree() {
  const store = new LiveStore();
  store.state.info = { ...store.state.info, window: bounds(0, 0, 1000, 600) };
  store._applyRoots([
    {
      id: "page",
      type: "ContentPage",
      isVisible: true,
      windowBounds: bounds(0, 0, 1000, 600),
      children: [
        {
          id: "btn",
          type: "Button",
          automationId: "btn",
          isVisible: true,
          windowBounds: bounds(100, 100, 200, 40),
        },
      ],
    },
  ]);
  return store;
}

// The agent answers an out-of-window point with an empty element list, so the store
// sees "no element" — exactly what the live WinUI agent returns for such a point.
const agentMiss = { hitTest: async () => ({ ok: true, element: null }) };

test("a coordinate that hits nothing returns null, not the previous selection", async () => {
  const store = storeWithTree();
  store.device = agentMiss;

  assert.equal(store.select("btn")?.id, "btn");

  assert.equal(
    await store.hitTestSelect(99999, 99999),
    null,
    "a miss must not be reported as a hit on the previously selected element",
  );
  // The on-screen selection is deliberately left alone: a miss reports nothing,
  // it does not silently deselect what the human was looking at.
  assert.equal(store.state.selectedId, "btn");
});

test("a coordinate over an element still resolves that element", async () => {
  const store = storeWithTree();
  store.device = agentMiss;

  const hit = await store.hitTestSelect(150, 120);
  assert.equal(hit?.id, "btn");
  assert.equal(store.state.selectedId, "btn");
});

test("a miss is still a miss when the agent is unreachable", async () => {
  const store = storeWithTree();
  store.device = {
    hitTest: async () => {
      throw new Error("agent unreachable");
    },
  };
  store.select("btn");

  assert.equal(await store.hitTestSelect(99999, 99999), null);
});

test("negative coordinates report a miss", async () => {
  const store = storeWithTree();
  store.device = agentMiss;
  store.select("btn");

  assert.equal(await store.hitTestSelect(-50, -50), null);
});
