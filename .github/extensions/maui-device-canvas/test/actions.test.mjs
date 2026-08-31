import test from "node:test";
import assert from "node:assert/strict";
import { createDeviceActions } from "../actions.mjs";

const expectedNames = [
  "list_devices",
  "get_device_catalog",
  "get_selected_device",
  "select_device",
  "create_device",
  "boot_device",
  "shutdown_device",
  "restart_device",
  "reveal_device",
  "erase_device",
  "delete_device",
  "get_device",
  "get_display_geometry",
  "tap_device",
  "long_press_device",
  "swipe_device",
  "type_text",
  "press_button",
  "press_key",
  "rotate_device",
  "take_screenshot",
  "start_recording",
  "stop_recording",
  "get_recording_status",
];

test("exposes the complete 24-action device canvas inventory", () => {
  const actions = createDeviceActions(async () => ({}));

  assert.deepEqual(actions.map((action) => action.name), expectedNames);
  assert.equal(new Set(expectedNames).size, 24);
});

test("selection carries the exact canvas session and instance", async () => {
  const calls = [];
  const actions = createDeviceActions(async (args) => { calls.push(args); return {}; });
  const select = actions.find((action) => action.name === "select_device");

  await select.handler({
    sessionId: "session-1",
    instanceId: "canvas-1",
    input: { deviceId: "android:emulator:Pixel_8" },
  });

  assert.deepEqual(calls, [[
    "devices", "select",
    "--session", "session-1",
    "--instance", "canvas-1",
    "--", "android:emulator:Pixel_8",
  ]]);
});

// Every device ID here is chosen by a model, not typed by a person, so one that begins with "-" is
// a realistic input rather than a hypothetical one. Without an end-of-options marker the parser
// reads it as a flag, and the command either fails confusingly or acts on a different target.
test("model-supplied device IDs are placed behind an end-of-options marker", async () => {
  const calls = [];
  const actions = createDeviceActions(async (args) => { calls.push(args); return {}; });
  const hostile = "--session=attacker";
  const ctx = { sessionId: "session", instanceId: "instance", input: { deviceId: hostile } };

  const positionalActions = [
    "select_device", "boot_device", "shutdown_device", "restart_device", "reveal_device",
    "get_device", "get_display_geometry", "take_screenshot", "stop_recording",
    "get_recording_status",
  ];

  for (const name of positionalActions) {
    calls.length = 0;
    await actions.find((action) => action.name === name).handler(ctx);
    const args = calls[0];
    const marker = args.indexOf("--");
    assert.ok(marker >= 0, `${name} did not emit an end-of-options marker`);
    assert.deepEqual(args.slice(marker + 1), [hostile], `${name} placed the device ID wrongly`);
    // Nothing before the marker may be attacker-controlled.
    assert.equal(args.slice(0, marker).includes(hostile), false, name);
  }
});

test("input actions place the device ID after their own options and the marker", async () => {
  const calls = [];
  const actions = createDeviceActions(async (args) => { calls.push(args); return {}; });
  const hostile = "-x";

  await actions.find((action) => action.name === "tap_device").handler({
    sessionId: "s", instanceId: "i",
    input: { deviceId: hostile, x: 10, y: 20 },
  });

  assert.deepEqual(calls[0], [
    "input", "tap",
    "--x", "10",
    "--y", "20",
    "--duration", "0",
    "--session", "s",
    "--instance", "i",
    "--", hostile,
  ]);
});

// A negative coordinate would render with a leading "-" in the option-value position, where no
// end-of-options marker can help. It is off-screen anyway, so it is refused outright.
test("coordinates that would render as options are refused", () => {
  let calls = 0;
  const actions = createDeviceActions(async () => { calls++; return {}; });
  const tap = actions.find((action) => action.name === "tap_device");

  assert.throws(
    () => tap.handler({ sessionId: "s", instanceId: "i", input: { deviceId: "ios:A1B2", x: -5, y: 20 } }),
    /must be a number/,
  );
  assert.throws(
    () => tap.handler({ sessionId: "s", instanceId: "i", input: { deviceId: "ios:A1B2", x: 5, y: -20 } }),
    /must be a number/,
  );
  assert.equal(calls, 0);
});

// Typed text may legitimately begin with "-", so it cannot be validated away. It is bound to its
// option in a single token instead, which no parser can re-read as a separate flag.
test("typed text is bound to its option in one token", async () => {
  const calls = [];
  const actions = createDeviceActions(async (args) => { calls.push(args); return {}; });

  await actions.find((action) => action.name === "type_text").handler({
    sessionId: "s", instanceId: "i",
    input: { deviceId: "ios:A1B2", text: "--session=attacker" },
  });

  assert.deepEqual(calls[0], [
    "input", "type",
    "--text=--session=attacker",
    "--session", "s",
    "--instance", "i",
    "--", "ios:A1B2",
  ]);
});

// Identifiers reach argv as separate option values, where a marker cannot protect them, so a
// leading "-" is refused before the process is started.
test("identifiers that would render as options are refused", () => {
  let calls = 0;
  const actions = createDeviceActions(async () => { calls++; return {}; });
  const create = actions.find((action) => action.name === "create_device");
  const valid = { platform: "android", name: "Pixel 8", runtimeId: "android:35", deviceTypeId: "pixel_8" };

  for (const field of ["name", "runtimeId", "deviceTypeId"]) {
    assert.throws(
      () => create.handler({
        sessionId: "s", instanceId: "i",
        input: { ...valid, [field]: "--platform" },
      }),
      /cannot begin with/,
    );
  }
  assert.equal(calls, 0);
});

test("an absent or oversized device ID is refused before the process starts", () => {
  let calls = 0;
  const actions = createDeviceActions(async () => { calls++; return {}; });
  const boot = actions.find((action) => action.name === "boot_device");

  assert.throws(
    () => boot.handler({ sessionId: "s", instanceId: "i", input: {} }),
    /device ID is required/,
  );
  assert.throws(
    () => boot.handler({ sessionId: "s", instanceId: "i", input: { deviceId: "x".repeat(513) } }),
    /device ID is required/,
  );
  assert.equal(calls, 0);
});

test("agent actions cannot authorize destructive device operations", () => {
  let calls = 0;
  const actions = createDeviceActions(async () => { calls++; return {}; });

  // The canvas has no approval authority, so it refuses rather than offering a confirmation of its
  // own, and it points the operator at the CLI they already trust.
  assert.throws(
    () => actions.find((action) => action.name === "erase_device").handler({
      input: { deviceId: "ios:simulator:A1B2" },
    }),
    /cannot authorize it/i,
  );
  assert.throws(
    () => actions.find((action) => action.name === "delete_device").handler({
      input: { deviceId: "ios:simulator:A1B2" },
    }),
    /cannot authorize it/i,
  );
  assert.equal(calls, 0);
});

test("media actions do not accept arbitrary output paths", async () => {
  const calls = [];
  const actions = createDeviceActions(async (args) => { calls.push(args); return {}; });
  const ctx = {
    sessionId: "session",
    instanceId: "instance",
    input: { deviceId: "ios:simulator:A1B2", output: "C:\\untrusted\\file.png" },
  };

  await actions.find((action) => action.name === "take_screenshot").handler(ctx);
  await actions.find((action) => action.name === "start_recording").handler(ctx);

  assert.equal(calls.flat().includes("--output"), false);
});

test("device creation carries the selected platform", async () => {
  const calls = [];
  const actions = createDeviceActions(async (args) => { calls.push(args); return {}; });

  await actions.find((action) => action.name === "create_device").handler({
    sessionId: "session",
    instanceId: "instance",
    input: {
      platform: "android",
      name: "Pixel 8",
      runtimeId: "android:35",
      deviceTypeId: "pixel_8",
    },
  });

  assert.deepEqual(calls[0].slice(0, 5), [
    "devices", "create", "--platform", "android", "--name",
  ]);
});
