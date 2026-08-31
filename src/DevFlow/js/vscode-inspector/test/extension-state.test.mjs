import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

import {
  agentRuntimeIdentity,
  selectRefreshedAgent,
} from "../dist-test/agent-identity.js";
import { requiresBridgeRequestId } from "../dist-test/bridge-contract.js";
import { BoundedReferenceStore } from "../dist-test/context-store.js";
import { supportsDiagnosticExplanation } from "../dist-test/diagnostic-actions.js";
import {
  isNativeApprovalRequest,
  performNativeApproval,
} from "../dist-test/native-approval.js";
import {
  inspectorTitle,
  reconnectDiscoveryAction,
  renderReconnectHost,
} from "../dist-test/host-shells.js";

const packageJson = JSON.parse(await readFile(
  new URL("../package.json", import.meta.url),
  "utf8",
));
const extensionSource = await readFile(
  new URL("../src/extension.ts", import.meta.url),
  "utf8",
);

const current = {
  id: "a",
  project: "/work/apps/demo/Demo.csproj",
  tfm: "net10.0-windows10.0.19041.0",
  platform: "windows",
  appName: "Demo",
  packageId: "com.example.demo",
  deviceId: "desktop",
  sessionId: "session-a",
  processId: 42,
  port: 9223,
};

const approvalRequest = Object.freeze({
  approvalRequestId: "approval-1",
  kind: "run",
  intent: "Run the reviewed login test once",
  approvedScope: {
    allowedActions: ["tap"],
    allowedSelectors: ["automationId:SignIn"],
    allowedRoutes: ["/login"],
    allowedSideEffectClasses: ["test-tenant-resettable"],
    maxActionCount: 1,
    maxValueBytes: 64,
  },
  grantDurationSeconds: 300,
  appName: "Demo",
  platform: "android",
  scopeSummary: "tap, 1 exact selector, up to 1 action",
});

const OWNER_TOKEN = "o".repeat(43);
const CAPABILITY = "c".repeat(43);

function brokerState() {
  return { port: 19223, nativeApprovalToken: OWNER_TOKEN };
}

test("reconnect selection fails closed on duplicate matches", () => {
  assert.equal(selectRefreshedAgent([{ ...current }], current)?.id, "a");
  assert.equal(selectRefreshedAgent([
    { ...current, id: "b" },
    { ...current, id: "c" },
  ], current), null);
  assert.equal(agentRuntimeIdentity(current), "a|session-a|42|desktop|com.example.demo");
});

test("host shell copy stays specific about what is missing", () => {
  assert.match(inspectorTitle("Demo"), /Demo/);
  assert.equal(reconnectDiscoveryAction("broker"), "wait");
  assert.match(renderReconnectHost("app", "nonce"), /nonce/);
});

test("bridge requests that return a result require a request id", () => {
  assert.equal(requiresBridgeRequestId("devflow:nativeApproval"), true);
  assert.equal(requiresBridgeRequestId("devflow:selectionChanged"), false);
  // This layer serves the layout suppression policy bridge, and VS Code is the only host that can
  // obtain the trusted confirmation it needs, so the page must await a real host reply.
  assert.equal(requiresBridgeRequestId("devflow:layoutPolicyMutation"), true);
  // One-way notifications carry no request id. Requiring one would drop them at the host's guard,
  // so "open source" and "save recording draft" would report success and do nothing.
  for (const notification of [
    "devflow:sendToCopilot",
    "devflow:openSource",
    "devflow:recordingComplete",
  ]) {
    assert.equal(requiresBridgeRequestId(notification), false, notification);
  }
});

test("the host's request set matches the modes the Inspector page declares", async () => {
  const bridge = await readFile(
    new URL(
      "../../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/inspector-host-bridge.js",
      import.meta.url,
    ),
    "utf8",
  );
  const contract = await readFile(
    new URL("../src/bridge-contract.ts", import.meta.url),
    "utf8",
  );

  const pageRequests = [...bridge.matchAll(/message: '(devflow:[A-Za-z]+)', mode: '(\w+)'/g)]
    .filter(([, , mode]) => mode === "request")
    .map(([, message]) => message)
    .sort();
  const hostRequests = [...contract.matchAll(/"(devflow:[A-Za-z]+)"/g)]
    .map(([, message]) => message)
    .sort();

  assert.notEqual(pageRequests.length, 0);
  assert.deepEqual(hostRequests, pageRequests);
  for (const message of pageRequests) assert.equal(requiresBridgeRequestId(message), true, message);
  // Source apply is deferred to a later layer. Neither side may reintroduce it alone: this host
  // would then hold a write capability the page never asked for, or the page would offer an apply
  // button that silently times out.
  for (const deferred of [
    "devflow:applySourceProposal",
    "devflow:applyCSharpSourceProposal",
    "devflow:getCSharpSourceSelection",
  ]) {
    assert.ok(!pageRequests.includes(deferred), `page still offers ${deferred}`);
    assert.ok(!hostRequests.includes(deferred), `host still accepts ${deferred}`);
  }
});

test("native approval redeems one owner-minted capability and never leaks the owner token", async () => {
  assert.equal(isNativeApprovalRequest(approvalRequest), true);
  assert.equal(
    isNativeApprovalRequest({
      ...approvalRequest,
      approvedScope: { ...approvalRequest.approvedScope, allowedActions: [] },
    }),
    false,
  );

  const calls = [];
  const result = await performNativeApproval(
    approvalRequest,
    { brokerPort: 19223, agentId: "agent-1" },
    brokerState,
    async (url, options) => {
      calls.push({ url, options });
      return calls.length === 1
        ? { ok: true, status: 201, json: async () => ({ confirmationCapability: CAPABILITY }) }
        : { ok: true, status: 200, json: async () => ({ ok: true, message: "Approved once." }) };
    },
  );

  assert.equal(result.ok, true);
  assert.equal(calls.length, 2);
  assert.match(calls[0].url, /approval-confirmations\/issue$/);
  assert.equal(calls[0].options.headers["X-DevFlow-Host-Approval-Token"], OWNER_TOKEN);
  assert.match(calls[1].url, /agent-requests\/approval-1\/approve$/);
  assert.equal(calls[1].options.headers["X-DevFlow-Host-Approval-Token"], undefined);
  const approveBody = JSON.parse(calls[1].options.body);
  assert.equal(approveBody.humanConfirmed, true);
  assert.equal(approveBody.confirmationCapability, CAPABILITY);
  assert.equal(JSON.stringify(result).includes(OWNER_TOKEN), false);
});

test("native approval reports a refusal without approving anything", async () => {
  const calls = [];
  const result = await performNativeApproval(
    approvalRequest,
    { brokerPort: 19223, agentId: "agent-1" },
    brokerState,
    async (url) => {
      calls.push(url);
      return {
        ok: false,
        status: 403,
        json: async () => ({ ok: false, code: "trusted-host-required", error: "refused" }),
      };
    },
  );

  assert.equal(result.ok, false);
  assert.equal(result.error, "refused");
  assert.equal(calls.length, 1, "a refused confirmation must not be followed by an approve");
});

test("a replayed confirmation capability is refused and reported, not retried", async () => {
  const calls = [];
  const result = await performNativeApproval(
    approvalRequest,
    { brokerPort: 19223, agentId: "agent-1" },
    brokerState,
    async (url) => {
      calls.push(url);
      return calls.length === 1
        ? { ok: true, status: 201, json: async () => ({ confirmationCapability: CAPABILITY }) }
        : {
            ok: false,
            status: 403,
            json: async () => ({
              ok: false,
              code: "approval-confirmation-invalid",
              error: "The approval confirmation was rejected.",
            }),
          };
    },
  );

  assert.equal(result.ok, false);
  assert.match(result.error ?? "", /rejected/);
  assert.equal(calls.length, 2, "a rejected approve must not be retried with the same capability");
});

test("native approval refuses a broker state that targets another Inspector", async () => {
  let called = false;
  const result = await performNativeApproval(
    approvalRequest,
    { brokerPort: 19223, agentId: "agent-1" },
    () => ({ port: 19999, nativeApprovalToken: OWNER_TOKEN }),
    async () => {
      called = true;
      return { ok: true, status: 200, json: async () => ({}) };
    },
  );
  assert.equal(result.ok, false);
  assert.equal(called, false);
});

test("the manifest advertises only what the extension registers", () => {
  assert.deepEqual(Object.keys(packageJson.contributes).sort(), ["commands", "configuration"]);
  assert.deepEqual(
    packageJson.contributes.commands.map((command) => command.command),
    ["mauiDevflow.openInspector"],
  );
  for (const command of packageJson.contributes.commands) {
    assert.match(
      extensionSource,
      new RegExp(`registerCommand\\(\\s*"${command.command.replace(".", "\\.")}"`),
    );
  }
  assert.deepEqual(
    Object.keys(packageJson.contributes.configuration.properties).sort(),
    ["mauiDevflow.brokerPort", "mauiDevflow.openLocation", "mauiDevflow.publishDiagnostics"],
  );
  for (const setting of Object.keys(packageJson.contributes.configuration.properties)) {
    assert.ok(
      extensionSource.includes(`"${setting.replace("mauiDevflow.", "")}"`),
      `${setting} is advertised but never read`,
    );
  }
  // Nothing here contributes a chat participant, language-model tools, an MCP definition provider,
  // or a URI handler, so the manifest must not promise them.
  assert.equal(extensionSource.includes("vscode.chat"), false);
  assert.equal(extensionSource.includes("vscode.lm.registerTool"), false);
  assert.equal(extensionSource.includes("registerUriHandler"), false);
  assert.deepEqual(packageJson.activationEvents, []);
});

test("VS Code host supports digest-bound layout policy approval", () => {
  assert.match(extensionSource, /"layoutPolicyMutation"/);
  assert.match(extensionSource, /action: "layout-policy-mutation"/);
  assert.match(extensionSource, /expectedPolicyDigest/);
});

test("bounded references expire and evict without encoding context", () => {
  const store = new BoundedReferenceStore(1, 1024, 60_000);
  const first = store.put({ problem: "one" });
  const second = store.put({ problem: "two" });
  assert.match(first, /^[A-Za-z0-9_-]+$/);
  assert.equal(store.get(first), null);
  assert.deepEqual(store.get(second), { problem: "two" });
  assert.ok(!second.includes("two"));
});

test("runtime Problems and layout findings offer bounded explanation actions", () => {
  assert.equal(supportsDiagnosticExplanation("problem"), true);
  assert.equal(supportsDiagnosticExplanation("layout"), true);
});
