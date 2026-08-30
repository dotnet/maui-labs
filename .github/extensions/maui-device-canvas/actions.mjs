// Every value below arrives from a model. Nothing here is typed by a person, so an argument that
// begins with "-" is a real possibility rather than a theoretical one — and in every mainstream
// argument parser that token is read as an option, not as the value it was meant to be.
//
// Two rules keep that impossible:
//
//   1. Positional values (always a device ID) are placed last, after a "--" end-of-options marker.
//      Everything after "--" is a positional argument by definition, whatever it starts with.
//   2. Option values that must never look like an option are validated to reject a leading "-",
//      and free text — where a leading "-" is legitimate content — is passed in the unambiguous
//      "--option=value" form instead of as a separate token.
//
// The ordering is what makes rule 1 usable: "--" ends option parsing, so the canvas's own
// --session/--instance flags have to precede it, not follow the model's value.

const MAX_DEVICE_ID = 512;

function contextArgs(ctx) {
  return ["--session", String(ctx.sessionId), "--instance", String(ctx.instanceId)];
}

/** Places model-supplied positional values behind an end-of-options marker. */
function positional(options, ...values) {
  return [...options, "--", ...values.map(String)];
}

function deviceIdSchema(description = "Provider-qualified device ID returned by list_devices.") {
  return { type: "string", maxLength: MAX_DEVICE_ID, description };
}

/** A device ID reaches argv as a positional, so only its shape needs checking. */
function deviceId(value) {
  const id = typeof value === "string" ? value.trim() : "";
  if (!id || id.length > MAX_DEVICE_ID) {
    throw new Error("A device ID is required. Call list_devices to find one.");
  }
  return id;
}

/**
 * An identifier that reaches argv as a separate option value. A leading "-" is never legitimate
 * content for one, so it is refused rather than smuggled past the parser.
 */
function identifier(value, label, maxLength) {
  const text = typeof value === "string" ? value.trim() : "";
  if (!text || text.length > maxLength) {
    throw new Error(`A ${label} between 1 and ${maxLength} characters is required.`);
  }
  if (text.startsWith("-")) {
    throw new Error(`A ${label} cannot begin with "-", because a command-line parser reads it as an option.`);
  }
  return text;
}

/**
 * A number that reaches argv as a separate option value. Negative values would render with a
 * leading "-", so the range is enforced here rather than trusted from the schema.
 */
function positiveNumber(value, label, { min = 0, max, fallback } = {}) {
  const parsed = value === undefined || value === null ? fallback : Number(value);
  if (!Number.isFinite(parsed) || parsed < min || (max !== undefined && parsed > max)) {
    throw new Error(`${label} must be a number between ${min} and ${max ?? "the supported maximum"}.`);
  }
  return String(parsed);
}

/** A value chosen from a closed set can never look like an option, but it is still pinned here. */
function oneOf(value, allowed, label) {
  if (!allowed.includes(value)) {
    throw new Error(`${label} must be one of: ${allowed.join(", ")}.`);
  }
  return value;
}

/**
 * Free text is the one value where a leading "-" is real content, so it cannot be validated away.
 * "--option=value" binds it to its option in a single token, which no parser can re-read as a
 * separate flag.
 */
function inlineOption(name, value, maxLength) {
  const text = typeof value === "string" ? value : "";
  if (text.length === 0 || text.length > maxLength) {
    throw new Error(`Text between 1 and ${maxLength} characters is required.`);
  }
  return `${name}=${text}`;
}

const BUTTONS = [
  "home", "lock", "side-button", "siri", "apple-pay",
  "back", "apps", "power", "volume-up", "volume-down", "menu",
];

const ORIENTATIONS = [
  "portrait", "portrait-upside-down", "landscape-left", "landscape-right",
];

function targetAction(runCli, name, description, verb) {
  return {
    name,
    description,
    inputSchema: {
      type: "object",
      properties: { deviceId: deviceIdSchema() },
      required: ["deviceId"],
    },
    handler: (ctx) => runCli(positional(
      ["devices", verb, ...contextArgs(ctx)],
      deviceId(ctx.input.deviceId),
    )),
  };
}

export function createDeviceActions(runCli) {
  return [
    {
      name: "list_devices",
      description:
        "List local iOS simulators and Android emulators with state, capabilities, and deployment IDs.",
      handler: () => runCli(["devices", "list"]),
    },
    {
      name: "get_device_catalog",
      description:
        "Get installed runtimes/system images, device types, existing devices, and dependency diagnostics.",
      handler: () => runCli(["devices", "catalog"]),
    },
    {
      name: "get_selected_device",
      description: "Get the target selected in this canvas.",
      handler: (ctx) => runCli(["devices", "selected", ...contextArgs(ctx)]),
    },
    {
      name: "select_device",
      description: "Select a device in this canvas and return its complete target record.",
      inputSchema: {
        type: "object",
        properties: { deviceId: deviceIdSchema() },
        required: ["deviceId"],
      },
      handler: (ctx) => runCli(positional(
        ["devices", "select", ...contextArgs(ctx)],
        deviceId(ctx.input.deviceId),
      )),
    },
    {
      name: "create_device",
      description:
        "Create an iOS simulator or Android emulator from an installed runtime and device type.",
      inputSchema: {
        type: "object",
        properties: {
          platform: { type: "string", enum: ["ios", "android"] },
          name: { type: "string", minLength: 1, maxLength: 128, pattern: "^[^-]" },
          runtimeId: { type: "string", minLength: 1, maxLength: 512, pattern: "^[^-]" },
          deviceTypeId: { type: "string", minLength: 1, maxLength: 512, pattern: "^[^-]" },
        },
        required: ["platform", "name", "runtimeId", "deviceTypeId"],
      },
      handler: (ctx) => runCli([
        "devices", "create",
        "--platform", oneOf(ctx.input.platform, ["ios", "android"], "platform"),
        "--name", identifier(ctx.input.name, "device name", 128),
        "--runtime", identifier(ctx.input.runtimeId, "runtime ID", 512),
        "--device-type", identifier(ctx.input.deviceTypeId, "device type ID", 512),
        ...contextArgs(ctx),
      ]),
    },
    targetAction(
      runCli,
      "boot_device",
      "Boot a shut-down device and wait for it to finish starting.",
      "boot",
    ),
    targetAction(
      runCli,
      "shutdown_device",
      "Shut down a booted device without deleting it.",
      "shutdown",
    ),
    targetAction(
      runCli,
      "restart_device",
      "Restart a device while preserving its contents.",
      "restart",
    ),
    targetAction(
      runCli,
      "reveal_device",
      "Bring the device's native window to the front.",
      "reveal",
    ),
    {
      name: "erase_device",
      description:
        "Erase device content. Refused here: this canvas has no approval authority, so an irreversible device operation is never performed from a model request.",
      inputSchema: {
        type: "object",
        properties: { deviceId: deviceIdSchema() },
        required: ["deviceId"],
      },
      handler: () => {
        throw new Error('Device erasure is irreversible and this canvas cannot authorize it. Run "maui devflow devices erase <deviceId> --confirm" yourself.');
      },
    },
    {
      name: "delete_device",
      description:
        "Delete a device. Refused here: this canvas has no approval authority, so an irreversible device operation is never performed from a model request.",
      inputSchema: {
        type: "object",
        properties: { deviceId: deviceIdSchema() },
        required: ["deviceId"],
      },
      handler: () => {
        throw new Error('Device deletion is irreversible and this canvas cannot authorize it. Run "maui devflow devices delete <deviceId> --confirm" yourself.');
      },
    },
    {
      name: "get_device",
      description: "Get one device's target record, state, display geometry, and capabilities.",
      inputSchema: {
        type: "object",
        properties: { deviceId: deviceIdSchema() },
        required: ["deviceId"],
      },
      handler: (ctx) => runCli(positional(["devices", "get"], deviceId(ctx.input.deviceId))),
    },
    {
      name: "get_display_geometry",
      description:
        "Get a booted device's logical point size, pixel size, scale, and orientation.",
      inputSchema: {
        type: "object",
        properties: { deviceId: deviceIdSchema() },
        required: ["deviceId"],
      },
      handler: (ctx) => runCli(positional(["devices", "display"], deviceId(ctx.input.deviceId))),
    },
    {
      name: "tap_device",
      description: "Tap a booted device at logical point coordinates.",
      inputSchema: {
        type: "object",
        properties: {
          deviceId: deviceIdSchema(),
          x: { type: "number", minimum: 0 },
          y: { type: "number", minimum: 0 },
          duration: { type: "number", minimum: 0, maximum: 60 },
        },
        required: ["deviceId", "x", "y"],
      },
      handler: (ctx) => runCli(positional(
        [
          "input", "tap",
          "--x", positiveNumber(ctx.input.x, "x"),
          "--y", positiveNumber(ctx.input.y, "y"),
          "--duration", positiveNumber(ctx.input.duration, "duration", { max: 60, fallback: 0 }),
          ...contextArgs(ctx),
        ],
        deviceId(ctx.input.deviceId),
      )),
    },
    {
      name: "long_press_device",
      description: "Press and hold a booted device at logical point coordinates.",
      inputSchema: {
        type: "object",
        properties: {
          deviceId: deviceIdSchema(),
          x: { type: "number", minimum: 0 },
          y: { type: "number", minimum: 0 },
          duration: { type: "number", minimum: 0.1, maximum: 60 },
        },
        required: ["deviceId", "x", "y"],
      },
      handler: (ctx) => runCli(positional(
        [
          "input", "tap",
          "--x", positiveNumber(ctx.input.x, "x"),
          "--y", positiveNumber(ctx.input.y, "y"),
          "--duration", positiveNumber(ctx.input.duration, "duration", { min: 0.1, max: 60, fallback: 1 }),
          ...contextArgs(ctx),
        ],
        deviceId(ctx.input.deviceId),
      )),
    },
    {
      name: "swipe_device",
      description: "Swipe or drag across a booted device in logical point coordinates.",
      inputSchema: {
        type: "object",
        properties: {
          deviceId: deviceIdSchema(),
          startX: { type: "number", minimum: 0 },
          startY: { type: "number", minimum: 0 },
          endX: { type: "number", minimum: 0 },
          endY: { type: "number", minimum: 0 },
          duration: { type: "number", minimum: 0.01, maximum: 60 },
        },
        required: ["deviceId", "startX", "startY", "endX", "endY"],
      },
      handler: (ctx) => runCli(positional(
        [
          "input", "swipe",
          "--start-x", positiveNumber(ctx.input.startX, "startX"),
          "--start-y", positiveNumber(ctx.input.startY, "startY"),
          "--end-x", positiveNumber(ctx.input.endX, "endX"),
          "--end-y", positiveNumber(ctx.input.endY, "endY"),
          "--duration", positiveNumber(ctx.input.duration, "duration", { min: 0.01, max: 60, fallback: 0.35 }),
          ...contextArgs(ctx),
        ],
        deviceId(ctx.input.deviceId),
      )),
    },
    {
      name: "type_text",
      description: "Type text into the focused control on a booted device.",
      inputSchema: {
        type: "object",
        properties: {
          deviceId: deviceIdSchema(),
          text: { type: "string", minLength: 1, maxLength: 8192 },
        },
        required: ["deviceId", "text"],
      },
      handler: (ctx) => runCli(positional(
        [
          "input", "type",
          // Typed text may legitimately start with "-", so it is bound to its option in one token
          // rather than validated away.
          inlineOption("--text", ctx.input.text, 8192),
          ...contextArgs(ctx),
        ],
        deviceId(ctx.input.deviceId),
      )),
    },
    {
      name: "press_button",
      description: "Press a supported hardware or system-navigation button.",
      inputSchema: {
        type: "object",
        properties: {
          deviceId: deviceIdSchema(),
          button: { type: "string", enum: BUTTONS },
        },
        required: ["deviceId", "button"],
      },
      handler: (ctx) => runCli(positional(
        [
          "input", "button",
          "--button", oneOf(ctx.input.button, BUTTONS, "button"),
          ...contextArgs(ctx),
        ],
        deviceId(ctx.input.deviceId),
      )),
    },
    {
      name: "press_key",
      description: "Press one keyboard key using its USB HID usage code.",
      inputSchema: {
        type: "object",
        properties: {
          deviceId: deviceIdSchema(),
          keyCode: { type: "integer", minimum: 0, maximum: 65535 },
        },
        required: ["deviceId", "keyCode"],
      },
      handler: (ctx) => runCli(positional(
        [
          "input", "key",
          "--code", positiveNumber(ctx.input.keyCode, "keyCode", { max: 65535 }),
          ...contextArgs(ctx),
        ],
        deviceId(ctx.input.deviceId),
      )),
    },
    {
      name: "rotate_device",
      description: "Rotate a booted device to a new orientation.",
      inputSchema: {
        type: "object",
        properties: {
          deviceId: deviceIdSchema(),
          orientation: { type: "string", enum: ORIENTATIONS },
        },
        required: ["deviceId", "orientation"],
      },
      handler: (ctx) => runCli(positional(
        [
          "input", "rotate",
          "--orientation", oneOf(ctx.input.orientation, ORIENTATIONS, "orientation"),
          ...contextArgs(ctx),
        ],
        deviceId(ctx.input.deviceId),
      )),
    },
    {
      name: "take_screenshot",
      description:
        "Capture a device screenshot into the companion runtime's bounded artifact directory.",
      inputSchema: {
        type: "object",
        properties: { deviceId: deviceIdSchema() },
        required: ["deviceId"],
      },
      handler: (ctx) => runCli(positional(
        ["screenshot", ...contextArgs(ctx)],
        deviceId(ctx.input.deviceId),
      )),
    },
    {
      name: "start_recording",
      description:
        "Start a bounded MP4 recording in the companion runtime's artifact directory.",
      inputSchema: {
        type: "object",
        properties: {
          deviceId: deviceIdSchema(),
          timeoutSeconds: { type: "integer", minimum: 1, maximum: 3600 },
        },
        required: ["deviceId"],
      },
      handler: (ctx) => runCli(positional(
        [
          "recording", "start",
          "--timeout", positiveNumber(ctx.input.timeoutSeconds, "timeoutSeconds", { min: 1, max: 3600, fallback: 180 }),
          ...contextArgs(ctx),
        ],
        deviceId(ctx.input.deviceId),
      )),
    },
    {
      name: "stop_recording",
      description: "Stop and finalize a device recording.",
      inputSchema: {
        type: "object",
        properties: { deviceId: deviceIdSchema() },
        required: ["deviceId"],
      },
      handler: (ctx) => runCli(positional(
        ["recording", "stop", ...contextArgs(ctx)],
        deviceId(ctx.input.deviceId),
      )),
    },
    {
      name: "get_recording_status",
      description: "Get current device recording status and artifact metadata.",
      inputSchema: {
        type: "object",
        properties: { deviceId: deviceIdSchema() },
        required: ["deviceId"],
      },
      handler: (ctx) => runCli(positional(
        ["recording", "status", ...contextArgs(ctx)],
        deviceId(ctx.input.deviceId),
      )),
    },
  ];
}
