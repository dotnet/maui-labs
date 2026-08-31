import assert from "node:assert/strict";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
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
    const duplicate = recorder.persist(store, { name: "Checkout flow", markdown: "# Replacement" });
    assert.equal(duplicate.ok, false);
    assert.equal(readFileSync(saved.file, "utf8"), "# Test");
    assert.equal(
      readdirSync(join(temp, "maui-tests")).some((name) => name.endsWith(".tmp")),
      false);

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

test("Recorder resolves only top-level tests under maui-tests", (t) => {
  const temp = mkdtempSync(join(tmpdir(), "maui-recording-path-test-"));
  try {
    const project = join(temp, "App.csproj");
    const root = join(temp, "maui-tests");
    const outside = join(temp, "outside.md");
    writeFileSync(project, "<Project />", "utf8");
    mkdirSync(root);
    writeFileSync(join(root, "inside.md"), "# inside", "utf8");
    writeFileSync(outside, "# outside", "utf8");

    const store = { device: { resolvedAgent: () => ({ project }), opts: {} } };
    const recorder = new Recorder();
    assert.deepEqual(recorder.resolveTestName(store, { name: "inside" }), { ok: true, name: "inside.md" });
    assert.deepEqual(recorder.resolveTestName(store, { file: join(root, "inside.md") }), { ok: true, name: "inside.md" });
    assert.equal(recorder.resolveTestName(store, { file: outside }).ok, false);
    assert.equal(recorder.load(store, { name: "inside" }).markdown, "# inside");
    assert.equal(recorder.load(store, { name: "missing" }).ok, false);

    const linked = join(root, "linked.md");
    try {
      symlinkSync(outside, linked, process.platform === "win32" ? "file" : "file");
    } catch (e) {
      t.diagnostic(`link check skipped: ${e.code || e.message}`);
      return;
    }
    assert.equal(recorder.resolveTestName(store, { file: linked }).ok, false);
    assert.equal(recorder.load(store, { file: linked }).ok, false);
  } finally {
    rmSync(temp, { recursive: true, force: true });
  }
});
