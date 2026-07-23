import { test } from "node:test";
import assert from "node:assert/strict";
import { isConnError, parseJsonSafe } from "../src/http.js";

test("isConnError: genuine socket failures are true", () => {
  assert.equal(isConnError({ status: 0, error: "ECONNREFUSED" }), true);
  assert.equal(isConnError({ status: 0, error: "ECONNRESET" }), true);
  assert.equal(isConnError({ status: 0, error: "socket hang up" }), true);
  assert.equal(isConnError({ status: 0, error: "ETIMEDOUT" }), true);
});

test("isConnError: request timeout against a live socket is NOT a conn error", () => {
  // "timeout" (our httpRaw timeout) must not trigger a re-resolve.
  assert.equal(isConnError({ status: 0, error: "timeout" }), false);
});

test("isConnError: HTTP errors and success are not conn errors", () => {
  assert.equal(isConnError({ status: 500 }), false);
  assert.equal(isConnError({ status: 404, error: undefined }), false);
  assert.equal(isConnError({ status: 200 }), false);
  assert.equal(isConnError(null), false);
  assert.equal(isConnError(undefined), false);
});

test("parseJsonSafe: valid JSON", () => {
  assert.deepEqual(parseJsonSafe('{"a":1}'), { a: 1 });
  assert.deepEqual(parseJsonSafe("[1,2,3]"), [1, 2, 3]);
});

test("parseJsonSafe: salvages JSON with a stray preamble", () => {
  assert.deepEqual(parseJsonSafe('warning: something\n{"a":1}'), { a: 1 });
});

test("parseJsonSafe: empty / garbage → null", () => {
  assert.equal(parseJsonSafe(""), null);
  assert.equal(parseJsonSafe("   "), null);
  assert.equal(parseJsonSafe("not json at all"), null);
});
