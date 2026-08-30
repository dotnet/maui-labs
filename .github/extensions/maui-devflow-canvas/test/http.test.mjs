import assert from "node:assert/strict";
import { PassThrough } from "node:stream";
import test from "node:test";
import { readJsonBody, selectInspectorAgent } from "../http.mjs";

function parse(body, maxBytes) {
  const req = new PassThrough();
  const result = readJsonBody(req, maxBytes);
  req.end(body);
  return result;
}

test("readJsonBody parses valid JSON and rejects malformed input", async () => {
  assert.deepEqual(await parse('{"ok":true}', 100), { ok: true, value: { ok: true } });
  assert.deepEqual(await parse("{", 100), { ok: false, status: 400, error: "invalid JSON" });
});

test("readJsonBody resolves oversized requests instead of hanging", async () => {
  assert.deepEqual(
    await parse(Buffer.from("123456789", "utf8"), 8),
    { ok: false, status: 413, error: "request body too large" },
  );
});

test("readJsonBody resolves when the request closes before end", async () => {
  const req = new PassThrough();
  const result = readJsonBody(req, 100);
  req.destroy();
  assert.deepEqual(await result, { ok: false, status: 400, error: "request closed" });
});

test("selectInspectorAgent fails closed when multiple agents cannot be resolved", () => {
  const agents = [
    { id: "first", port: 10001 },
    { id: "second", port: 10002 },
  ];

  assert.equal(selectInspectorAgent(agents), null);
  assert.equal(selectInspectorAgent(agents, 19999), null);
  assert.equal(selectInspectorAgent(agents, 10002)?.id, "second");
  assert.equal(selectInspectorAgent([agents[0]])?.id, "first");
});
