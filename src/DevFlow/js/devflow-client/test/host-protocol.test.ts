import assert from "node:assert/strict";
import test from "node:test";
import {
  INSPECTOR_HOST_PROTOCOL,
  INSPECTOR_HOST_IDS,
  createInspectorHostManifest,
} from "../src/host-protocol.js";

test("host manifests share one versioned capability contract", () => {
  const manifest = createInspectorHostManifest({
    hostId: "canvas",
    hostLabel: "Canvas",
    interactionSessionId: "session-1",
    capabilities: ["selection", "selection", "attachData"],
  });

  assert.equal(manifest.protocol.version, INSPECTOR_HOST_PROTOCOL.currentVersion);
  assert.equal(manifest.protocol.minimumVersion, INSPECTOR_HOST_PROTOCOL.minimumVersion);
  assert.equal(manifest.interactionSessionId, "session-1");
  assert.deepEqual(manifest.capabilities, ["selection", "attachData"]);
  assert.deepEqual(manifest.capabilityDescriptors, [
    { name: "selection", version: 1 },
    { name: "attachData", version: 1 },
  ]);
});

test("host identity is a closed set with no alias forms", () => {
  // The broker matches these values exactly for its source-apply policy, so an alias like
  // "copilot-canvas-ui" must never reappear.
  assert.deepEqual([...INSPECTOR_HOST_IDS], ["browser", "vscode", "canvas"]);
  const manifest = createInspectorHostManifest({
    hostId: "canvas",
    hostLabel: "Canvas",
    interactionSessionId: "session-1",
    capabilities: [],
  });
  assert.equal(manifest.hostId, "canvas");
  assert.ok(!("hostKind" in manifest), "the manifest must carry exactly one identity field");
});
