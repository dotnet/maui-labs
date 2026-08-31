import assert from "node:assert/strict";
import test from "node:test";
import {
  isInspectorQueryResult,
  isInspectorSnapshot,
} from "../src/inspect-contracts.js";

test("Inspector snapshot guards enforce the canonical projection contract", () => {
  const snapshot = {
    ok: true,
    protocolVersion: 1,
    projection: "activeVisual",
    snapshotId: "snapshot-1",
    revision: "snapshot-1",
    capturedAt: new Date(0).toISOString(),
    target: { agentId: "agent-1" },
    viewport: { width: 400, height: 800, rootOffsetX: 0, rootOffsetY: 0 },
    screenshotUrl: "screenshot.png?frame=snapshot-1",
    roots: [],
  };

  assert.equal(isInspectorSnapshot(snapshot), true);
  assert.equal(isInspectorSnapshot({ ...snapshot, projection: "raw" }), false);
  assert.equal(isInspectorSnapshot({ ...snapshot, roots: null }), false);
});

test("Inspector query guards require revisioned activeVisual results", () => {
  const result = {
    ok: true,
    protocolVersion: 1,
    projection: "activeVisual",
    snapshotId: "snapshot-1",
    revision: "snapshot-1",
    elements: [],
  };

  assert.equal(isInspectorQueryResult(result), true);
  assert.equal(isInspectorQueryResult({ ...result, revision: null }), false);
});
