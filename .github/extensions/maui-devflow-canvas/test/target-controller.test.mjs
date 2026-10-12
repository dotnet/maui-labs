import assert from "node:assert/strict";
import test from "node:test";
import { createTargetController } from "../target-controller.mjs";
import { agentIdentityKey } from "../targeting.mjs";
import { createServer } from "node:http";
import { LiveStore } from "../store.mjs";

const old = {
  id: "old", project: "C:/old/App.csproj", tfm: "net10.0", platform: "Windows",
  appName: "Old", sessionId: "old-worktree", port: 9223,
};
const chosen = { ...old, id: "chosen", project: "C:/chosen/App.csproj", appName: "Chosen", port: 9224 };
const latest = { ...old, id: "latest", project: "C:/latest/App.csproj", appName: "Latest", port: 9225 };

function deferred() {
  let resolve;
  const promise = new Promise((r) => { resolve = r; });
  return { promise, resolve };
}

function fixture() {
  let resolved = old;
  let port = old.port;
  let snapshot = { connected: true, info: { appName: old.appName } };
  const calls = [];
  const f = { agents: [old, chosen, latest], broker: { port: 19223, embedToken: "test" }, onSelect: null };
  const device = {
    opts: { agentPort: port },
    resolvedAgent: () => resolved,
    whichPort: () => port,
  };
  f.st = {
    targetAgentId: old.id, targetIdentity: agentIdentityKey(old), targetPort: old.port,
    targetMutation: Promise.resolve(),
    store: {
      device,
      snapshot: () => snapshot,
      async selectAgent(options) {
        calls.push(options);
        port = options.port ?? f.agents?.find((a) => a.platform === options.platform)?.port;
        device.opts.agentPort = port;
        resolved = f.agents?.find((a) => a.port === port) ?? null;
        snapshot = f.onSelect ? await f.onSelect(options) : { connected: !!resolved };
        return snapshot;
      },
    },
  };
  f.controller = createTargetController({
    readBrokerState: () => f.broker,
    fetchBrokerAgents: async () => f.agents,
  });
  f.calls = calls;
  return f;
}

test("failed explicit switch stays unavailable and cannot resynchronize the old app", async () => {
  const f = fixture();
  f.onSelect = async () => {
    assert.equal(f.st.targetAgentId, chosen.id);
    assert.equal(f.st.targetIdentity, agentIdentityKey(chosen));
    f.agents = [old];
    return { connected: false };
  };
  assert.equal((await f.controller.selectTargetAgent(f.st, { port: chosen.port })).connected, false);
  const readiness = await f.controller.resolveInspectorState(f.st);
  assert.equal(readiness.ready, false);
  assert.equal(readiness.state, "target");
  assert.equal(readiness.inspectorUrl, null);
  assert.equal((await f.controller.syncTargetAgent(f.st, old.id)).ok, false);
  assert.deepEqual(f.calls.map((c) => c.port), [chosen.port]);
});

test("successful explicit switch aligns readiness and store with the chosen app", async () => {
  const f = fixture();
  assert.equal((await f.controller.selectTargetAgent(f.st, { port: chosen.port })).connected, true);
  assert.equal((await f.controller.resolveInspectorState(f.st)).agentId, chosen.id);
  assert.equal((await f.controller.syncTargetAgent(f.st, chosen.id)).ok, true);
  assert.equal(f.st.store.device.resolvedAgent().id, chosen.id);
  assert.equal(f.calls.length, 1);
});

test("a connected snapshot for a different registration does not replace the choice", async () => {
  const f = fixture();
  f.st.store.device.resolvedAgent = () => old;
  const result = await f.controller.selectTargetAgent(f.st, { port: chosen.port });
  assert.equal(result.connected, false);
  assert.match(result.error, /could not be connected/);
  assert.equal(f.st.targetAgentId, chosen.id);
  assert.equal((await f.controller.syncTargetAgent(f.st, old.id)).ok, false);
});

test("unknown requested port never inherits the old resolved registration", async () => {
  const f = fixture();
  f.agents = [old];
  const pending = f.controller.selectTargetAgent(f.st, { port: chosen.port });
  assert.equal(f.st.targetAgentId, null);
  assert.equal(f.st.targetPort, chosen.port);
  assert.equal((await f.controller.resolveInspectorState(f.st)).state, "target");
  await pending;
  assert.equal((await f.controller.syncTargetAgent(f.st, old.id)).ok, false);
  f.agents.push(chosen);
  assert.equal((await f.controller.resolveInspectorState(f.st)).agentId, chosen.id);
  assert.equal((await f.controller.syncTargetAgent(f.st, chosen.id)).ok, true);
  assert.equal(f.st.targetAgentId, chosen.id);
});

test("failed platform-only switch retains the discovered identity", async () => {
  const f = fixture();
  const android = { ...chosen, platform: "Android" };
  f.agents = [old, android];
  f.onSelect = async () => {
    f.agents = [old];
    return { connected: false };
  };
  await f.controller.selectTargetAgent(f.st, { platform: "Android" });
  assert.equal(f.st.targetAgentId, android.id);
  assert.equal((await f.controller.resolveInspectorState(f.st)).state, "target");
  assert.equal((await f.controller.syncTargetAgent(f.st, old.id)).ok, false);
});

test("broker loss during a platform choice does not make the previous app ready", async () => {
  const f = fixture();
  f.broker = null;
  f.onSelect = async () => ({ connected: false });
  await f.controller.selectTargetAgent(f.st, { platform: "Android" });
  f.broker = { port: 19223 };
  assert.equal((await f.controller.resolveInspectorState(f.st)).state, "target");
  assert.equal((await f.controller.syncTargetAgent(f.st, old.id)).ok, false);
});

test("thrown connection failure retains the choice and does not poison the mutation queue", async () => {
  const f = fixture();
  f.onSelect = async () => { throw new Error("connection failed"); };
  await assert.rejects(f.controller.selectTargetAgent(f.st, { port: chosen.port }), /connection failed/);
  f.agents = [old];
  assert.equal((await f.controller.resolveInspectorState(f.st)).state, "target");
  assert.equal((await f.controller.syncTargetAgent(f.st, old.id)).ok, false);
  f.onSelect = null;
  await f.controller.selectTargetAgent(f.st, { port: old.port });
  assert.equal((await f.controller.syncTargetAgent(f.st, old.id)).ok, true);
});

test("rapid queued choices discard superseded selection before connecting", async () => {
  const f = fixture();
  const first = f.controller.selectTargetAgent(f.st, { port: chosen.port });
  const second = f.controller.selectTargetAgent(f.st, { port: latest.port });
  assert.equal((await first).connected, false);
  await second;
  assert.deepEqual(f.calls.map((c) => c.port), [latest.port]);
  assert.equal((await f.controller.resolveInspectorState(f.st)).agentId, latest.id);
});

test("an in-flight selection cannot overwrite a newer explicit choice", async () => {
  const f = fixture();
  const started = deferred();
  const finish = deferred();
  f.onSelect = async ({ port }) => {
    if (port === chosen.port) {
      started.resolve();
      await finish.promise;
    }
    return { connected: true };
  };
  const first = f.controller.selectTargetAgent(f.st, { port: chosen.port });
  await started.promise;
  const second = f.controller.selectTargetAgent(f.st, { port: latest.port });
  assert.equal((await f.controller.resolveInspectorState(f.st)).agentId, latest.id);
  finish.resolve();
  assert.equal((await first).connected, false);
  await second;
  assert.equal(f.st.targetAgentId, latest.id);
  assert.equal((await f.controller.syncTargetAgent(f.st, chosen.id)).ok, false);
  assert.equal(f.st.store.device.resolvedAgent().id, latest.id);
});

test("queued shell synchronization cannot revert a subsequent explicit selection", async () => {
  const f = fixture();
  const staleSync = f.controller.syncTargetAgent(f.st, old.id);
  const select = f.controller.selectTargetAgent(f.st, { port: chosen.port });
  assert.equal((await staleSync).ok, false);
  await select;
  assert.deepEqual(f.calls.map((c) => c.port), [chosen.port]);
});

test("in-flight shell synchronization cannot commit over a newer choice", async () => {
  const f = fixture();
  f.st.store.device.opts.agentPort = 0;
  const started = deferred();
  const finish = deferred();
  f.onSelect = async ({ port }) => {
    if (port === old.port) {
      started.resolve();
      await finish.promise;
    }
    return { connected: true };
  };
  const sync = f.controller.syncTargetAgent(f.st, old.id);
  await started.promise;
  const select = f.controller.selectTargetAgent(f.st, { port: chosen.port });
  finish.resolve();
  assert.equal((await sync).ok, false);
  await select;
  assert.equal(f.st.targetAgentId, chosen.id);
  assert.equal(f.st.store.device.resolvedAgent().id, chosen.id);
});

test("selected app restart reconnects by identity but not to a different worktree", async () => {
  const f = fixture();
  await f.controller.selectTargetAgent(f.st, { port: chosen.port });
  const replacement = { ...chosen, id: "restarted", port: 9444 };
  const other = { ...replacement, id: "other-worktree", sessionId: "another-session", port: 9555 };
  f.agents = [old, other];
  assert.equal((await f.controller.resolveInspectorState(f.st)).state, "target");
  assert.equal((await f.controller.syncTargetAgent(f.st, other.id)).ok, false);
  f.agents.push(replacement);
  assert.equal((await f.controller.resolveInspectorState(f.st)).agentId, replacement.id);
  assert.equal((await f.controller.syncTargetAgent(f.st, replacement.id)).ok, true);
  assert.equal(f.st.store.device.whichPort(), replacement.port);
});

test("incomplete selected identity cannot synchronize a replacement on the reused port", async () => {
  for (const selected of [
    { ...chosen, project: "App.csproj" },
    { ...chosen, sessionId: undefined },
  ]) {
    const f = fixture();
    f.agents = [old, selected];
    await f.controller.selectTargetAgent(f.st, { port: selected.port });
    const replacement = { ...selected, id: "other-process" };
    f.agents = [old, replacement];
    assert.equal((await f.controller.resolveInspectorState(f.st)).state, "target");
    assert.equal((await f.controller.syncTargetAgent(f.st, replacement.id)).ok, false);
  }
});

test("unpinned initial discovery still connects the sole running app", async () => {
  const f = fixture();
  f.agents = [chosen];
  f.st.targetAgentId = f.st.targetIdentity = f.st.targetPort = null;
  f.st.store.device.resolvedAgent = () => null;
  f.st.store.device.whichPort = () => null;
  assert.equal((await f.controller.resolveInspectorState(f.st)).agentId, chosen.id);
});

test("real LiveStore and HTTP transport retain a failed choice and recover only that app", { timeout: 30000 }, async () => {
  let registrations = [];
  let chosenAvailable = true;
  const mutations = [];
  const servers = [];
  const storeOpts = { bootstrapBroker: "never" };
  function serve(agent) {
    const server = createServer((req, res) => {
      const path = new URL(req.url, "http://localhost").pathname;
      const json = (body, status = 200) => {
        res.writeHead(status, { "Content-Type": "application/json" });
        res.end(JSON.stringify(body));
      };
      if (path === "/api/agents") return json(registrations);
      if (agent.id === chosen.id && !chosenAvailable) {
        registrations = registrations.filter((a) => a.id !== chosen.id);
        return json({ error: "agent disconnected" }, 503);
      }
      if (path === "/api/v1/agent/status")
        return json({
          running: true, app: { name: agent.appName },
          device: { platform: "windows", windowWidth: 100, windowHeight: 100 },
          agent: { framework: "maui" }, capabilities: {},
        });
      if (path === "/api/v1/ui/tree")
        return json([{ id: `${agent.id}-button`, type: "Button", children: [],
          windowBounds: { x: 0, y: 0, width: 100, height: 100 } }]);
      if (path === "/api/v1/device/app/theme") return json({ theme: "light" });
      if (path === "/api/v1/agent/lease") {
        let body = "";
        req.on("data", (chunk) => { body += chunk; });
        req.on("end", () => json({ ok: true, allowed: true, youHold: true,
          authority: "agent", leaseId: JSON.parse(body).leaseId }));
        return;
      }
      if (path === "/api/v1/ui/actions/tap") {
        mutations.push(agent.id);
        req.resume();
        return json({ success: true });
      }
      return json({ error: "not supported by fixture" }, 404);
    });
    servers.push(server);
    return server;
  }
  let store;
  try {
    const aServer = serve(old);
    const bServer = serve(chosen);
    await Promise.all(servers.map((server) => new Promise((resolve) => server.listen(0, "127.0.0.1", resolve))));
    const a = { ...old, port: aServer.address().port };
    const b = { ...chosen, port: bServer.address().port };
    registrations = [a, b];
    const broker = { port: a.port, embedToken: "test" };
    store = new LiveStore({ ...storeOpts, brokerPort: a.port, agentPort: a.port });
    const st = { store, targetAgentId: a.id, targetIdentity: agentIdentityKey(a), targetPort: a.port };
    const controller = createTargetController({
      readBrokerState: () => broker,
      fetchBrokerAgents: async (port) => (await fetch(`http://127.0.0.1:${port}/api/agents`)).json(),
    });
    assert.equal((await store.refresh({ shot: false })).connected, true);
    chosenAvailable = false;
    const selection = await controller.selectTargetAgent(st, { port: b.port, platform: b.platform });
    assert.equal(selection.connected, false);
    assert.equal(st.targetAgentId, b.id);
    assert.equal((await controller.resolveInspectorState(st)).state, "target");
    assert.equal((await controller.syncTargetAgent(st, a.id)).ok, false);
    assert.equal(store.device.whichPort(), b.port);

    chosenAvailable = true;
    registrations = [a, b];
    assert.equal((await controller.syncTargetAgent(st, b.id)).ok, true);
    assert.equal((await controller.resolveInspectorState(st)).agentId, b.id);
    assert.equal((await store.tap({ id: `${b.id}-button` })).ok, true);
    assert.deepEqual(mutations, [b.id]);
  } finally {
    store?.dispose();
    await Promise.all(servers.map((server) => new Promise((resolve) => {
      server.close(resolve);
      server.closeAllConnections();
    })));
  }
});