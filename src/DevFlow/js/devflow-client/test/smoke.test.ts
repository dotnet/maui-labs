// Online smoke test. Skipped unless a live DevFlow
// agent is running and MAUI_DEVFLOW_SMOKE=1 is set. Configure the target with the usual
// MAUI_DEVFLOW_* env vars (e.g. MAUI_DEVFLOW_PLATFORM=windows).
//
//   MAUI_DEVFLOW_SMOKE=1 MAUI_DEVFLOW_PLATFORM=windows npm test

import { test } from "node:test";
import assert from "node:assert/strict";
import { DevFlowClient } from "../src/index.js";

const enabled = process.env.MAUI_DEVFLOW_SMOKE === "1";

test("smoke: discover → connect → tree → screenshot → property roundtrip", { skip: !enabled }, async () => {
  const client = DevFlowClient.fromEnv({ bootstrapBroker: "once" });
  try {
    const agents = await client.listAgents();
    assert.equal(agents.ok, true, agents.ok ? "" : agents.error.message);

    const conn = await client.connect();
    assert.equal(conn.ok, true, conn.ok ? "" : conn.error.message);

    const tree = await client.getTree(3);
    assert.equal(tree.ok, true, tree.ok ? "" : tree.error.message);
    assert.ok(tree.ok && tree.value.length > 0, "expected a non-empty tree");

    const shot = await client.screenshot({ scale: "auto" });
    assert.equal(shot.ok, true, shot.ok ? "" : shot.error.message);
    assert.ok(shot.ok && shot.value.length > 8, "expected PNG bytes");

    // Best-effort property read on the first element that reports one.
    const first = tree.ok ? tree.value[0] : undefined;
    if (first) {
      const prop = await client.getProperty(first.id, "IsVisible");
      assert.equal(prop.ok, true, prop.ok ? "" : prop.error.message);
    }
  } finally {
    client.dispose();
  }
});
