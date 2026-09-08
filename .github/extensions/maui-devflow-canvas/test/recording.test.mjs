import assert from "node:assert/strict";
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { Recorder, RECORDING_MAX_BYTES } from "../recorder.mjs";

test("Recorder persists bounded broker recordings under maui-tests", () => {
  const temp = mkdtempSync(join(tmpdir(), "maui-recording-test-"));
  try {
    const project = join(temp, "App.csproj");
    writeFileSync(project, "<Project />", "utf8");
    const store = { device: { resolvedAgent: () => ({ project }), opts: {} } };
    const recorder = new Recorder();

    const saved = recorder.persist(store, { name: "Checkout flow", markdown: "# Test" });
    assert.equal(saved.ok, true);
    assert.equal(readFileSync(saved.file, "utf8"), "# Test");
    assert.equal(recorder.list(store).tests[0].name, "checkout-flow");

    const oversized = recorder.persist(store, {
      name: "Too large",
      markdown: "x".repeat(RECORDING_MAX_BYTES + 1),
    });
    assert.equal(oversized.ok, false);
    assert.match(oversized.error, /1 MiB/);
    assert.equal(existsSync(join(temp, "maui-tests", "too-large.md")), false);
  } finally {
    rmSync(temp, { recursive: true, force: true });
  }
});
