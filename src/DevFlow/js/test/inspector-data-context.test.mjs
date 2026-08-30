import assert from "node:assert/strict";
import test from "node:test";

import {
  createDataSnapshot,
  isSecretContextKey,
  supportsDataContextScope,
} from "../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/inspector-data-context.js";

const agent = { id: "agent", appName: "TestApp", platform: "Windows", port: 9223 };

test("data snapshots redact nested secrets and URL credentials", () => {
  const snapshot = createDataSnapshot({
    scope: "network",
    title: "Requests",
    payload: {
      apiToken: "top-level-secret",
      nested: { password: "nested-secret" },
      authorization: "Bearer abcdefghijklmnop",
      url: "https://user:password@example.test/path?sig=url-secret#fragment-secret",
      encodedUrl: encodeURIComponent("https://safe.test/path?token=encoded-secret"),
    },
    itemCount: 1,
    metadata: { cookie: "metadata-secret" },
    agent,
  });

  const serialized = JSON.stringify(snapshot);
  for (const secret of [
    "top-level-secret",
    "nested-secret",
    "abcdefghijklmnop",
    "user:password",
    "url-secret",
    "fragment-secret",
    "encoded-secret",
    "metadata-secret",
  ]) {
    assert.doesNotMatch(serialized, new RegExp(secret));
  }
  assert.match(serialized, /redacted/i);
  assert.equal(snapshot.redacted, true);
});

test("redaction remains stable across repeated snapshots", () => {
  for (let index = 0; index < 100; index++) {
    const marker = `unique-secret-${index}`;
    const snapshot = createDataSnapshot({
      scope: "logs",
      title: "Logs",
      payload: {
        apiKey: marker,
        message: `Authorization: Bearer abcdefghijklmnop; token=${marker}`,
      },
      itemCount: 1,
      metadata: {},
      agent,
    });
    const serialized = JSON.stringify(snapshot);
    assert.doesNotMatch(serialized, new RegExp(marker));
    assert.doesNotMatch(serialized, /abcdefghijklmnop/);
  }
});

test("data snapshots stay within the host envelope limit", () => {
  const snapshot = createDataSnapshot({
    scope: "logs",
    title: "Large logs " + "🚨".repeat(1000),
    payload: Array.from({ length: 300 }, (_, index) => ({
      index,
      message: "日志🚨".repeat(1500),
    })),
    itemCount: 300,
    metadata: { note: "y".repeat(3000) },
    agent: { ...agent, appName: "应用🚨".repeat(2000) },
  });

  assert.equal(snapshot.truncated, true);
  assert.ok(Buffer.byteLength(JSON.stringify(snapshot), "utf8") <= 18000);
  assert.ok(snapshot.appName === null || Buffer.byteLength(snapshot.appName, "utf8") <= 512);
  assert.equal(snapshot.appName, snapshot.agent?.appName ?? null);
});

test("scope and secret-key helpers expose the supported contract", () => {
  assert.equal(supportsDataContextScope("network"), true);
  assert.equal(supportsDataContextScope("layout"), true);
  assert.equal(supportsDataContextScope("alerts"), true);
  assert.equal(supportsDataContextScope("secureStorage"), false);
  assert.equal(isSecretContextKey("apiToken"), true);
  assert.equal(isSecretContextKey("displayName"), false);
});