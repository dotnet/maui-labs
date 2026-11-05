// Deterministic integration test against a fake agent (a local loopback HTTP server that
// mimics the broker /api/agents registry + the agent endpoints). Exercises the parts that
// pure unit tests can't: resolve() → getTree() → the composite coordinate-tap fall-through
// (child declines with success:false → root accepts). Pinning brokerPort+agentPort to the
// fake keeps it isolated from any real broker/agents on the machine.

import { test } from "node:test";
import assert from "node:assert/strict";
import http from "node:http";
import type { AddressInfo } from "node:net";
import { DevFlowClient } from "../src/index.js";

interface FakeAgent {
  port: number;
  taps: string[];
  recordingHeaders: string[];
  recordingRequests: Array<{ action?: string; recordingId?: string }>;
  leaseActions: string[];
  close: () => Promise<void>;
}

function startFakeAgent(): Promise<FakeAgent> {
  const taps: string[] = [];
  const recordingHeaders: string[] = [];
  const recordingRequests: Array<{ action?: string; recordingId?: string }> = [];
  const leaseActions: string[] = [];
  let recording = false;
  let heldLease: string | null = null;
  const server = http.createServer((req, res) => {
    const url = req.url || "";
    const port = (server.address() as AddressInfo).port;
    const send = (obj: unknown, status = 200): void => {
      res.writeHead(status, { "Content-Type": "application/json" });
      res.end(JSON.stringify(obj));
    };
    if (req.method === "GET" && url === "/api/agents") {
      return send([
        { id: "x", project: "", tfm: "net10.0-windows10.0.19041.0", platform: "windows", appName: "Fake", port },
      ]);
    }
    if (req.method === "GET" && url.startsWith("/api/v1/agent/status")) {
      return send({ running: true, app: { name: "Fake" }, device: { platform: "windows" } });
    }
    if (req.method === "POST" && url === "/api/v1/agent/lease") {
      let body = "";
      req.on("data", (c) => (body += c));
      req.on("end", () => {
        const request = JSON.parse(body || "{}") as { action?: string; leaseId?: string };
        const action = request.action || "status";
        const leaseId = request.leaseId || "";
        leaseActions.push(action);
        if (action === "claim" && leaseId) heldLease = leaseId;
        if (action === "release" && heldLease === leaseId) heldLease = null;
        const youHold = !!heldLease && heldLease === leaseId;
        return send({
          ok: true,
          allowed: youHold,
          youHold,
          heldByOther: !!heldLease && !youHold,
          authority: "agent",
        });
      });
      return;
    }
    if (req.method === "POST" && url === "/api/v1/agent/recording") {
      let body = "";
      req.on("data", (c) => (body += c));
      req.on("end", () => {
        recordingHeaders.push(String(req.headers["x-devflow-lease"] || ""));
        const request = JSON.parse(body || "{}") as { action?: string; recordingId?: string };
        recordingRequests.push(request);
        const action = request.action;
        if (action === "start") {
          recording = true;
          return send({ ok: true, recording: true, recordingId: "recording-1", name: "scenario", steps: 0 });
        }
        if (action === "stop") {
          recording = false;
          return send({ ok: true, recording: false, recordingId: "recording-1", name: "scenario", steps: 2, markdown: "# Scenario" });
        }
        return send({ ok: true, recording, recordingId: recording ? "recording-1" : null, name: recording ? "scenario" : null, steps: recording ? 2 : 0 });
      });
      return;
    }
    if (req.method === "GET" && url.startsWith("/api/v1/ui/tree")) {
      return send([{ id: "root", type: "Page", fullType: "P", children: [{ id: "child", type: "Button", fullType: "B" }] }]);
    }
    if (req.method === "GET" && url.startsWith("/api/v1/ui/hit-test")) {
      return send({ elements: [{ id: "child1" }, { id: "child2" }, { id: "root" }] });
    }
    if (req.method === "POST" && url === "/api/v1/ui/actions/tap") {
      let body = "";
      req.on("data", (c) => (body += c));
      req.on("end", () => {
        const j = JSON.parse(body || "{}") as { elementId?: string };
        const id = j.elementId ?? "";
        taps.push(id);
        if (id === "root") return send({ success: true });
        // child1 declines like the REAL agent (HTTP 400 + {success:false,error}); child2
        // declines via 2xx {success:false}. Both must be treated as semantic rejections
        // so the composite falls through; neither is a transport error.
        if (id === "child1") return send({ success: false, error: "element not tappable" }, 400);
        return send({ success: false });
      });
      return;
    }
    send({ error: "not found" }, 404);
  });

  return new Promise<FakeAgent>((resolve) => {
    server.listen(0, "127.0.0.1", () => {
      const port = (server.address() as AddressInfo).port;
      resolve({
        port,
        taps,
        recordingHeaders,
        recordingRequests,
        leaseActions,
        close: () => new Promise<void>((r) => server.close(() => r())),
      });
    });
  });
}

test("integration: resolve (broker-pinned) → tree → composite tap falls through child→root", async () => {
  const fake = await startFakeAgent();
  const client = new DevFlowClient({ brokerPort: fake.port, agentPort: fake.port, bootstrapBroker: "never" });
  try {
    const tree = await client.getTree();
    assert.equal(tree.ok, true, tree.ok ? "" : tree.error.message);
    assert.equal(tree.ok && tree.value[0]?.id, "root");
    assert.equal(client.target?.appName, "Fake");

    const tap = await client.tap({ x: 5, y: 5 });
    assert.equal(tap.ok, true, tap.ok ? "" : tap.error.message);
    // child1 (400 decline) then child2 (2xx decline) then root (accepted) — both decline
    // shapes fall through, and no element is tapped twice (no double-apply).
    assert.deepEqual(fake.taps, ["child1", "child2", "root"]);
  } finally {
    client.dispose();
    await fake.close();
  }
});

test("integration: getStatus reports the resolved agent", async () => {
  const fake = await startFakeAgent();
  const client = new DevFlowClient({ brokerPort: fake.port, agentPort: fake.port, bootstrapBroker: "never" });
  try {
    const status = await client.getStatus();
    assert.equal(status.ok, true, status.ok ? "" : status.error.message);
    assert.equal(status.ok && status.value?.running, true);
  } finally {
    client.dispose();
    await fake.close();
  }
});

test("integration: shared recording control uses the mutation lease", async () => {
  const fake = await startFakeAgent();
  const client = new DevFlowClient({ brokerPort: fake.port, agentPort: fake.port, bootstrapBroker: "never" });
  try {
    const started = await client.controlMutationRecording("start", { name: "scenario" });
    assert.equal(started.ok, true, started.ok ? "" : started.error.message);
    assert.equal(started.ok && started.value.recordingId, "recording-1");

    const status = await client.controlMutationRecording("status");
    assert.equal(status.ok && status.value.steps, 2);

    const stopped = await client.controlMutationRecording("stop", { recordingId: "recording-1" });
    assert.equal(stopped.ok && stopped.value.markdown, "# Scenario");
    assert.equal(fake.recordingHeaders.length, 3);
    assert.ok(fake.recordingHeaders.every((header) => header.length > 0));
    assert.equal(new Set(fake.recordingHeaders).size, 1);
    assert.equal(fake.recordingRequests[2]?.recordingId, "recording-1");
  } finally {
    client.dispose();
    await fake.close();
  }
});

test("integration: recording status does not acquire a free mutation lease", async () => {
  const fake = await startFakeAgent();
  const client = new DevFlowClient({ brokerPort: fake.port, agentPort: fake.port, bootstrapBroker: "never" });
  try {
    const status = await client.controlMutationRecording("status");
    assert.equal(status.ok, true, status.ok ? "" : status.error.message);
    assert.equal(status.ok && status.value.recording, false);
    assert.deepEqual(fake.leaseActions, []);
    assert.deepEqual(fake.recordingRequests.map((request) => request.action), ["status"]);
  } finally {
    client.dispose();
    await fake.close();
  }
});
