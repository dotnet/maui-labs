import { execFile } from "node:child_process";
import { AsyncLocalStorage } from "node:async_hooks";
import { promisify } from "node:util";
import { createCanvas, joinSession } from "@github/copilot-sdk/extension";
import { createDeviceActions } from "./actions.mjs";
import { DeviceLeaseClient } from "./lease.mjs";
import { withJson } from "./lib/argv.mjs";
import { resolveCommand } from "./lib/runtime.mjs";

const execFileAsync = promisify(execFile);
const mutationContext = new AsyncLocalStorage();

async function command() {
  return (await resolveCommand()).command;
}

async function runCli(args) {
  try {
    const signal = mutationContext.getStore()?.signal;
    const { stdout } = await execFileAsync(await command(), withJson(args.map(String)), {
      encoding: "utf8",
      maxBuffer: 8 * 1024 * 1024,
      timeout: 120_000,
      windowsHide: true,
      signal,
    });
    return JSON.parse(stdout);
  } catch (error) {
    let message = String(error?.stderr || error?.message || error).trim();
    if (message.length > 4096) message = message.slice(0, 4096);
    const failure = new Error(`Mobile Canvas: ${message || "the companion command failed"}`);
    failure.unknownCompletion = error?.killed === true || !!error?.signal;
    throw failure;
  }
}

function contextArgs(ctx) {
  return ["--session", String(ctx.sessionId), "--instance", String(ctx.instanceId)];
}

const leaseClient = new DeviceLeaseClient();
const mutatingActions = new Set([
  "create_device", "boot_device", "shutdown_device", "restart_device", "reveal_device",
  "tap_device", "long_press_device", "swipe_device", "type_text", "press_button",
  "press_key", "rotate_device", "start_recording", "stop_recording",
]);
const actions = createDeviceActions(runCli).map((action) => {
  if (!mutatingActions.has(action.name)) return action;
  const handler = action.handler;
  return {
    ...action,
    handler: (ctx) => leaseClient.run(
      ctx,
      action.name === "create_device"
        ? { catalog: true }
        : { deviceId: ctx.input?.deviceId },
      (signal) => mutationContext.run({ signal }, () => handler(ctx)),
    ),
  };
});

const canvas = createCanvas({
  id: "maui-mobile-device",
  displayName: "MAUI Mobile Device",
  description:
    "A standalone view of local iOS simulators and Android emulators. It can create, boot, " +
    "select, inspect, and drive a device before a MAUI app starts or after it exits. " +
    "When an app is attached, MAUI DevFlow Inspector also exposes these controls beside semantic " +
    "in-app inspection and workflow authoring.",
  inputSchema: {
    type: "object",
    properties: {
      deviceId: {
        type: "string",
        maxLength: 512,
        description: "Optional provider-qualified device ID to select after opening.",
      },
    },
  },
  actions,
  open: async (ctx) => {
    const result = await runCli(["canvas", "open", ...contextArgs(ctx)]);
    if (ctx.input?.deviceId) {
      // The canvas input is model-supplied too, so it goes behind the end-of-options marker just
      // like every action argument.
      await runCli([
        "devices", "select",
        ...contextArgs(ctx),
        "--", String(ctx.input.deviceId),
      ]);
    }
    return {
      title: result.title || "MAUI Mobile Device",
      url: result.url,
      status: "Connected to the pinned local Mobile Canvas companion",
    };
  },
  onClose: async (ctx) => {
    await runCli(["canvas", "close", ...contextArgs(ctx)]);
  },
});

await joinSession({ canvases: [canvas] });
