import assert from "node:assert/strict";
import test from "node:test";
import { renderDisconnected, renderShell } from "../shell.mjs";

function nonceOf(html) {
  return html.match(/<script nonce="([^"]+)">/)?.[1] || "";
}

test("Canvas shell uses an exact frame origin and a separate CSP nonce", () => {
  const bridgeId = "bridge-secret";
  const first = renderShell("http://localhost:19223/inspector/app/?embed=token", "App", bridgeId);
  const second = renderShell("http://localhost:19223/inspector/app/?embed=token", "App", bridgeId);
  const nonce = nonceOf(first);

  assert.match(first, /const frameOrigin = "http:\/\/localhost:19223";/);
  assert.match(first, /if \(e\.origin !== frameOrigin\) return;/);
  assert.doesNotMatch(first, /postMessage\([\s\S]{0,500},\s*['"]\*['"]\)/);
  assert.ok(first.includes(`#devflowBridge=${bridgeId}`));
  assert.notEqual(nonce, bridgeId);
  assert.equal(first.match(/script-src 'nonce-([^']+)'/)?.[1], nonce);
  assert.notEqual(nonceOf(second), nonce);
});

test("Disconnected shell rotates its script nonce", () => {
  assert.notEqual(nonceOf(renderDisconnected("App")), nonceOf(renderDisconnected("App")));
});
