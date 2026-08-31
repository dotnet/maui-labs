import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { withJson } from "../lib/argv.mjs";

test("standalone canvas does not depend on the DevFlow broker or app client", async () => {
  const source = await readFile(new URL("../extension.mjs", import.meta.url), "utf8");
  const runtime = await readFile(new URL("../lib/runtime.mjs", import.meta.url), "utf8");

  assert.match(source, /id:\s*"maui-mobile-device"/);
  assert.doesNotMatch(source, /@maui-devflow\/client|broker\.json|maui-live-canvas/);
  assert.doesNotMatch(runtime, /process\.env|PATH|download|https:/);
  assert.match(runtime, /devices host install/);
});

// --json appended after the end-of-options marker stops being a flag and becomes a positional
// argument, so the command emits human-readable output and JSON.parse fails on it. That reads as a
// broken companion rather than a broken argv, which is exactly the kind of bug nobody finds twice.
test("--json is placed before the end-of-options marker", () => {
  assert.deepEqual(
    withJson(["devices", "boot", "--session", "s", "--instance", "i", "--", "-x"]),
    ["devices", "boot", "--session", "s", "--instance", "i", "--json", "--", "-x"],
  );
});

test("--json is appended when there is no marker", () => {
  assert.deepEqual(withJson(["devices", "list"]), ["devices", "list", "--json"]);
});

// A device ID of exactly "--" must not be mistaken for the marker: the first "--" is always the one
// this extension wrote.
test("a model-supplied value equal to the marker does not move --json", () => {
  assert.deepEqual(
    withJson(["devices", "get", "--", "--"]),
    ["devices", "get", "--json", "--", "--"],
  );
});

test("the canvas open path routes its device ID through the marker", async () => {
  const source = await readFile(new URL("../extension.mjs", import.meta.url), "utf8");

  assert.match(source, /"devices", "select",\s*\n\s*\.\.\.contextArgs\(ctx\),\s*\n\s*"--", String\(ctx\.input\.deviceId\),/);
});
