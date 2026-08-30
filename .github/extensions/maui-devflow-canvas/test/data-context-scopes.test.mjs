import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

function readSet(source, name) {
  const match = source.match(
    new RegExp(`const\\s+${name}\\s*=\\s*new Set\\(\\[([\\s\\S]*?)\\]\\);`),
  );
  assert.ok(match, `Could not find ${name}`);
  return [...match[1].matchAll(/["']([^"']+)["']/g)].map((entry) => entry[1]);
}

test("VS Code and Canvas accept redacted Alerts Data snapshots", () => {
  const canvasSource = readFileSync(new URL("../extension.mjs", import.meta.url), "utf8");
  const vscodeSource = readFileSync(
    new URL("../../../../src/DevFlow/js/vscode-inspector/src/context-store.ts", import.meta.url),
    "utf8",
  );

  assert.ok(readSet(canvasSource, "DATA_CONTEXT_SCOPES").includes("alerts"));
  assert.match(vscodeSource, /scope:[^;]*"alerts";/s);
});
