import { test } from "node:test";
import assert from "node:assert/strict";
import http from "node:http";
import { fetchAgents, isAgentRegistration } from "../src/broker.js";

const validAgent = {
  id: "agent-1",
  project: "C:\\src\\App.csproj",
  tfm: "net10.0-windows10.0.19041.0",
  platform: "WinUI",
  appName: "App",
  port: 9223,
};

test("fetchAgents keeps the literal loopback socket and Host header aligned", async (t) => {
  let expectedHost = "";
  const server = http.createServer((request, response) => {
    if (request.headers.host !== expectedHost) {
      response.writeHead(404);
      response.end();
      return;
    }

    response.setHeader("Content-Type", "application/json");
    response.end(JSON.stringify([validAgent]));
  });

  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  t.after(() => server.close());

  const address = server.address();
  assert.ok(address && typeof address !== "string");
  expectedHost = `127.0.0.1:${address.port}`;

  const agents = await fetchAgents(address.port);
  assert.deepEqual(agents, [validAgent]);
});

test("isAgentRegistration validates required and optional broker fields", () => {
  assert.equal(isAgentRegistration(validAgent), true);
  assert.equal(isAgentRegistration({ ...validAgent, version: null, processId: null }), true);
  assert.equal(isAgentRegistration({ ...validAgent, id: "" }), false);
  assert.equal(isAgentRegistration({ ...validAgent, project: 42 }), false);
  assert.equal(isAgentRegistration({ ...validAgent, port: 0 }), false);
  assert.equal(isAgentRegistration({ ...validAgent, port: 65536 }), false);
  assert.equal(isAgentRegistration({ ...validAgent, port: 9223.5 }), false);
  assert.equal(isAgentRegistration({ ...validAgent, port: "9223" }), false);
  assert.equal(isAgentRegistration({ ...validAgent, version: 1 }), false);
  assert.equal(isAgentRegistration({ ...validAgent, processId: -1 }), false);
  assert.equal(isAgentRegistration({ ...validAgent, connectedAt: {} }), false);
});

test("fetchAgents rejects a registry containing malformed entries", async (t) => {
  const server = http.createServer((_request, response) => {
    response.setHeader("Content-Type", "application/json");
    response.end(JSON.stringify([validAgent, { ...validAgent, port: 70000 }]));
  });

  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  t.after(() => server.close());

  const address = server.address();
  assert.ok(address && typeof address !== "string");

  assert.equal(await fetchAgents(address.port), null);
});
