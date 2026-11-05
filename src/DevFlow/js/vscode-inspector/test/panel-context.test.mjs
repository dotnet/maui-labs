import assert from "node:assert/strict";
import test from "node:test";
import { PanelContextStore } from "../dist-test/panel-context.js";

function fixture() {
  const contexts = new PanelContextStore();
  const a = {};
  const b = {};
  contexts.add(a);
  contexts.add(b);
  contexts.activate(a);
  contexts.setSelection(a, { id: "a-element" });
  contexts.setSnapshot(a, { appName: "a" });
  return { contexts, a, b };
}

test("activating an empty Inspector clears another panel's Copilot context", () => {
  const { contexts, b } = fixture();
  contexts.activate(b);
  assert.equal(contexts.selection, null);
  assert.equal(contexts.snapshot, null);
  contexts.setSelection(b, null);
  assert.equal(contexts.selection, null);
});

test("active panel deselection clears context without clearing another panel's stored selection", () => {
  const { contexts, a, b } = fixture();
  contexts.activate(b);
  contexts.setSelection(b, { id: "b-element" });
  contexts.setSelection(b, null);
  assert.equal(contexts.selection, null);
  contexts.activate(a);
  assert.deepEqual(contexts.selection, { id: "a-element" });
  assert.deepEqual(contexts.snapshot, { appName: "a" });
});

test("background selection, deselection, disconnect and disposal cannot steal active context", () => {
  const { contexts, a, b } = fixture();
  contexts.activate(b);
  contexts.setSelection(b, { id: "b-element" });
  contexts.setSnapshot(b, { appName: "b" });
  contexts.setSelection(a, { id: "background-element" });
  contexts.setSnapshot(a, { appName: "background" });
  contexts.setSelection(a, null);
  contexts.clear(a);
  contexts.remove(a);
  assert.deepEqual(contexts.selection, { id: "b-element" });
  assert.deepEqual(contexts.snapshot, { appName: "b" });
});

test("disconnect clears only that panel while retaining its activation ownership", () => {
  const { contexts, a, b } = fixture();
  contexts.clear(a);
  contexts.setSelection(b, { id: "b-element" });
  assert.equal(contexts.selection, null);
  contexts.setSelection(a, { id: "a-reconnected" });
  assert.deepEqual(contexts.selection, { id: "a-reconnected" });
});

test("late events and activation from disposed panels cannot restore stale context", () => {
  const { contexts, a } = fixture();
  contexts.remove(a);
  contexts.setSelection(a, { id: "stale-element" });
  contexts.setSnapshot(a, { appName: "stale" });
  contexts.activate(a);
  assert.equal(contexts.selection, null);
  assert.equal(contexts.snapshot, null);
});
