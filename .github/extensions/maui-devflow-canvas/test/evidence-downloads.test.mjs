import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";
import { renderShell } from "../shell.mjs";

test("Canvas enables evidence downloads before loading the frame, without enabling navigation or popups", () => {
  const html = renderShell("http://localhost:19223/inspector/app/?embed=fixture", "App", "bridge-fixture");
  const permissions = new Set(html.match(/sandbox="([^"]+)"/)[1].split(" "));
  const configured = new Error("Sandbox configured");
  const frame = {
    sandbox: { add(value) { permissions.add(value); throw configured; } },
  };
  const script = html.match(/<script nonce="[^"]+">([\s\S]+?)<\/script>/)[1];
  assert.throws(() => vm.runInNewContext(script, {
    document: { getElementById: () => frame },
  }), error => error === configured);
  assert.deepEqual([...permissions].sort(), [
    "allow-downloads", "allow-forms", "allow-same-origin", "allow-scripts",
  ]);
});

test("VS Code applies the same bounded download permission", () => {
  const source = readFileSync(new URL("../../../../src/DevFlow/js/vscode-inspector/src/extension.ts", import.meta.url), "utf8");
  assert.match(source, /frame\.sandbox\.add\('allow-downloads'\)/);
  assert.match(source, /sandbox="allow-scripts allow-forms allow-same-origin"/);
  assert.doesNotMatch(source, /allow-popups|allow-top-navigation/);
});
