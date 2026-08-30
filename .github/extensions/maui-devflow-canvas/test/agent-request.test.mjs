import assert from "node:assert/strict";
import test from "node:test";
import { dispatchAgentRequest } from "../agent-request.mjs";

test("direct agent requests are bounded and dispatched to the joined session", async () => {
  const sent = [];
  const session = {
    send: async (message) => {
      sent.push(message);
      return "message-1";
    },
  };
  const state = {};

  const result = await dispatchAgentRequest(session, state, "  Prepare the test  ", "Create test", {
    now: () => 1000,
  });

  assert.deepEqual(sent, [{ prompt: "Prepare the test" }]);
  assert.deepEqual(result, { ok: true, status: 'Sent "Create test" to Copilot' });
  assert.ok(state._lastAgentRequestKey);
  assert.equal(state._lastAgentRequestAt, 1000);
  assert.equal((await dispatchAgentRequest(session, state, "   ", null)).ok, false);
  assert.equal((await dispatchAgentRequest(session, state, "x".repeat(8193), null)).ok, false);
});

test("direct agent requests suppress rapid duplicates without hiding later requests", async () => {
  let sends = 0;
  let now = 1000;
  const session = { send: async () => { sends += 1; } };
  const state = {};
  const options = { now: () => now };

  assert.equal((await dispatchAgentRequest(session, state, "Diagnose run", null, options)).ok, true);
  now += 1000;
  const duplicate = await dispatchAgentRequest(session, state, "Diagnose run", null, options);
  assert.equal(duplicate.deduped, true);
  assert.equal(sends, 1);

  now += 30000;
  assert.equal((await dispatchAgentRequest(session, state, "Diagnose run", null, options)).ok, true);
  assert.equal(sends, 2);
});

test("direct agent requests fail closed when host dispatch is unavailable or unsuccessful", async () => {
  const unsupported = await dispatchAgentRequest({}, {}, "Improve test");
  assert.equal(unsupported.code, "unsupported_runtime");

  const rejected = await dispatchAgentRequest(
    { send: async () => { throw new Error("session closed"); } },
    {},
    "Improve test",
  );
  assert.match(rejected.error, /session closed/);

  const timedOut = await dispatchAgentRequest(
    { send: async () => "message" },
    {},
    "Improve test",
    null,
    { timeout: async () => ({ timedOut: true, error: "request timed out" }) },
  );
  assert.deepEqual(timedOut, { ok: false, error: "request timed out" });
});
