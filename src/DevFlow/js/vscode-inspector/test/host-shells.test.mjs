import assert from "node:assert/strict";
import test from "node:test";
import {
  createReconnectController,
  inspectorTitle,
  reconnectDiscoveryAction,
  renderReconnectHost,
  sameAgentIdentity,
} from "../dist-test/host-shells.js";

test("Inspector titles retain the product name while identifying an app", () => {
  assert.equal(inspectorTitle(), "MAUI DevFlow Inspector");
  assert.equal(inspectorTitle(" MauiTodo "), "MAUI DevFlow Inspector · MauiTodo");
});

test("Reconnect discovery waits, connects uniquely, or asks before choosing", () => {
  assert.equal(reconnectDiscoveryAction(0, false), "wait");
  assert.equal(reconnectDiscoveryAction(1, false), "connect");
  assert.equal(reconnectDiscoveryAction(2, false), "wait");
  assert.equal(reconnectDiscoveryAction(2, true), "choose");
});

test("Stable agent identity ignores process-specific ids and ports", () => {
  const before = {
    id: "old",
    port: 9001,
    project: "C:\\src\\App.csproj",
    tfm: "net10.0-windows10.0.19041.0",
    platform: "Windows",
    appName: "MauiTodo",
    sessionId: "worktree-a",
  };
  const after = { ...before, id: "new", port: 9011, project: "C:/src/App.csproj" };

  assert.equal(sameAgentIdentity(before, after), true);
  assert.equal(sameAgentIdentity(before, { ...after, appName: "Other" }), false);
  assert.equal(sameAgentIdentity(before, { ...after, sessionId: "worktree-b" }), false);
  assert.equal(sameAgentIdentity(before, { ...after, project: "C:\\other\\App.csproj" }), false);
});

test("Relative project identities cannot identify a replacement process across worktrees", () => {
  for (const project of ["DevFlow.Sample.csproj", "samples/DevFlow.Sample.csproj", "C:DevFlow.Sample.csproj"]) {
    for (const sessionId of ["dwampledevflowsamplecsproj", undefined]) {
      const selected = { project, tfm: "net10.0", platform: "Windows", appName: "MauiTodo", sessionId };
      assert.equal(sameAgentIdentity(selected, { ...selected }), false);
    }
  }
});

test("Reconnect host distinguishes broker, app, and multiple-app states", () => {
  const broker = renderReconnectHost("broker", "nonce", "bridge");
  const app = renderReconnectHost("app", "nonce", "bridge");
  const multiple = renderReconnectHost("multiple", "nonce", "bridge");
  const target = renderReconnectHost("target", "nonce", "bridge");

  assert.match(broker, /Waiting for the DevFlow broker/);
  assert.match(app, /Waiting for a running MAUI app/);
  assert.match(multiple, /Choose a running app/);
  assert.match(target, /Waiting for the selected MAUI app/);
  assert.match(multiple, /id="choose"/);
  assert.match(target, /Choose another app/);
  assert.match(multiple, /id="retry"/);
  assert.match(multiple, /bridgeId: bridgeId/);
  assert.match(multiple, /Checking…/);
  assert.doesNotMatch(multiple, /setInterval\(poll/);
});

test("Reconnect controller does not mutate a disposed panel after discovery", async () => {
  let finishDiscovery;
  let disposed = false;
  const applied = [];
  const controller = createReconnectController({
    discover: () => new Promise((resolve) => { finishDiscovery = resolve; }),
    pickAgent: async () => undefined,
    targetAgent: () => undefined,
    matchesTarget: (candidate, target) => candidate.id === target.id,
    applyConnected: () => applied.push("connected"),
    applyReconnect: (state) => applied.push(state),
    isDisposed: () => disposed,
  });

  const poll = controller.poll();
  disposed = true;
  finishDiscovery({ port: 19223, agents: [{ id: "app" }] });
  await poll;

  assert.deepEqual(applied, []);
});

test("Reconnect controller refreshes a selected app before connecting", async () => {
  let discoveryCount = 0;
  const connected = [];
  const selected = { id: "a", app: "todo", port: 9001 };
  const controller = createReconnectController({
    discover: async () => {
      discoveryCount++;
      return discoveryCount === 1
        ? { port: 19223, agents: [selected, { id: "b", app: "other", port: 9002 }] }
        : { port: 19224, agents: [{ id: "a-new", app: "todo", port: 9011 }, { id: "b", app: "other", port: 9012 }] };
    },
    pickAgent: async () => selected,
    targetAgent: () => undefined,
    matchesTarget: (candidate, target) => candidate.app === target.app,
    applyConnected: (discovery, agent) => connected.push([discovery.port, agent.port]),
    applyReconnect: () => {},
    isDisposed: () => false,
  });

  await controller.poll({ choose: true });

  assert.equal(discoveryCount, 2);
  assert.deepEqual(connected, [[19224, 9011]]);
});

test("Reconnect controller does not use a different app when a picked app exits", async () => {
  let discoveryCount = 0;
  const selected = { id: "a", app: "todo" };
  const connected = [];
  const states = [];
  const controller = createReconnectController({
    discover: async () => {
      discoveryCount++;
      return discoveryCount === 1
        ? { port: 19223, agents: [selected, { id: "b", app: "weather" }] }
        : { port: 19223, agents: [{ id: "b", app: "weather" }] };
    },
    pickAgent: async () => selected,
    targetAgent: () => undefined,
    matchesTarget: (candidate, target) => candidate.app === target.app,
    applyConnected: (_discovery, agent) => connected.push(agent.id),
    applyReconnect: (state) => states.push(state),
    isDisposed: () => false,
  });

  await controller.poll({ choose: true });
  await controller.poll();
  await controller.poll();

  assert.deepEqual(connected, []);
  assert.deepEqual(states, ["target", "target", "target"]);
});

test("A failed new choice does not reconnect the previously active app", async () => {
  const previous = { id: "previous", app: "weather" };
  const selected = { id: "selected", app: "todo" };
  let discoveryCount = 0;
  let connected = true;
  const states = [];
  const connections = [];
  const controller = createReconnectController({
    discover: async () => ({
      port: 19223,
      agents: ++discoveryCount === 1 ? [previous, selected] : [previous],
    }),
    pickAgent: async () => selected,
    targetAgent: () => previous,
    matchesTarget: (candidate, target) => candidate.app === target.app,
    applyConnected: (_discovery, agent) => connections.push(agent.id),
    applyReconnect: (state) => { connected = false; states.push(state); },
    isConnected: () => connected,
    isDisposed: () => false,
  });

  await controller.poll({ choose: true });
  assert.equal(connected, false);
  await controller.poll();

  assert.deepEqual(connections, []);
  assert.deepEqual(states, ["target", "target"]);
});

test("A picked target survives a broker failure and connects when it returns", async () => {
  const selected = { id: "selected", app: "todo" };
  const other = { id: "other", app: "weather" };
  let discoveryCount = 0;
  const connections = [];
  const controller = createReconnectController({
    discover: async () => {
      discoveryCount++;
      if (discoveryCount === 2) throw new Error("Broker restarting");
      return { port: 19223, agents: discoveryCount === 3 ? [other] : [selected, other] };
    },
    pickAgent: async () => selected,
    targetAgent: () => undefined,
    matchesTarget: (candidate, target) => candidate.app === target.app,
    applyConnected: (_discovery, agent) => connections.push(agent.id),
    applyReconnect: () => {},
    isDisposed: () => false,
  });

  await controller.poll({ choose: true });
  await controller.poll();
  assert.deepEqual(connections, []);
  await controller.poll();
  assert.deepEqual(connections, ["selected"]);
});

test("A relative-project target reconnects by exact id but requires a choice for a new process", async () => {
  const selected = {
    id: "selected",
    project: "DevFlow.Sample.csproj",
    sessionId: "dwampledevflowsamplecsproj",
    appName: "MauiTodo",
    platform: "Windows",
    tfm: "net10.0",
  };
  const other = { ...selected, id: "other-worktree" };
  let agents = [selected];
  const connections = [];
  const states = [];
  const controller = createReconnectController({
    discover: async () => ({ port: 19223, agents }),
    pickAgent: async () => other,
    targetAgent: () => selected,
    matchesTarget: sameAgentIdentity,
    applyConnected: (_discovery, agent) => connections.push(agent.id),
    applyReconnect: (state) => states.push(state),
    isDisposed: () => false,
  });

  await controller.poll();
  agents = [other];
  await controller.poll();
  await controller.poll();
  assert.deepEqual(connections, ["selected"]);
  assert.deepEqual(states, ["target", "target"]);

  await controller.poll({ choose: true });
  await controller.poll();
  assert.deepEqual(connections, ["selected", "other-worktree", "other-worktree"]);
});

test("Reconnect controller queues an explicit retry behind an active poll", async () => {
  let finishFirstDiscovery;
  let discoveryCount = 0;
  const controller = createReconnectController({
    discover: () => {
      discoveryCount++;
      if (discoveryCount === 1)
        return new Promise((resolve) => { finishFirstDiscovery = resolve; });
      return Promise.resolve(null);
    },
    pickAgent: async () => undefined,
    targetAgent: () => undefined,
    matchesTarget: (candidate, target) => candidate.id === target.id,
    applyConnected: () => {},
    applyReconnect: () => {},
    isDisposed: () => false,
  });

  const first = controller.poll();
  await controller.poll({ queueIfBusy: true });
  finishFirstDiscovery(null);
  await first;

  assert.equal(discoveryCount, 2);
});

test("Reconnect controller keeps a connected panel through one transient miss", async () => {
  const states = [];
  const controller = createReconnectController({
    discover: async () => null,
    pickAgent: async () => undefined,
    targetAgent: () => ({ id: "app" }),
    matchesTarget: (candidate, target) => candidate.id === target.id,
    applyConnected: () => {},
    applyReconnect: (state) => states.push(state),
    isConnected: () => true,
    isDisposed: () => false,
  });

  await controller.poll();
  assert.deepEqual(states, []);

  await controller.poll();
  assert.deepEqual(states, ["broker"]);
});

test("Reconnect controller never substitutes an unrelated sole app for the selected target", async () => {
  const target = { id: "old", app: "todo" };
  const connected = [];
  const states = [];
  const controller = createReconnectController({
    discover: async () => ({ port: 19223, agents: [{ id: "other", app: "weather" }] }),
    pickAgent: async () => undefined,
    targetAgent: () => target,
    matchesTarget: (candidate, selected) => candidate.app === selected.app,
    applyConnected: (_discovery, agent) => connected.push(agent.id),
    applyReconnect: (state) => states.push(state),
    isDisposed: () => false,
  });

  await controller.poll();

  assert.deepEqual(connected, []);
  assert.deepEqual(states, ["target"]);
});

test("Reconnect controller changes targets only after an explicit choice", async () => {
  const target = { id: "old", app: "todo" };
  const other = { id: "other", app: "weather" };
  const connected = [];
  const controller = createReconnectController({
    discover: async () => ({ port: 19223, agents: [other] }),
    pickAgent: async (agents) => agents[0],
    targetAgent: () => target,
    matchesTarget: (candidate, selected) => candidate.app === selected.app,
    applyConnected: (_discovery, agent) => connected.push(agent.id),
    applyReconnect: () => {},
    isDisposed: () => false,
  });

  await controller.poll({ choose: true });

  assert.deepEqual(connected, ["other"]);
});
