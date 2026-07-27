import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { renderDisconnected, renderShell } from "../shell.mjs";

const fontStack = '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", sans-serif';

test("browser and Canvas hosts use the shared readable typography baseline", () => {
  const css = readFileSync(
    new URL("../../../../src/Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/devflow.css", import.meta.url),
    "utf8",
  );
  const shell = renderShell("http://localhost:19223/inspector/app/", "App", "nonce");
  const disconnected = renderDisconnected("App", "nonce");

  assert.ok(fontStack.length <= 120, "Canvas font stack must pass safeFontFamily");
  assert.match(css, /--df-font-size:\s*13px;/);
  assert.ok(css.includes(`--df-font: ${fontStack};`));
  assert.ok(shell.includes(`font: 13px/1.5 ${fontStack};`));
  assert.ok(disconnected.includes(`font: 13px/1.5 ${fontStack};`));
});
