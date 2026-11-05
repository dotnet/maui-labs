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

test("Disconnected shell distinguishes broker and app waits with explicit retry", () => {
  const broker = renderDisconnected(null, "broker");
  const app = renderDisconnected("MauiTodo", "app");
  const multiple = renderDisconnected(null, "multiple");
  const target = renderDisconnected("MauiTodo", "target");

  assert.match(broker, /MAUI DevFlow Inspector/);
  assert.match(broker, /Waiting for the DevFlow broker/);
  assert.match(broker, /Start or restart MAUI DevFlow/);
  assert.match(app, /MAUI DevFlow Inspector · MauiTodo/);
  assert.match(app, /Waiting for a running MAUI app/);
  assert.match(app, /Launch your app with the DevFlow agent/);
  assert.match(multiple, /Choose a running MAUI app/);
  assert.match(target, /Waiting for the selected MAUI app/);
  assert.match(app, /id="df-retry"/);
  assert.match(app, /Checking the connection/);
  assert.match(app, /if \(explicit\) pollAgain = true/);
  assert.match(app, /void heal\(true\)/);
  assert.doesNotMatch(app, /spinner|@keyframes/);
});

test("Connected Canvas shell uses the MAUI DevFlow Inspector product title", () => {
  const html = renderShell(
    "http://localhost:19223/inspector/app/?embed=token",
    "MauiTodo",
    "bridge-secret",
    "agent-1",
    "generation-1",
  );

  assert.match(html, /<title>MAUI DevFlow Inspector · MauiTodo<\/title>/);
  assert.match(html, /fetch\('\/inspector-ready'/);
  assert.match(html, /readiness\.agentId === targetAgentId/);
  assert.match(html, /readiness\.generation === targetGeneration/);
  assert.match(html, /action: 'syncTarget'/);
  assert.match(html, /targetMisses >= 2/);
  assert.match(html, /Waiting for the selected MAUI app/);
  assert.match(html, /showSyncError[\s\S]*startLiveness\(\)/);
});
