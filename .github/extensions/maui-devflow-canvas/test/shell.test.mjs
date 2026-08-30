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

test("Canvas shells use MAUI DevFlow Inspector product titles", () => {
  assert.match(renderShell("http://localhost:19223/inspector/app/", "Demo", "bridge"), /<title>MAUI DevFlow Inspector · Demo<\/title>/);
  const disconnected = renderDisconnected("Demo", "app");
  assert.match(disconnected, /<title>MAUI DevFlow Inspector · Demo<\/title>/);
  assert.match(disconnected, /<p class="eyebrow">MAUI DevFlow Inspector · Demo<\/p>/);
  assert.match(renderDisconnected(null, "broker"), /<title>MAUI DevFlow Inspector<\/title>/);
});

test("Disconnected shell distinguishes broker and app waits with explicit retry polling", () => {
  const broker = renderDisconnected(null, "broker");
  const app = renderDisconnected(null, "app");
  const nonce = nonceOf(broker);

  assert.match(broker, /Waiting for the DevFlow broker/);
  assert.match(app, /Waiting for a running MAUI app/);
  assert.match(broker, />Retry<\/button>/);
  assert.match(broker, /fetch\('\/inspector-ready'/);
  assert.match(broker, /setInterval\(heal, 2500\)/);
  assert.match(broker, /j && j\.state === 'app'/);
  assert.match(broker, /prefers-reduced-motion: reduce/);
  assert.equal(broker.match(/script-src 'nonce-([^']+)'/)?.[1], nonce);
  assert.doesNotMatch(broker, /spinner|@keyframes/);
});

test("Canvas shell relays test saves and direct agent requests without adding replay semantics", () => {
  const html = renderShell("http://localhost:19223/inspector/app/?embed=token", "App", "bridge-secret");

  assert.match(html, /"protocol":\{"version":2,"minimumVersion":1,"maximumVersion":2\}/);
  assert.match(html, /"hostId":"canvas"/);
  assert.match(html, /"interactionSessionId":"bridge-secret"/);
  assert.match(html, /"capabilityDescriptors":\[/);
  assert.match(html, /'saveTestBundle'/);
  assert.match(html, /action: 'saveTestBundle', bundle: d\.bundle/);
  assert.match(html, /'requestTestProposal'/);
  assert.match(html, /devflow:requestTestProposal/);
  assert.match(html, /action: 'requestTestProposal', prompt: d\.prompt/);
  assert.match(html, /devflow:hostResult/);
  assert.doesNotMatch(html, /devflow:loadTestBundle/);
  assert.doesNotMatch(html, /const capabilities = \[[^\]]*'attachTestContext'/);
  assert.doesNotMatch(html, /devflow:attachTestContext/);
});

test("Canvas is not a trusted approval host and carries no approval or source authority", () => {
  const html = renderShell("http://localhost:19223/inspector/app/?embed=token", "App", "bridge-secret");
  // Canvas may inspect, interact and record. It must never claim an approval capability: its
  // confirm() runs in a webview the page can reach, so it is not evidence of local human consent.
  assert.doesNotMatch(html, /nativeApproval/);
  assert.doesNotMatch(html, /layoutPolicyMutation/);
  assert.doesNotMatch(html, /applySourceProposal|applyCSharpSourceProposal|getCSharpSourceSelection/);
  assert.doesNotMatch(html, /window\.confirm/);
  assert.doesNotMatch(html, /\/native-approval/);
  assert.doesNotMatch(html, /X-DevFlow-Host-Approval-Token/);
  assert.doesNotMatch(html, /confirmationCapability/);
});

test("Canvas shell notifies the shared Inspector when broker recording state changes", () => {
  const html = renderShell("http://localhost:19223/inspector/app/?embed=token", "App", "bridge-secret");

  assert.match(html, /new EventSource\('\/recording-events'\)/);
  assert.match(html, /type: 'devflow:recordingChanged'/);
  assert.match(html, /bridgeId: bridgeId/);
  assert.match(html, /recordingEvents\.close\(\)/);
});
