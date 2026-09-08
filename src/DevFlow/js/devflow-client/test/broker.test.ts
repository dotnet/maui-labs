import { test } from "node:test";
import assert from "node:assert/strict";
import http from "node:http";
import { fetchAgents } from "../src/broker.js";

test("fetchAgents keeps the literal loopback socket and Host header aligned", async (t) => {
  let expectedHost = "";
  const server = http.createServer((request, response) => {
    if (request.headers.host !== expectedHost) {
      response.writeHead(404);
      response.end();
      return;
    }

    response.setHeader("Content-Type", "application/json");
    response.end(JSON.stringify([{ id: "agent-1", port: 9223 }]));
  });

  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  t.after(() => server.close());

  const address = server.address();
  assert.ok(address && typeof address !== "string");
  expectedHost = `127.0.0.1:${address.port}`;

  const agents = await fetchAgents(address.port);
  assert.deepEqual(agents, [{ id: "agent-1", port: 9223 }]);
});
