import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import {
  HOST_BRIDGE_PROTOCOL,
  HOST_CAPABILITIES,
  HOST_OPERATIONS,
  createInspectorHostBridge,
  normalizeHostManifest,
} from "../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/inspector-host-bridge.js";

const devflowSource = readFileSync(
  new URL("../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/devflow.js", import.meta.url),
  "utf8",
);

const vscodeHostSource = readFileSync(
  new URL("../vscode-inspector/src/extension.ts", import.meta.url),
  "utf8",
);

const canvasShellSource = readFileSync(
  new URL("../../../../.github/extensions/maui-devflow-canvas/shell.mjs", import.meta.url),
  "utf8",
);

function advertisedCapabilities(source, declaration) {
  const body = source.match(new RegExp(`${declaration} = (?:Object\\.freeze\\()?\\[([^\\]]+)\\]`))?.[1];
  assert.ok(body, `could not read ${declaration}`);
  return [...body.matchAll(/["']([A-Za-z][A-Za-z0-9]*)["']/g)].map((match) => match[1]);
}

test("host manifest requires a canonical host id and versioned capabilities", () => {
  // There is one identity field. A host that omits `hostId` is unidentified, not silently trusted.
  const unidentified = normalizeHostManifest({
    type: "devflow:host",
    hostLabel: "Mystery host",
    capabilities: ["openSource", "openSource"],
    profile: { surface: "editor" },
  });
  assert.equal(unidentified.protocolVersion, 1);
  assert.equal(unidentified.hostId, "embedded-host");
  assert.deepEqual(unidentified.capabilities, ["openSource"]);
  assert.equal(unidentified.profile.surface, "editor");

  const identified = normalizeHostManifest({
    type: "devflow:host",
    hostId: "vscode",
    hostLabel: "VS Code",
    capabilities: ["openSource"],
    profile: { surface: "editor" },
  });
  assert.equal(identified.hostId, "vscode");

  const versioned = normalizeHostManifest({
    type: "devflow:host",
    protocol: { version: 2, minimumVersion: 1, maximumVersion: 2 },
    hostId: "canvas",
    hostLabel: "Canvas",
    interactionSessionId: "canvas-session",
    capabilities: ["attachData"],
    capabilityDescriptors: [
      {
        name: "attachData",
        version: 2,
        constraints: {
          maxBytes: 20000,
          nested: { unsafe: true },
          "invalid key": "ignored",
          longValue: "x".repeat(300),
        },
      },
      { name: "requestTestProposal", version: 1 },
    ],
    profile: { surface: "side-panel" },
  });
  assert.equal(versioned.protocolVersion, HOST_BRIDGE_PROTOCOL.currentVersion);
  assert.equal(versioned.hostId, "canvas");
  assert.equal(versioned.interactionSessionId, "canvas-session");
  assert.deepEqual(versioned.capabilities, ["attachData", "requestTestProposal"]);
  assert.deepEqual(versioned.capabilityDescriptors[0].constraints, { maxBytes: 20000 });
});

test("host manifest rejects an incompatible protocol range", () => {
  assert.equal(normalizeHostManifest({
    type: "devflow:host",
    protocol: { version: 4, minimumVersion: 4, maximumVersion: 5 },
  }), null);
  assert.equal(normalizeHostManifest({
    type: "devflow:host",
    protocol: { version: 2, minimumVersion: 2, maximumVersion: 1 },
  }), null);
});

test("bridge exposes an explicit browser host and emits negotiated request versions", async () => {
  const listeners = new Map();
  const posted = [];
  const parent = { postMessage: (message) => posted.push(message) };
  const windowLike = {
    location: { hash: "#devflowBridge=bridge_1" },
    parent,
    addEventListener: (name, listener) => listeners.set(name, listener),
    removeEventListener: () => {},
    setTimeout,
    clearTimeout,
  };
  const bridge = createInspectorHostBridge(windowLike);
  assert.equal(bridge.manifest().hostId, "browser");
  assert.equal(bridge.profile().surface, "browser");

  listeners.get("message")({
    source: parent,
    data: {
      type: "devflow:host",
      bridgeId: "bridge_1",
      protocol: { version: 2, minimumVersion: 1, maximumVersion: 2 },
      hostId: "vscode",
      capabilities: ["pickTrace"],
      profile: { surface: "editor" },
    },
  });
  const pending = bridge.request("pickTrace", { kind: "mauitrace" }, 1000);
  assert.equal(posted[0].protocolVersion, 2);
  assert.equal(posted[0].type, "devflow:pickTrace");

  listeners.get("message")({
    source: parent,
    data: {
      type: "devflow:hostResult",
      bridgeId: "bridge_1",
      requestId: posted[0].requestId,
      ok: true,
    },
  });
  assert.equal((await pending).ok, true);
  bridge.dispose();
});

test("authenticated recording changes are forwarded to Inspector observers", () => {
  const listeners = new Map();
  const parent = { postMessage() {} };
  const windowLike = {
    location: { hash: "#devflowBridge=bridge_1" },
    parent,
    addEventListener: (name, listener) => listeners.set(name, listener),
    removeEventListener: () => {},
    setTimeout,
    clearTimeout,
  };
  const bridge = createInspectorHostBridge(windowLike);
  const observed = [];
  bridge.onHostMessage((event) => observed.push(event));

  listeners.get("message")({
    source: {},
    data: { type: "devflow:recordingChanged", bridgeId: "bridge_1" },
  });
  listeners.get("message")({
    source: parent,
    data: { type: "devflow:recordingChanged", bridgeId: "wrong" },
  });
  assert.deepEqual(observed, []);

  listeners.get("message")({
    source: parent,
    data: { type: "devflow:recordingChanged", bridgeId: "bridge_1" },
  });
  assert.deepEqual(observed, [{ type: "recording" }]);
  bridge.dispose();
});

test("embedded hosts adopt their shared interaction session before claiming a random lease", () => {
  assert.match(devflowSource, /interactionSessionId/);
  assert.match(devflowSource, /hostInteractionAdopted/);
  assert.match(devflowSource, /if \(hostBridge\.isEmbedded\(\)\)/);
  assert.match(devflowSource, /if \(!hostInteractionAdopted\) control\('claim'\)/);
  assert.doesNotMatch(devflowSource, /control\('claim'\);\s*\/\/ optimistically claim/);
});

// ── Contract tests: the whole point of a single registry ──────────────────────────────────────
// These are the tests that would have caught `attachTestContext` shipping as a capability no host
// implemented, and any future silent drift between a host and the page.

test("every advertised host capability exists in the shared registry", () => {
  for (const [host, declaration, source] of [
    ["VS Code", "const VSCODE_HOST_CAPABILITIES", vscodeHostSource],
    ["Canvas", "const CANVAS_HOST_CAPABILITIES", canvasShellSource],
  ]) {
    for (const capability of advertisedCapabilities(source, declaration)) {
      assert.ok(
        HOST_CAPABILITIES.includes(capability),
        `${host} advertises '${capability}', which is not in the shared registry`,
      );
    }
  }
});

test("every registry capability is implemented by at least one host", () => {
  const advertised = new Set([
    ...advertisedCapabilities(vscodeHostSource, "const VSCODE_HOST_CAPABILITIES"),
    ...advertisedCapabilities(canvasShellSource, "const CANVAS_HOST_CAPABILITIES"),
  ]);
  for (const capability of HOST_CAPABILITIES) {
    assert.ok(
      advertised.has(capability),
      `'${capability}' is in the registry but no host implements it — remove it or implement it`,
    );
  }
});

test("Canvas advertises exactly the operations its shell relays", () => {
  const relayed = new Set(
    [...canvasShellSource.matchAll(/d\.type === '(devflow:[A-Za-z]+)'/g)].map((m) => m[1]),
  );
  for (const capability of advertisedCapabilities(canvasShellSource, "const CANVAS_HOST_CAPABILITIES")) {
    assert.ok(
      relayed.has(HOST_OPERATIONS[capability].message),
      `Canvas advertises '${capability}' but its shell never relays ${HOST_OPERATIONS[capability].message}`,
    );
  }
});

test("one-way operations can never be awaited as if they completed", async () => {
  const bridge = createInspectorHostBridge({
    location: { hash: "" },
    addEventListener() {}, removeEventListener() {},
    setTimeout() {}, clearTimeout() {},
  });

  test("native approval is unavailable to a standalone browser", async () => {
    const browser = createInspectorHostBridge({
      location: { hash: "" },
      addEventListener() {}, removeEventListener() {},
      setTimeout() {}, clearTimeout() {},
    });
    assert.equal(browser.has("nativeApproval"), false);
    assert.equal(browser.resolve("nativeApproval").state, "unavailable");
    const result = await browser.request("nativeApproval", {});
    assert.equal(result.ok, false);
    assert.equal(result.code, "capability-missing");
  });
  for (const [name, operation] of Object.entries(HOST_OPERATIONS)) {
    if (operation.mode !== "notify") continue;
    const result = await bridge.request(name, {});
    assert.equal(result.ok, false, `${name} is one-way and must not report success`);
  }
});

test("a standalone browser can download but an embedded surface cannot", () => {
  const browser = createInspectorHostBridge({
    location: { hash: "" },
    addEventListener() {}, removeEventListener() {},
    setTimeout() {}, clearTimeout() {},
  });
  assert.equal(browser.canDownload(), true);
  assert.equal(browser.isEmbedded(), false);
  // A browser tab resolves download-backed operations to its own fallback.
  assert.equal(browser.resolve("saveTestBundle").state, "alternative");

  const parent = { postMessage() {} };
  const embedded = createInspectorHostBridge({
    parent,
    location: { hash: "#devflowBridge=abc123" },
    addEventListener() {}, removeEventListener() {},
    setTimeout() { return 0; }, clearTimeout() {},
  });
  assert.equal(embedded.canDownload(), false);
  assert.equal(embedded.isEmbedded(), true);
  // Until the host answers, nothing resolves to a fallback the sandbox cannot run.
  assert.equal(embedded.resolve("saveTestBundle").state, "pending");
});

test("an embedded surface never promises a download it cannot deliver", () => {
  const parent = { postMessage() {} };
  const listeners = [];
  const embedded = createInspectorHostBridge({
    parent,
    location: { hash: "#devflowBridge=abc123" },
    addEventListener(_type, fn) { listeners.push(fn); },
    removeEventListener() {},
    setTimeout() { return 0; }, clearTimeout() {},
  });
  // A host that cannot save test bundles.
  listeners[0]({
    source: parent,
    data: {
      type: "devflow:host",
      bridgeId: "abc123",
      hostId: "canvas",
      hostLabel: "Canvas",
      capabilities: ["selection"],
    },
  });
  const resolution = embedded.resolve("saveTestBundle");
  assert.equal(resolution.state, "unavailable");
  assert.equal(resolution.reasonCode, "downloads-blocked");
  assert.match(resolution.message, /Downloads are blocked in this embedded surface/);
});

test("a timed-out request is indeterminate so callers never double-apply", async () => {
  const parent = { postMessage() {} };
  const listeners = [];
  let fire = null;
  const embedded = createInspectorHostBridge({
    parent,
    location: { hash: "#devflowBridge=abc123" },
    addEventListener(_type, fn) { listeners.push(fn); },
    removeEventListener() {},
    setTimeout(fn) { fire = fn; return 1; },
    clearTimeout() {},
  });
  listeners[0]({
    source: parent,
    data: {
      type: "devflow:host",
      bridgeId: "abc123",
      hostId: "vscode",
      hostLabel: "VS Code",
      capabilities: ["saveTestBundle"],
    },
  });
  const pending = embedded.request("saveTestBundle", {});
  fire();
  const result = await pending;
  assert.equal(result.ok, false);
  assert.equal(result.state, "indeterminate");
  assert.equal(result.reasonCode, "host-timeout");
});
