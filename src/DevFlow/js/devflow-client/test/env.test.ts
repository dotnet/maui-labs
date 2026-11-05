import { test } from "node:test";
import assert from "node:assert/strict";
import { optionsFromEnv, parsePort } from "../src/env.js";

test("parsePort", () => {
  assert.equal(parsePort("9223"), 9223);
  assert.equal(parsePort(" 10223 "), 10223);
  assert.equal(parsePort("0"), undefined);
  assert.equal(parsePort("-1"), undefined);
  assert.equal(parsePort("70000"), undefined);
  assert.equal(parsePort("abc"), undefined);
  assert.equal(parsePort(undefined), undefined);
  assert.equal(parsePort(null), undefined);
});

test("optionsFromEnv: maps the public env contract, only present keys", () => {
  const opts = optionsFromEnv({
    MAUI_DEVFLOW_PLATFORM: "android",
    MAUI_DEVFLOW_DEVICE: "emulator-5554",
    MAUI_DEVFLOW_AGENT_PORT: "10223",
    MAUI_DEVFLOW_PROJECT_ROOT: "D:/apps/foo",
    MAUI_CLI: "C:/tools/maui.exe",
    ADB: "C:/sdk/adb.exe",
  } as NodeJS.ProcessEnv);
  assert.deepEqual(opts, {
    platform: "android",
    device: "emulator-5554",
    agentPort: 10223,
    projectRoot: "D:/apps/foo",
    mauiCliPath: "C:/tools/maui.exe",
    adbPath: "C:/sdk/adb.exe",
  });
});

test("optionsFromEnv: empty env → empty options", () => {
  assert.deepEqual(optionsFromEnv({} as NodeJS.ProcessEnv), {});
});

test("optionsFromEnv: invalid port is dropped", () => {
  const opts = optionsFromEnv({ MAUI_DEVFLOW_AGENT_PORT: "nope" } as NodeJS.ProcessEnv);
  assert.equal(opts.agentPort, undefined);
});
