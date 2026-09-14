import assert from "node:assert/strict";
import { mkdirSync, mkdtempSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { replayTest } from "../replay.mjs";

const markdown = (name) => [
  `# ${name}`,
  "",
  "```json maui-test",
  JSON.stringify({ name, steps: [] }),
  "```",
  "",
].join("\n");

const store = { refresh: async () => ({}) };

test("replayTest accepts files inside the output root", async () => {
  const temp = mkdtempSync(join(tmpdir(), "maui-replay-test-"));
  try {
    const root = join(temp, "maui-tests");
    mkdirSync(root);
    const file = join(root, "inside.md");
    writeFileSync(file, markdown("inside"), "utf8");

    const report = await replayTest(store, { file, root });
    assert.equal(report.ok, true);
    assert.equal(report.file, file);
  } finally {
    rmSync(temp, { recursive: true, force: true });
  }
});

test("replayTest rejects traversal and sibling-prefix paths", async () => {
  const temp = mkdtempSync(join(tmpdir(), "maui-replay-test-"));
  try {
    const root = join(temp, "maui-tests");
    const sibling = join(temp, "maui-tests-elsewhere");
    mkdirSync(root);
    mkdirSync(sibling);
    const outside = join(temp, "outside.md");
    const siblingFile = join(sibling, "outside.md");
    writeFileSync(outside, markdown("outside"), "utf8");
    writeFileSync(siblingFile, markdown("sibling"), "utf8");

    for (const file of [outside, siblingFile]) {
      const report = await replayTest(store, { file, root });
      assert.equal(report.ok, false);
      assert.match(report.error, /inside the resolved maui-tests directory/);
    }
  } finally {
    rmSync(temp, { recursive: true, force: true });
  }
});

test("replayTest rejects canonical paths that escape through a link", async (t) => {
  const temp = mkdtempSync(join(tmpdir(), "maui-replay-test-"));
  try {
    const root = join(temp, "maui-tests");
    const outside = join(temp, "outside");
    const linked = join(root, "linked");
    mkdirSync(root);
    mkdirSync(outside);
    writeFileSync(join(outside, "escape.md"), markdown("escape"), "utf8");
    try {
      symlinkSync(outside, linked, process.platform === "win32" ? "junction" : "dir");
    } catch (e) {
      t.skip(`links are unavailable in this environment: ${e.code || e.message}`);
      return;
    }

    const report = await replayTest(store, { file: join(linked, "escape.md"), root });
    assert.equal(report.ok, false);
    assert.match(report.error, /inside the resolved maui-tests directory/);
  } finally {
    rmSync(temp, { recursive: true, force: true });
  }
});
