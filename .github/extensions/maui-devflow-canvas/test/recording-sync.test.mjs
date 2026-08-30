import assert from "node:assert/strict";
import test from "node:test";
import { LiveStore } from "../store.mjs";

test("successful mutations discover broker recordings started outside the store", async () => {
  const store = new LiveStore();
  let calls = 0;
  let emitted = null;
  store.device = {
    async recordingStatus() {
      calls++;
      return {
        ok: true,
        recording: true,
        recordingId: "external-recording",
        name: "external",
        steps: 2,
      };
    },
  };
  store.subscribe((snapshot) => { emitted = snapshot; });

  await store._recordAction({ action: "tap", ok: true });

  assert.equal(calls, 1);
  assert.equal(emitted.recording, true);
  assert.equal(emitted.recorder.count, 2);
});

test("failed mutations do not query or publish recording state", async () => {
  const store = new LiveStore();
  let calls = 0;
  let emissions = 0;
  store.device = {
    async recordingStatus() {
      calls++;
      return { ok: true, recording: false, steps: 0 };
    },
  };
  store.subscribe(() => { emissions++; });

  await store._recordAction({ action: "tap", ok: false });

  assert.equal(calls, 0);
  assert.equal(emissions, 0);
});
