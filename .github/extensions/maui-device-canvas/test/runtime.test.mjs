import test from "node:test";
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdtemp, mkdir, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { resolveCommand } from "../lib/runtime.mjs";

function digest(value) {
  return createHash("sha256").update(value).digest("hex");
}

async function fixture() {
  const home = await mkdtemp(join(tmpdir(), "maui-device-canvas-"));
  const content = Buffer.from("verified-mobile-canvas");
  const id = digest(content);
  const key = "test-x64";
  const directory = join(home, "runtimes", `${key}-${id.slice(0, 12)}`);
  await mkdir(directory, { recursive: true });
  await writeFile(join(directory, "mobile-canvas"), content);
  const manifest = {
    schema: 1,
    version: "0.1.16",
    validatedRevision: "0f0d7806a08d41b3b0b932c05b313686486f75ca",
    runtimes: {
      [key]: {
        id,
        executable: "mobile-canvas",
        files: {
          "mobile-canvas": { size: content.length, sha256: id },
        },
      },
    },
  };
  return { home, key, directory, content, manifest };
}

test("resolves only a checksum-valid pinned installation", async (t) => {
  const f = await fixture();
  t.after(() => rm(f.home, { recursive: true, force: true }));

  const resolved = await resolveCommand({
    homeDirectory: f.home,
    runtimeKey: f.key,
    manifest: f.manifest,
  });

  assert.equal(resolved.command, join(f.directory, "mobile-canvas"));
  assert.equal(resolved.version, "0.1.16");
});

test("rejects a corrupt installed runtime instead of falling back", async (t) => {
  const f = await fixture();
  t.after(() => rm(f.home, { recursive: true, force: true }));
  await writeFile(join(f.directory, "mobile-canvas"), "corrupt");

  await assert.rejects(
    resolveCommand({
      homeDirectory: f.home,
      runtimeKey: f.key,
      manifest: f.manifest,
    }),
    /integrity verification/i,
  );
});

test("missing installation gives the explicit CLI recovery command", async (t) => {
  const f = await fixture();
  const missingHome = await mkdtemp(join(tmpdir(), "maui-device-canvas-missing-"));
  t.after(() => Promise.all([
    rm(f.home, { recursive: true, force: true }),
    rm(missingHome, { recursive: true, force: true }),
  ]));

  await assert.rejects(
    resolveCommand({
      homeDirectory: missingHome,
      runtimeKey: f.key,
      manifest: f.manifest,
    }),
    /devices host install/i,
  );
});
