import assert from "node:assert/strict";
import { mkdirSync, rmSync, writeFileSync } from "node:fs";
import http from "node:http";
import { join } from "node:path";
import test from "node:test";
import { DevFlowWorkspaceSession } from "../src/index.js";
import { readBrokerState } from "../src/broker.js";

test("workspace session resolves an exact agent and uses broker-hosted Inspector reads", async (t) => {
  let instanceId = "instance-1";
  let readToken = "11111111111111111111111111111111";
  let layoutSupported = true;
  const requests: Array<{
    method?: string;
    url?: string;
    body: unknown;
    inspectorToken?: string;
  }> = [];
  const server = http.createServer(async (request, response) => {
    const body = await readJson(request);
    requests.push({
      method: request.method,
      url: request.url,
      body,
      inspectorToken: header(request, "x-devflow-inspector-token"),
    });
    response.setHeader("Content-Type", "application/json");

    if (request.url === "/api/agents") {
      response.end(JSON.stringify([agent("agent/one", instanceId, 9123)]));
      return;
    }
    if (request.url === "/inspector/agent%2Fone/?embed=trusted-embed-token") {
      response.setHeader("Content-Type", "text/html; charset=utf-8");
      response.end(inspectorHtml("agent/one", instanceId, readToken));
      return;
    }
    if (request.url === "/inspector/agent%2Fone/api/inspect/snapshot") {
      response.end(JSON.stringify(snapshot("agent/one", "snapshot-1")));
      return;
    }
    if (request.url === "/inspector/agent%2Fone/api/inspect/query") {
      response.end(JSON.stringify({
        ok: true,
        protocolVersion: 1,
        projection: "activeVisual",
        snapshotId: "snapshot-2",
        revision: "snapshot-2",
        elements: [],
      }));
      return;
    }
    if (request.url === "/inspector/agent%2Fone/api/problems") {
      if (header(request, "x-devflow-inspector-token") !== readToken) {
        response.writeHead(403);
        response.end(JSON.stringify({ ok: false, error: "forbidden" }));
        return;
      }
      response.end(JSON.stringify({
        ok: true,
        enabled: true,
        revision: 4,
        count: 1,
        evicted: 0,
        problems: [{
          id: "problem-1",
          kind: "binding",
          severity: "warning",
          message: "Missing path",
          count: 1,
          firstSeenUtc: "2026-01-01T00:00:00Z",
          lastSeenUtc: "2026-01-01T00:00:01Z",
        }],
      }));
      return;
    }
    if (request.url === "/inspector/agent%2Fone/api/diagnostics/layout") {
      if (header(request, "x-devflow-inspector-token") !== readToken) {
        response.writeHead(403);
        response.end(JSON.stringify({ ok: false, error: "forbidden" }));
        return;
      }
      response.end(JSON.stringify(layoutSupported ? {
        ok: true,
        report: {
          schemaVersion: "2.0",
          ruleSetVersion: "2.0",
          snapshot: {
            id: "layout-1",
            capturedAt: "2026-01-01T00:00:00Z",
            platform: "windows",
            treeRevision: "tree-1",
            diagnosticsRevision: "diagnostics-1",
            stable: true,
            nodeCount: 1,
            windows: [],
          },
          capturedUtc: "2026-01-01T00:00:00Z",
          platform: "windows",
          scope: { maxElements: 100, elementsExamined: 1, truncated: false },
          coverage: { overall: "full", rules: [], opaqueSubtrees: [], limitations: [], neverCaptured: [] },
          summary: { violations: 0, observations: 0, incomplete: 0, passes: 1, notApplicable: 0, suppressed: 0 },
          findings: [],
        },
      } : {
        ok: false,
        error: "Layout diagnostics are not supported by the connected agent.",
      }));
      return;
    }
    if (request.url === "/inspector/agent%2Fone/api/evidence/preview") {
      if (header(request, "x-devflow-inspector-token") !== readToken) {
        response.writeHead(403);
        response.end(JSON.stringify({ ok: false, error: "forbidden" }));
        return;
      }
      response.end(JSON.stringify({
        ok: true,
        plan: {
          ok: true,
          schema: "maui-devflow-evidence",
          formatVersion: 1,
          redactionVersion: 1,
          source: "inspector",
          generatedUtc: "2026-01-01T00:00:00Z",
          included: [],
          excluded: [],
          neverIncluded: ["secure-storage"],
          screenshot: { requested: false, included: false },
          counts: { treeElements: 1 },
          limits: { treeElements: 2000 },
          warnings: [],
          suggestedFileName: "evidence.mauitrace",
          estimatedBytes: 1024,
        },
      }));
      return;
    }

    response.writeHead(404);
    response.end(JSON.stringify({ error: "not found" }));
  });

  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  t.after(() => server.close());
  const address = server.address();
  assert.ok(address && typeof address !== "string");
  installBrokerState(t, address.port, "trusted-embed-token", "native-approval-token-12345678901234567890");
  assert.equal(readBrokerState()?.nativeApprovalToken, "native-approval-token-12345678901234567890");

  const session = new DevFlowWorkspaceSession({
    brokerPort: address.port,
    agent: { agentId: "agent/one" },
  });
  t.after(() => session.dispose());

  const connected = await session.connect();
  assert.equal(connected.ok, true);
  assert.equal(connected.ok && connected.value.agentInstanceId, "instance-1");

  const url = session.buildInspectorUrl("/api/inspect/snapshot", { frame: "a b" });
  assert.deepEqual(url, {
    ok: true,
    value: `http://localhost:${address.port}/inspector/agent%2Fone/api/inspect/snapshot?frame=a+b`,
  });

  const snapshotResult = await session.getSnapshot();
  assert.equal(snapshotResult.ok, true);
  assert.equal(snapshotResult.ok && snapshotResult.value.snapshotRevision, "snapshot-1");

  const query = await session.query({ automationId: "SaveButton" });
  assert.equal(query.ok, true);
  assert.equal(query.ok && query.value.snapshotRevision, "snapshot-2");

  const problems = await session.getProblems({ limit: 25, elementId: "button-1" });
  assert.equal(problems.ok, true);
  assert.equal(problems.ok && problems.value.value.problems[0]?.id, "problem-1");

  const layout = await session.analyzeLayout({ schemaVersion: "2.0", maxElements: 100 });
  assert.equal(layout.ok, true);
  assert.equal(layout.ok && layout.value.value.coverage.overall, "full");

  const layoutRequestCount = requests.filter((request) =>
    request.url?.endsWith("/api/diagnostics/layout")).length;
  const invalidLayout = await session.analyzeLayout({ maxElements: 5001 });
  assert.equal(invalidLayout.ok, false);
  assert.equal(!invalidLayout.ok && invalidLayout.error.kind, "invalid-argument");
  assert.equal(requests.filter((request) =>
    request.url?.endsWith("/api/diagnostics/layout")).length, layoutRequestCount);

  layoutSupported = false;
  const unavailableLayout = await session.analyzeLayout();
  assert.equal(unavailableLayout.ok, false);
  assert.equal(!unavailableLayout.ok && unavailableLayout.error.kind, "capability-missing");
  layoutSupported = true;

  const evidence = await session.previewEvidence({ logLimit: 25, networkLimit: 25 });
  assert.equal(evidence.ok, true);
  assert.equal(evidence.ok && evidence.value.value.source, "inspector");

  assert.equal(
    requests.filter((request) => request.url === "/inspector/agent%2Fone/?embed=trusted-embed-token").length,
    1,
  );
  assert.ok(requests
    .filter((request) => request.url?.includes("/api/problems") ||
      request.url?.includes("/api/diagnostics/layout") ||
      request.url?.includes("/api/evidence/preview"))
    .every((request) => request.inspectorToken === "11111111111111111111111111111111"));
  assert.ok(requests
    .filter((request) => request.url?.includes("/api/inspect/") ||
      request.url?.includes("?embed=") ||
      request.url === "/api/agents")
    .every((request) => request.inspectorToken == null));
  assert.ok(requests
    .filter((request) => request.url?.startsWith("/inspector/") && !request.url?.includes("?embed="))
    .every((request) => !request.url?.includes("trusted-embed-token")));

  readToken = "22222222222222222222222222222222";
  const refreshedToken = await session.getProblems();
  assert.equal(refreshedToken.ok, true);
  assert.equal(
    requests.filter((request) => request.url === "/inspector/agent%2Fone/?embed=trusted-embed-token").length,
    2,
  );
  assert.equal(
    requests.filter((request) => request.url?.endsWith("/api/problems")).at(-1)?.inspectorToken,
    readToken,
  );

  assert.deepEqual(
    requests.find((request) => request.url?.endsWith("/api/inspect/query"))?.body,
    { automationId: "SaveButton" },
  );
  assert.deepEqual(
    requests.find((request) => request.url?.endsWith("/api/problems"))?.body,
    { limit: 25, elementId: "button-1" },
  );

  const reference = session.captureReference();
  assert.equal(reference.ok, true);
  instanceId = "instance-2";
  readToken = "33333333333333333333333333333333";
  const stale = reference.ok ? await session.assertCurrent(reference.value) : reference;
  assert.equal(stale.ok, false);
  assert.equal(!stale.ok && stale.error.kind, "stale-agent");
  assert.equal((await session.getProblems()).ok, true);
  assert.equal(
    requests.filter((request) => request.url?.endsWith("/api/problems")).at(-1)?.inspectorToken,
    readToken,
  );
});

test("workspace session emits bounded lifecycle changes and rejects unsafe construction", async (t) => {
  let agents = [agent("agent-1", "instance-1", 9123), agent("agent-2", "instance-2", 9124)];
  const server = http.createServer((request, response) => {
    response.setHeader("Content-Type", "application/json");
    if (request.url === "/api/agents") {
      response.end(JSON.stringify(agents));
      return;
    }
    response.writeHead(404);
    response.end("{}");
  });
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  t.after(() => server.close());
  const address = server.address();
  assert.ok(address && typeof address !== "string");

  const session = new DevFlowWorkspaceSession({
    brokerPort: address.port,
    agent: { port: 9123 },
  });
  t.after(() => session.dispose());
  const events: string[] = [];
  session.onLifecycle((event) => events.push(event.type));

  assert.equal((await session.connect()).ok, true);
  const unsafe = session.buildInspectorUrl("/../../outside");
  assert.equal(unsafe.ok, false);
  assert.equal(!unsafe.ok && unsafe.error.kind, "invalid-argument");
  assert.equal((await session.query({ selector: "*", text: "no" })).ok, false);
  assert.equal((await session.getProblems({ limit: 501 })).ok, false);

  agents = [agent("agent-1", "instance-new", 9123), agent("agent-2", "instance-2", 9124)];
  assert.equal((await session.refresh()).ok, true);
  agents = [agent("agent-2", "instance-2", 9124)];
  assert.equal((await session.refresh()).ok, false);
  const selected = await session.selectAgent({ agentInstanceId: "instance-2" });
  assert.equal(selected.ok, true);
  assert.equal(selected.ok && selected.value.agentId, "agent-2");
  assert.deepEqual(events, ["connected", "restarted", "disconnected", "selected-agent-changed"]);
});

test("workspace session rejects malformed Inspector token metadata without sending gated reads", async (t) => {
  let gatedRequests = 0;
  const server = http.createServer((request, response) => {
    if (request.url === "/api/agents") {
      response.setHeader("Content-Type", "application/json");
      response.end(JSON.stringify([agent("agent-1", "instance-1", 9123)]));
      return;
    }
    if (request.url === "/inspector/agent-1/?embed=trusted-embed-token") {
      response.setHeader("Content-Type", "text/html");
      response.end([
        '<meta name="devflow-inspector-token" content="not-a-token">',
        '<meta name="devflow-agent-id" content="agent-1">',
        '<meta name="devflow-agent-instance-id" content="instance-1">',
      ].join(""));
      return;
    }
    if (request.url === "/inspector/agent-1/api/problems") gatedRequests++;
    response.writeHead(403);
    response.end("{}");
  });
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  t.after(() => server.close());
  const address = server.address();
  assert.ok(address && typeof address !== "string");
  installBrokerState(t, address.port, "trusted-embed-token");

  const session = new DevFlowWorkspaceSession({
    brokerPort: address.port,
    agent: { agentId: "agent-1" },
  });
  t.after(() => session.dispose());
  const result = await session.getProblems();
  assert.equal(result.ok, false);
  assert.equal(!result.ok && result.error.kind, "bad-json");
  assert.equal(gatedRequests, 0);
});

function agent(id: string, instanceId: string, port: number) {
  return {
    id,
    instanceId,
    project: "C:\\src\\App.csproj",
    tfm: "net10.0-windows",
    platform: "windows",
    appName: "App",
    port,
    connectedAt: "2026-01-01T00:00:00Z",
  };
}

function snapshot(agentId: string, revision: string) {
  return {
    ok: true,
    protocolVersion: 1,
    projection: "activeVisual",
    snapshotId: revision,
    revision,
    capturedAt: "2026-01-01T00:00:00Z",
    target: { agentId, appName: "App", platform: "windows" },
    viewport: { width: 400, height: 800, rootOffsetX: 0, rootOffsetY: 0 },
    screenshotUrl: `screenshot.png?frame=${revision}`,
    roots: [],
  };
}

async function readJson(request: http.IncomingMessage): Promise<unknown> {
  const chunks: Buffer[] = [];
  for await (const chunk of request) chunks.push(Buffer.from(chunk));
  if (chunks.length === 0) return null;
  return JSON.parse(Buffer.concat(chunks).toString("utf8"));
}

function inspectorHtml(agentId: string, instanceId: string, token: string): string {
  return [
    "<html><head>",
    `<meta name="devflow-inspector-token" content="${token}">`,
    `<meta name="devflow-agent-id" content="${agentId}">`,
    `<meta name="devflow-agent-instance-id" content="${instanceId}">`,
    "</head></html>",
  ].join("");
}

function header(request: http.IncomingMessage, name: string): string | undefined {
  const value = request.headers[name];
  return Array.isArray(value) ? value[0] : value;
}

function installBrokerState(
  t: { after(callback: () => void): void },
  port: number,
  embedToken: string,
  nativeApprovalToken?: string,
): void {
  const previousUserProfile = process.env.USERPROFILE;
  const previousHome = process.env.HOME;
  const home = join(process.cwd(), `.workspace-session-home-${process.pid}-${Date.now()}-${port}`);
  const stateDirectory = join(home, ".mauidevflow");
  mkdirSync(stateDirectory, { recursive: true });
  writeFileSync(join(stateDirectory, "broker.json"), JSON.stringify({
    pid: process.pid,
    port,
    startedAt: "2026-01-01T00:00:00Z",
    embedToken,
    nativeApprovalToken,
  }));
  process.env.USERPROFILE = home;
  process.env.HOME = home;
  t.after(() => {
    if (previousUserProfile == null) delete process.env.USERPROFILE;
    else process.env.USERPROFILE = previousUserProfile;
    if (previousHome == null) delete process.env.HOME;
    else process.env.HOME = previousHome;
    rmSync(home, { recursive: true, force: true });
  });
}
