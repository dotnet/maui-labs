// extension.mjs — MAUI DevFlow Inspector GitHub Copilot Canvas host entry point.
//
// One canvas, backed by a LIVE .NET MAUI app through DevFlow. Both a human (in the side
// panel) and Copilot (via the agent actions below) inspect and drive the SAME running app:
// read the visual tree, hit-test coordinates, select elements, live-edit properties, tap,
// fill, scroll, navigate, retheme, screenshot.
//
// Transport (see devflow.mjs): discovery goes through the DevFlow Broker
// (~/.mauidevflow/broker.json → GET /api/agents) and all interaction through the app's
// in-app DevFlow Agent (http://127.0.0.1:<port>/api/v1/...) — the same two hops the
// `maui devflow` CLI does internally, minus the per-call process spawn.

import { createReadStream, existsSync, lstatSync, mkdirSync, renameSync, statSync, unlinkSync, writeFileSync } from "node:fs";
import { createServer } from "node:http";
import { Buffer } from "node:buffer";
import { createHash, randomBytes } from "node:crypto";
import { basename, dirname, join, relative, resolve, sep } from "node:path";
import { joinSession, createCanvas, CanvasError } from "@github/copilot-sdk/extension";
import { LiveStore } from "./store.mjs";
import { Recorder } from "./recorder.mjs";
import { renderShell, renderDisconnected } from "./shell.mjs";
import { readJsonBody, selectInspectorAgent } from "./http.mjs";
import { dispatchAgentRequest } from "./agent-request.mjs";
import {
  isInspectorQueryResult,
  isInspectorSnapshot,
  readBrokerState,
} from "@maui-devflow/client";

// Device targeting is optional — the CLI auto-discovers the agent via the broker. Override
// via env if you run multiple emulators/apps at once.
function deviceOpts(input = {}) {
  const o = {};
  const platform = input.platform || process.env.MAUI_DEVFLOW_PLATFORM;
  const device = input.device || process.env.MAUI_DEVFLOW_DEVICE;
  const agentPort = input.agentPort ?? process.env.MAUI_DEVFLOW_AGENT_PORT;
  const projectRoot =
    input.projectRoot || input.workingDirectory || process.env.MAUI_DEVFLOW_PROJECT_ROOT;
  if (platform) o.platform = String(platform);
  if (device) o.device = String(device);
  if (input.mutationLeaseId) o.mutationLeaseId = String(input.mutationLeaseId);
  if (projectRoot) o.projectRoot = String(projectRoot);
  if (agentPort) {
    const parsed = Number(agentPort);
    if (Number.isFinite(parsed)) o.agentPort = parsed;
  }
  return o;
}

function inspectorStartupHints(input = {}) {
  const hints = {};
  for (const key of ["test", "trace", "agentRequest"]) {
    const value = typeof input?.[key] === "string" ? input[key].trim() : "";
    if (value && value.length <= 2048) hints[key] = value;
  }
  return hints;
}

// instanceId -> { store, server, port, url, sse:Set<res>, recordingSse:Set<res> }
const instances = new Map();

// The joined Copilot session (assigned at the very bottom, after createCanvas).
// Used by pushSelectionContext() to drop a context pill into the composer.
let sharedSession = null;

function ensure(instanceId, input = {}) {
  let st = instances.get(instanceId);
  if (!st) {
    const bridgeId = randomToken();
    const store = new LiveStore({
      ...deviceOpts({ ...input, mutationLeaseId: bridgeId }),
      inspectSnapshot: async () => {
        const result = await getInspectorJson(st, "/api/inspect/snapshot");
        return isInspectorSnapshot(result)
          ? result
          : result?.ok === false
            ? result
            : {
                ok: false,
                error: { code: "invalid-response", message: "The Inspector snapshot contract was invalid.", retriable: true },
              };
      },
      inspectQuery: async (query) => {
        const result = await postInspectorJson(st, "/api/inspect/query", query);
        return isInspectorQueryResult(result)
          ? result
          : result?.ok === false
            ? result
            : {
                ok: false,
                error: { code: "invalid-response", message: "The Inspector query contract was invalid.", retriable: true },
              };
      },
      inspectAction: (path, body) => postInspectorJson(st, path, body),
    });
    const recorder = new Recorder();
    st = {
      store,
      recorder,
      server: null,
      port: 0,
      url: null,
      sse: new Set(),
      recordingSse: new Set(),
      recordingEventKey: null,
      bridgeId,
      startupHints: inspectorStartupHints(input),
    };
    store.subscribe((snapshot) => {
      broadcast(st, snapshot);
      broadcastRecording(st, snapshot);
    });
    instances.set(instanceId, st);
  } else {
    st.startupHints = { ...(st.startupHints || {}), ...inspectorStartupHints(input) };
  }
  return st;
}

function requireInstance(ctx) {
  const st = instances.get(ctx.instanceId);
  if (!st) throw new CanvasError("not_open", "Canvas is not open");
  return st;
}

// Hard time-box any promise. A capability that shells to a slow or wedged DevFlow
// agent must never leave the Copilot session waiting on a result forever (which the
// app surfaces as "Session appears unresponsive"). On timeout we RESOLVE with a
// readable, recoverable error object; real rejections (e.g. CanvasError) pass through.
function withTimeout(promise, ms, label) {
  return new Promise((resolve, reject) => {
    let settled = false;
    const t = setTimeout(() => {
      if (settled) return;
      settled = true;
      resolve({
        ok: false,
        timedOut: true,
        error: `${label} timed out after ${ms}ms — the running app or DevFlow agent did not respond. It may be busy; try again.`,
      });
    }, ms);
    Promise.resolve(promise).then(
      (v) => { if (!settled) { settled = true; clearTimeout(t); resolve(v); } },
      (e) => { if (!settled) { settled = true; clearTimeout(t); reject(e); } }
    );
  });
}

// Every agent-facing capability handler is time-boxed through this wrapper so a single
// hung DevFlow call can never wedge the whole session. 30s comfortably covers the
// slowest legit capability (a screenshot refresh ~20s + one retry) while still
// guaranteeing the session always gets a result back and can recover.
const CAP_TIMEOUT_MS = 30000;
function wrapActions(actions) {
  return actions.map((a) => ({
    ...a,
    handler: (ctx) =>
      withTimeout(Promise.resolve().then(() => a.handler(ctx)), a.timeoutMs || CAP_TIMEOUT_MS, `Capability '${a.name}'`),
  }));
}

function broadcast(st, snapshot) {
  const payload = `data: ${JSON.stringify(snapshot)}\n\n`;
  for (const res of st.sse) {
    try {
      res.write(payload);
    } catch {
      st.sse.delete(res);
    }
  }
}

function recordingProjection(snapshot) {
  return {
    recording: snapshot?.recording === true,
    steps: Number(snapshot?.recorder?.count || 0),
  };
}

function broadcastRecording(st, snapshot) {
  const projection = recordingProjection(snapshot);
  const key = `${projection.recording}:${projection.steps}`;
  if (key === st.recordingEventKey) return;
  st.recordingEventKey = key;
  const payload = `data: ${JSON.stringify(projection)}\n\n`;
  for (const res of st.recordingSse) {
    try {
      res.write(payload);
    } catch {
      st.recordingSse.delete(res);
    }
  }
}

const CONTEXT_ATTACHMENT_MAX_BYTES = 20_000;

// Push lightweight, structured context into the Copilot composer. Standard text blobs remain
// compatible with hosts whose message transport predates extension_context attachments.
async function pushContextPill(instanceId, options) {
  const st = instances.get(instanceId);
  const nowTs = Date.now();
  if (st && st._lastPushKey === options.pushKey && nowTs - (st._lastPushAt || 0) < 30000) {
    return { ok: true, deduped: true, status: options.duplicateStatus };
  }
  const api = sharedSession?.rpc?.extensions?.sendAttachmentsToMessage;
  if (typeof api !== "function") {
    return {
      ok: false,
      code: "unsupported_runtime",
      error:
        "This Copilot build can't receive canvas context yet. Update the Copilot CLI/app " +
        "(session.extensions.sendAttachmentsToMessage is unavailable).",
    };
  }
  let content;
  try {
    content = JSON.stringify({
      kind: "mauiDevFlowContext",
      title: options.title,
      payload: options.payload,
    }, null, 2);
  } catch {
    return { ok: false, error: `Could not serialize ${options.errorLabel}.` };
  }
  if (Buffer.byteLength(content, "utf8") > CONTEXT_ATTACHMENT_MAX_BYTES) {
    return { ok: false, error: `${options.errorLabel} exceeded the safe context size.` };
  }
  try {
    // Do not pass instanceId: canvas provenance resolution can wedge host restarts.
    const r = await withTimeout(
      api.call(sharedSession.rpc.extensions, {
        attachments: [{
          type: "blob",
          displayName: options.title,
          mimeType: "text/plain",
          data: Buffer.from(content, "utf8").toString("base64"),
        }],
      }),
      8000,
      "Attach-to-Copilot",
    );
    if (r && r.timedOut) return { ok: false, error: r.error };
  } catch (e) {
    return { ok: false, error: `Could not attach ${options.errorLabel}: ${String(e?.message || e)}` };
  }
  if (st) { st._lastPushKey = options.pushKey; st._lastPushAt = nowTs; }
  return { ok: true, status: options.successStatus };
}

// Push the current canvas selection into the Copilot composer.
async function pushSelectionContext(instanceId, store, fallbackElement) {
  let sel = store.selectionContext();
  // Fall back to the element the inspector pushed if the store selection isn't set (the human's
  // selection came from the embedded shared inspector rather than a store-side select).
  if ((!sel || !sel.selectedId) && fallbackElement && fallbackElement.id) {
    const summary = [fallbackElement.type || "Element",
      fallbackElement.automationId ? "#" + fallbackElement.automationId : "",
      fallbackElement.text ? '"' + fallbackElement.text + '"' : ""].filter(Boolean).join(" ");
    sel = { selectedId: fallbackElement.id, summary, element: fallbackElement };
  }
  if (!sel || !sel.selectedId) {
    return { ok: false, error: "Nothing is selected — click an element in the canvas first." };
  }
  const title = `MAUI selection · ${sel.summary}`;
  const payload = { ...sel, capturedAt: new Date().toISOString() };
  return pushContextPill(instanceId, {
    title,
    payload,
    pushKey: `selection:${sel.selectedId}:${sel.summary || ""}`,
    duplicateStatus: `${sel.summary} is already attached to Copilot`,
    successStatus: `Attached ${sel.summary} to Copilot`,
    errorLabel: "selection",
  });
}

async function pushInspectorContext(instanceId, store, context, input) {
  const payload = input && typeof input === "object" ? input : {};
  const element = payload.element && typeof payload.element === "object"
    ? payload.element
    : store.selectionContext()?.element;
  const markdown = typeof payload.markdown === "string" ? payload.markdown.slice(0, 6000) : null;
  if (context === "selection" && !element)
    return { ok: false, error: "Nothing is selected — click an element in the canvas first." };
  if (context === "workflow" && !markdown)
    return { ok: false, error: "No workflow is loaded in Inspector." };
  if (context === "combined" && (!element || !markdown))
    return { ok: false, error: "Select an element and load a workflow before attaching both." };

  const title = context === "selection"
    ? `MAUI selection · ${element.type || "Element"}${element.automationId ? ` #${element.automationId}` : ""}`
    : context === "workflow"
      ? `MAUI workflow · ${payload.workflowName || "workflow"}`
      : `MAUI selection + workflow · ${element.type || "Element"}${element.automationId ? ` #${element.automationId}` : ""}`;
  return pushContextPill(instanceId, {
    title,
    payload: {
      kind: "inspectorContext",
      context,
      appName: payload.appName || store.snapshot()?.info?.appName || null,
      element: context === "workflow" ? null : element,
      workflow: context === "selection" ? null : {
        name: payload.workflowName || null,
        source: payload.workflowSource || null,
        markdown,
        truncated: payload.markdownTruncated === true,
      },
      capturedAt: new Date().toISOString(),
    },
    pushKey: `inspector:${context}:${element?.id || ""}:${payload.workflowName || ""}:${markdown?.length || 0}`,
    duplicateStatus: "This Inspector context is already attached to Copilot",
    successStatus: "Attached Inspector context to Copilot",
    errorLabel: "Inspector context",
  });
}

async function sendAgentRequest(instanceId, prompt, title) {
  return dispatchAgentRequest(sharedSession, instances.get(instanceId), prompt, title, {
    timeout: withTimeout,
  });
}

const DATA_CONTEXT_SCOPES = new Set(["logs", "network", "preferences", "device", "sensors", "files", "alerts"]);

async function pushDataContext(instanceId, snapshot) {
  if (!snapshot || snapshot.kind !== "dataSnapshot" || snapshot.redacted !== true
      || !DATA_CONTEXT_SCOPES.has(snapshot.scope) || typeof snapshot.title !== "string") {
    return { ok: false, error: "The DevFlow Data snapshot is invalid." };
  }
  const serialized = JSON.stringify(snapshot);
  if (Buffer.byteLength(serialized, "utf8") > CONTEXT_ATTACHMENT_MAX_BYTES) {
    return { ok: false, error: "The DevFlow Data snapshot exceeded the safe context size." };
  }
  const payload = JSON.parse(serialized);
  const digest = createHash("sha256").update(serialized).digest("hex").slice(0, 16);
  return pushContextPill(instanceId, {
    title: `MAUI data · ${snapshot.title}`,
    payload,
    pushKey: `data:${snapshot.scope}:${digest}`,
    duplicateStatus: `${snapshot.title} is already attached to Copilot`,
    successStatus: `Attached ${snapshot.title} to Copilot`,
    errorLabel: "Data context",
  });
}

// Browser (side-panel) control actions → store methods. All async.
async function applyControl(st, body, instanceId) {
  const store = st.store;
  switch (body.action) {
    case "refresh":      return store.refresh();
    case "select":       return { ok: !!store.select(body.id) };
    case "hitTest":      return { ok: !!(await store.hitTestSelect(Number(body.x), Number(body.y))) };
    case "getProperty":  return store.getProperty(body.id, body.name || "Text");
    case "setProperty":  return store.setProperty(body.id, body.name || "Text", body.value);
    case "applyVerify":  return store.applyAndVerify(body.id, body.name || "Text", body.value);
    case "tap":          return store.tap({ id: body.id });
    case "fill":         return store.fill({ id: body.id }, body.text ?? "");
    case "scroll":       return store.scroll(body);
  case "navigate":     return store.navigate(body.route);
  case "back":         return store.back();
  case "resize":       return store.resize(Number(body.width), Number(body.height));
  case "setTheme":     return store.setTheme(body.theme || "light");
  case "listAgents":   return store.listAgents();
  case "selectAgent":  return store.selectAgent({ platform: body.platform, port: body.port });
  case "screenshot":   return store.refresh({ shot: true });
  case "logs":         return store.getLogs(body.limit || 100);
  case "attachSelection": return pushSelectionContext(instanceId, store, body.element);
  case "attachCopilot": return pushInspectorContext(instanceId, store, body.context, body.payload);
  case "requestTestProposal": return sendAgentRequest(instanceId, body.prompt, body.title);
  case "attachData":   return pushDataContext(instanceId, body.snapshot);
  case "saveTestBundle": return persistTestBundle(st, body.bundle);
  // ── Workflow Test Recorder ──────────────────────────────────────────────
  case "record.start": {
    const r = await store.device.recordingStart({ name: body.name, preconditions: body.preconditions });
    store._recordingStatus = r;
    store._emit();
    return r;
  }
  case "record.stop":
  case "record.save": {
    const r = await store.device.recordingStop(store._recordingStatus?.recordingId);
    store._recordingStatus = r;
    store._emit();
    if (!r.ok) return r;
    return persistRecording(st, { markdown: r.markdown, name: body.name || r.name });
  }
  case "record.clear": {
    const r = await store.device.recordingCancel(store._recordingStatus?.recordingId);
    store._recordingStatus = r;
    store._emit();
    return r;
  }
  case "record.deleteLast":
    return { ok: false, error: "Deleting individual steps is not supported by shared recordings." };
  case "record.status": {
    const r = await store.device.recordingStatus();
    store._recordingStatus = r;
    store._emit();
    return r;
  }
  case "record.list":       return st.recorder.list(store);
  case "replay": {
    const status = await store.device.recordingStatus();
    if (status.recording) return { ok: false, error: "Stop or cancel the active recording before replaying a test." };
    return replaySharedFlow(st, body);
  }
  default:             return { ok: false, error: `unknown action: ${body.action}` };
}
}

// Build the shared broker Inspector URL for this instance's resolved agent, or null when no
// broker/agent is available yet (then the canvas falls back to the disconnected shell). The
// ?embed={token} parameter proves this is a trusted local shell so the Inspector relaxes its
// anti-framing headers and the iframe can load.
function brokerInspectorUrl(st) {
  try {
    const state = readBrokerState();
    const agent = st.store?.device?.resolvedAgent?.();
    if (state?.port && agent?.id) {
      // The broker's HttpListener is bound to `localhost`, so the iframe host MUST be localhost
      // (a 127.0.0.1 Host header is rejected as "Invalid Hostname"). frame-ancestors covers both.
      const base = `http://localhost:${state.port}/inspector/${encodeURIComponent(agent.id)}/`;
      return state.embedToken ? `${base}?embed=${encodeURIComponent(state.embedToken)}` : base;
    }
  } catch {
    // Broker state unreadable — fall back to the disconnected shell.
  }
  return null;
}

function withInspectorStartupHints(inspectorUrl, hints) {
  const url = new URL(inspectorUrl);
  for (const [key, value] of Object.entries(hints || {}))
    url.searchParams.set(key, value);
  return url.toString();
}

async function postInspectorJson(st, path, body, timeoutMs = 30000) {
  const inspectorUrl = await resolveInspectorUrl(st);
  if (!inspectorUrl) {
    return {
      ok: false,
      attempted: false,
      error: { code: "broker-unavailable", message: "The shared DevFlow Inspector is unavailable.", retriable: true },
    };
  }

  const endpoint = new URL(path.replace(/^\/+/, ""), inspectorUrl);
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const response = await fetch(endpoint, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-DevFlow-Lease": st.bridgeId,
        "X-DevFlow-Writer": st.bridgeId,
        "X-DevFlow-Holder": "canvas",
        "X-DevFlow-Label": "GitHub Copilot Canvas",
      },
      body: JSON.stringify(body || {}),
      signal: controller.signal,
    });
    const result = await response.json().catch(() => null);
    if (!response.ok) {
      return {
        ok: false,
        attempted: true,
        error: result?.error || {
          code: response.status === 409 ? "lease-held" : "inspector-http",
          message: `The shared DevFlow Inspector rejected the request (HTTP ${response.status}).`,
          retriable: response.status >= 500,
        },
        status: response.status,
      };
    }

    return result && typeof result === "object" ? result : { ok: false, error: "The shared DevFlow Inspector returned an invalid response." };
  } catch (e) {
    return {
      ok: false,
      attempted: true,
      error: {
        code: e?.name === "AbortError" ? "timeout" : "broker-unavailable",
        message: e?.name === "AbortError"
          ? "The shared DevFlow Inspector request timed out."
          : String(e?.message || e),
        retriable: true,
      },
    };
  } finally {
    clearTimeout(timer);
  }
}

async function getInspectorJson(st, path, timeoutMs = 10000) {
  const inspectorUrl = await resolveInspectorUrl(st);
  if (!inspectorUrl) {
    return {
      ok: false,
      error: { code: "broker-unavailable", message: "The shared DevFlow Inspector is unavailable.", retriable: true },
    };
  }

  const endpoint = new URL(path.replace(/^\/+/, ""), inspectorUrl);
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const response = await fetch(endpoint, { signal: controller.signal });
    const result = await response.json().catch(() => null);
    if (!response.ok) {
      return {
        ok: false,
        error: result?.error || {
          code: "inspector-http",
          message: `The shared DevFlow Inspector rejected the request (HTTP ${response.status}).`,
          retriable: response.status >= 500,
        },
      };
    }
    return result && typeof result === "object"
      ? result
      : { ok: false, error: { code: "invalid-response", message: "The shared DevFlow Inspector returned invalid JSON.", retriable: true } };
  } catch (error) {
    return {
      ok: false,
      error: {
        code: error?.name === "AbortError" ? "timeout" : "broker-unavailable",
        message: error?.name === "AbortError"
          ? "The shared DevFlow Inspector request timed out."
          : String(error?.message || error),
        retriable: true,
      },
    };
  } finally {
    clearTimeout(timer);
  }
}

async function replaySharedFlow(st, input = {}) {
  const selected = st.recorder.resolveTestName(st.store, input);
  if (!selected.ok) return selected;

  const loaded = st.recorder.load(st.store, { name: selected.name });
  if (!loaded.ok || typeof loaded.markdown !== "string") {
    return { ok: false, error: loaded.error || "The Canvas host could not load the workflow test." };
  }

  // Canvas mutations use their own lease identity. Release it before the shared Inspector
  // claims its run-scoped lease so the canonical C# replay cannot interleave with Canvas actions.
  await st.store.device.releaseMutationLease().catch(() => {});
  return postInspectorJson(st, "/api/flows/replay", { markdown: loaded.markdown }, 130000);
}

// Query the broker's live agent list. HTTP fallback used when the client's cached registration
// went stale (e.g. after a broker restart the client reconnects to the app port but drops its
// broker registration), so we can still resolve an agent id for the shared inspector URL.
async function fetchBrokerAgents(port) {
  try {
    const ctrl = new AbortController();
    const timer = setTimeout(() => ctrl.abort(), 2000);
    const r = await fetch(`http://localhost:${port}/api/agents`, { signal: ctrl.signal });
    clearTimeout(timer);
    if (!r.ok) return [];
    const j = await r.json();
    return Array.isArray(j) ? j : [];
  } catch {
    return [];
  }
}

// Resilient variant: build the shared inspector URL even when resolvedAgent() is momentarily
// stale, by matching the broker's live agent list on the app port we're connected to. This keeps
// the canvas on the SHARED tool across broker restarts instead of dropping to the disconnected shell.
async function resolveInspectorUrl(st) {
  const direct = brokerInspectorUrl(st);
  if (direct) return direct;
  try {
    const state = readBrokerState();
    if (!state?.port) return null;
    const port = st.store?.device?.whichPort?.();
    const agents = await fetchBrokerAgents(state.port);
    const match = selectInspectorAgent(agents, port);
    if (match?.id) {
      const base = `http://localhost:${state.port}/inspector/${encodeURIComponent(match.id)}/`;
      return state.embedToken ? `${base}?embed=${encodeURIComponent(state.embedToken)}` : base;
    }
  } catch {
    // fall back to the disconnected shell
  }
  return null;
}

async function inspectorReadiness(st) {
  const inspectorUrl = await resolveInspectorUrl(st);
  const snapshot = st.store.snapshot();
  const appName = snapshot.connected ? snapshot.info?.appName || null : null;
  if (inspectorUrl) return { ready: true, state: "ready", appName };
  try {
    const state = readBrokerState();
    if (!state?.port) return { ready: false, state: "broker", appName };
    const ctrl = new AbortController();
    const timer = setTimeout(() => ctrl.abort(), 2000);
    try {
      const response = await fetch(`http://localhost:${state.port}/api/agents`, {
        signal: ctrl.signal,
      });
      return {
        ready: false,
        state: response.ok ? "app" : "broker",
        appName,
      };
    } finally {
      clearTimeout(timer);
    }
  } catch {
    return { ready: false, state: "broker", appName };
  }
}

// 128-bit URL-safe token — the per-instance host-bridge nonce, safe in a URL fragment and in the
// [A-Za-z0-9_-] the shared inspector accepts.
function randomToken() {
  return randomBytes(16).toString("base64").replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

// Persist a recording relayed over the host bridge into the project's maui-tests/ folder, reusing
// the canvas recorder's own directory resolution so bridge + native recordings land together.
// Requires the per-instance bridge nonce so a cross-site localhost POST can't write project files.
function saveBridgeRecording(st, body) {
  if (!body || body.bridgeId !== st.bridgeId) return { ok: false, error: "unauthorized" };
  return persistRecording(st, body);
}

function persistRecording(st, body) {
  return st.recorder.persist(st.store, body);
}

const TEST_BUNDLE_MAX_BYTES = 1024 * 1024;

function safeBundleName(value) {
  if (typeof value !== "string") return null;
  const name = value.trim();
  return name && name.length <= 255 && /\.md$/i.test(name) &&
    basename(name) === name && !name.includes("/") && !name.includes("\\") ? name : null;
}

function isDigest(value) {
  return typeof value === "string" && /^[a-f0-9]{64}$/i.test(value);
}

function canonicalBundleJson(value) {
  if (Array.isArray(value)) return value.map(canonicalBundleJson);
  if (value && typeof value === "object") {
    return Object.fromEntries(Object.keys(value).sort().map((key) => [key, canonicalBundleJson(value[key])]));
  }
  return value;
}

function canonicalBundleDigest(value) {
  return createHash("sha256").update(JSON.stringify(canonicalBundleJson(value)), "utf8").digest("hex");
}

function flowPayload(markdown) {
  const match = /```json maui-test\s*\r?\n([\s\S]*?)\r?\n```/.exec(markdown);
  if (!match) return null;
  try { return JSON.parse(match[1]); } catch { return null; }
}

function noReparsePoint(path) {
  try { return !lstatSync(path).isSymbolicLink(); } catch { return true; }
}

function canvasBundleRoot(st) {
  const project = st.store?.device?.resolvedAgent?.()?.project;
  if (!project || !existsSync(project) || !noReparsePoint(project)) return null;
  let projectRoot;
  try { projectRoot = statSync(project).isDirectory() ? project : dirname(project); } catch { return null; }
  if (!noReparsePoint(projectRoot)) return null;
  const root = resolve(projectRoot, "maui-tests");
  const rel = relative(resolve(projectRoot), root);
  if (rel === ".." || rel.startsWith(`..${sep}`)) return null;
  try {
    mkdirSync(root, { recursive: true });
    return noReparsePoint(root) ? root : null;
  } catch {
    return null;
  }
}

function persistTestBundle(st, bundle) {
  const name = safeBundleName(bundle?.name);
  const markdown = typeof bundle?.markdown === "string" ? bundle.markdown : null;
  const planJson = typeof bundle?.planJson === "string" ? bundle.planJson : null;
  if (!name || !markdown || !planJson || !isDigest(bundle?.flowDigest) ||
      (bundle?.planDigest != null && !isDigest(bundle.planDigest))) {
    return { ok: false, error: "The test bundle must contain bounded flow and plan content bound to SHA-256 digests." };
  }
  if (Buffer.byteLength(markdown, "utf8") > TEST_BUNDLE_MAX_BYTES ||
      Buffer.byteLength(planJson, "utf8") > TEST_BUNDLE_MAX_BYTES) {
    return { ok: false, error: "Flow and plan files must each be 1 MB or smaller." };
  }
  let plan;
  try { plan = JSON.parse(planJson); } catch { return { ok: false, error: "The plan sidecar is not valid JSON." }; }
  if (!plan || plan.schema !== 1 || plan.flow?.path !== name || plan.flow?.digest !== bundle.flowDigest)
    return { ok: false, error: "The plan sidecar is not bound to this canonical flow filename and digest." };
  const payload = flowPayload(markdown);
  if (!payload || canonicalBundleDigest(payload) !== bundle.flowDigest)
    return { ok: false, error: "The flow digest does not match the authoritative maui-test payload." };
  if (bundle.planDigest && canonicalBundleDigest(plan) !== bundle.planDigest)
    return { ok: false, error: "The plan digest does not match the submitted plan sidecar." };

  const root = canvasBundleRoot(st);
  if (!root) return { ok: false, error: "A safe project maui-tests directory could not be resolved." };
  const flow = join(root, name);
  const sidecar = join(root, name.replace(/\.md$/i, "") + ".maui-plan.json");
  if (!noReparsePoint(flow) || !noReparsePoint(sidecar))
    return { ok: false, error: "Refusing to overwrite a symbolic-link test artifact." };

  const token = randomBytes(12).toString("hex");
  const items = [
    { path: flow, temp: `${flow}.${token}.tmp`, backup: null, content: markdown, committed: false },
    { path: sidecar, temp: `${sidecar}.${token}.tmp`, backup: null, content: planJson, committed: false },
  ];
  let preserveBackups = false;
  try {
    for (const item of items) writeFileSync(item.temp, item.content, { encoding: "utf8", flag: "wx" });
    for (const item of items) {
      if (!existsSync(item.path)) continue;
      item.backup = `${item.path}.${token}.bak`;
      renameSync(item.path, item.backup);
    }
    for (const item of items) {
      renameSync(item.temp, item.path);
      item.committed = true;
    }
    for (const item of items) if (item.backup) unlinkSync(item.backup);
    return { ok: true, status: `Saved ${name} and ${basename(sidecar)}.`, name };
  } catch {
    let restored = true;
    for (const item of [...items].reverse()) {
      try {
        if (item.committed && existsSync(item.path)) unlinkSync(item.path);
        if (item.backup && existsSync(item.backup)) renameSync(item.backup, item.path);
      } catch { restored = false; }
    }
    preserveBackups = !restored;
    return {
      ok: false,
      error: restored
        ? "The bundle was not saved; the prior flow and plan were restored."
        : "The bundle write failed and the prior files could not be fully restored.",
    };
  } finally {
    for (const item of items) {
      try { if (existsSync(item.temp)) unlinkSync(item.temp); } catch {}
      try { if (!preserveBackups && item.backup && existsSync(item.backup)) unlinkSync(item.backup); } catch {}
    }
  }
}

async function startServer(instanceId, input = {}) {
  const st = ensure(instanceId, input);
  const server = createServer(async (req, res) => {
    const host = req.headers.host || "";
    if (host !== `127.0.0.1:${st.port}` && host !== `localhost:${st.port}`) {
      res.statusCode = 403;
      res.end("Forbidden");
      return;
    }
    const url = new URL(req.url, "http://127.0.0.1");

    if (url.pathname === "/" && req.method === "GET") {
      res.setHeader("Content-Type", "text/html; charset=utf-8");
      res.setHeader("Cache-Control", "no-store");
      // The canvas IS the shared inspector: it reverse-proxies the broker's per-agent inspector
      // (the same devflow.js/inspector.html the browser + VS Code host use). If the agent isn't
      // resolved yet (panel opened during connect), resolve it FIRST — otherwise we'd fall back to
      // the lightweight disconnected shell. The canvas should always be the shared tool whenever a
      // broker + a running app are reachable; renderDisconnected is only a last resort (and self-
      // heals via /inspector-ready) when nothing resolves yet.
      let inspectorUrl = await resolveInspectorUrl(st);
      if (!inspectorUrl) {
        try { await withTimeout(st.store.refresh(), 5000, "resolve-agent"); } catch { /* fall through to the disconnected shell */ }
        inspectorUrl = await resolveInspectorUrl(st);
      }
      const snapshot = st.store.snapshot();
      const appName = snapshot.connected ? snapshot.info?.appName : null;
      const readiness = inspectorUrl ? null : await inspectorReadiness(st);
      res.end(inspectorUrl
        ? renderShell(
          withInspectorStartupHints(inspectorUrl, st.startupHints),
          appName,
          st.bridgeId,
        )
        : renderDisconnected(appName, readiness?.state));
      return;
    }

    // Cheap readiness probe for the disconnected shell's self-heal: report whether the SHARED
    // inspector is now reachable (broker + a resolved running app). The fallback UI polls this and
    // reloads into the shared inspector once it flips true, so a panel opened mid-(re)connect
    // converges on its own.
    if (url.pathname === "/inspector-ready" && req.method === "GET") {
      res.setHeader("Content-Type", "application/json");
      res.setHeader("Cache-Control", "no-store");
      let readiness = await inspectorReadiness(st);
      if (!readiness.ready) {
        try {
          await withTimeout(st.store.refresh(), 2500, "ready-check");
          readiness = await inspectorReadiness(st);
        } catch { /* still not ready */ }
      }
      res.end(JSON.stringify(readiness));
      return;
    }

    if (url.pathname === "/events" && req.method === "GET") {
      res.statusCode = 200;
      res.setHeader("Content-Type", "text/event-stream");
      res.setHeader("Cache-Control", "no-store");
      res.setHeader("Connection", "keep-alive");
      st.sse.add(res);
      req.on("close", () => st.sse.delete(res));
      res.write(`data: ${JSON.stringify(st.store.snapshot())}\n\n`);
      return;
    }

    if (url.pathname === "/recording-events" && req.method === "GET") {
      res.statusCode = 200;
      res.setHeader("Content-Type", "text/event-stream");
      res.setHeader("Cache-Control", "no-store");
      res.setHeader("Connection", "keep-alive");
      st.recordingSse.add(res);
      req.on("close", () => st.recordingSse.delete(res));
      res.write(`data: ${JSON.stringify(recordingProjection(st.store.snapshot()))}\n\n`);
      return;
    }

    // Live screenshot PNG (cache-busted by ?seq=N from the browser).
    if (url.pathname === "/shot" && req.method === "GET") {
      const p = st.store.currentShotPath();
      if (!p) {
        res.statusCode = 204;
        res.end();
        return;
      }
      res.setHeader("Content-Type", "image/png");
      res.setHeader("Cache-Control", "no-store");
      const stream = createReadStream(p);
      stream.on("error", () => {
        res.statusCode = 404;
        res.end();
      });
      stream.pipe(res);
      return;
    }

    if (url.pathname === "/control" && req.method === "POST") {
      if (!String(req.headers["content-type"] || "").includes("application/json")) {
        res.statusCode = 415;
        res.setHeader("Content-Type", "application/json");
        res.end(JSON.stringify({ ok: false, error: "unsupported media type" }));
        return;
      }
      const parsed = await readJsonBody(req);
      if (!parsed.ok) {
        res.statusCode = parsed.status;
        res.setHeader("Content-Type", "application/json");
        res.end(JSON.stringify({ ok: false, error: parsed.error }));
        return;
      }
      const body = parsed.value;
      if (!body || body.bridgeId !== st.bridgeId) {
        res.statusCode = 403;
        res.setHeader("Content-Type", "application/json");
        res.end(JSON.stringify({ ok: false, error: "unauthorized" }));
        return;
      }
      let r;
      try {
        r = await applyControl(st, body, instanceId);
      } catch (e) {
        r = { ok: false, error: String(e?.message || e) };
      }
      res.setHeader("Content-Type", "application/json");
      res.end(JSON.stringify(r ?? { ok: true }));
      return;
    }

    // The host bridge's `saveRecording` capability receives a completed recording from the Inspector
    // and the shell relayed the Markdown here. Persist it into the MAUI project's maui-tests/ folder
    // (same convention as the canvas's own recorder), so record-in-the-inspector lands a real test.
    // Requiring application/json forces a CORS preflight a cross-site page can't satisfy, and the
    // body must carry the per-instance bridge nonce (see saveBridgeRecording).
    if (url.pathname === "/recording" && req.method === "POST") {
      if (!String(req.headers["content-type"] || "").includes("application/json")) {
        res.statusCode = 415;
        res.setHeader("Content-Type", "application/json");
        res.end(JSON.stringify({ ok: false, error: "unsupported media type" }));
        return;
      }
      const parsed = await readJsonBody(req);
      if (!parsed.ok) {
        res.statusCode = parsed.status;
        res.setHeader("Content-Type", "application/json");
        res.end(JSON.stringify({ ok: false, error: parsed.error }));
        return;
      }
      const body = parsed.value;
      let r;
      try {
        r = saveBridgeRecording(st, body);
      } catch (e) {
        r = { ok: false, error: String(e?.message || e) };
      }
      res.setHeader("Content-Type", "application/json");
      res.end(JSON.stringify(r ?? { ok: true }));
      return;
    }

    res.statusCode = 404;
    res.end("Not found");
  });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  const addr = server.address();
  st.port = typeof addr === "object" && addr ? addr.port : 0;
  st.server = server;
  st.url = `http://127.0.0.1:${st.port}/`;
  return st.url;
}

const canvas = createCanvas({
  id: "maui-live-canvas",
  displayName: "MAUI DevFlow Inspector",
  description:
    "A LIVE view of a running .NET MAUI app: its real screenshot with a visual-tree overlay and " +
    "an editable property grid. The agent can inspect the tree, hit-test coordinates, select " +
    "elements, read/edit properties, tap, fill, scroll, navigate, retheme, and verify changes — all " +
    "against the same running app the human sees, through the DevFlow agent. " +
    "If several apps are running (for example the same app built for Android and for Windows), " +
    "call list_agents to see them and select_agent to switch which one the canvas is attached to. " +
    "IMPORTANT: the human can click an element in the canvas to select it. Whenever the user refers " +
    "to \u201cthe selected element\u201d, \u201cthis element\u201d, \u201cthe highlighted control\u201d, or \u201cwhat I clicked\u201d, call " +
    "get_selection first to resolve exactly which element (id, type, text, bounds) they mean before acting.",
  inputSchema: {
    type: "object",
    properties: {
      agentPort: { type: "number", description: "DevFlow agent port to target when multiple apps are running." },
      platform: { type: "string", description: "Optional platform hint, e.g. android or windows." },
      device: { type: "string", description: "Optional device/emulator identifier." },
      test: { type: "string", description: "Optional saved test hint to open in the shared Test Workbench." },
      trace: { type: "string", description: "Optional test-result hint to open in the shared Test Workbench." },
      agentRequest: { type: "string", description: "Optional agent-request identifier to focus in the shared Test Workbench." },
    },
  },

  actions: wrapActions([
    {
      name: "get_canvas",
      description: "Return a summary of the live app: connection status, app name/platform, element count, and current selection.",
      handler: async (ctx) => {
        const snap = requireInstance(ctx).store.snapshot();
        const count = (function c(rs) { let n = 0; for (const e of rs || []) { n += 1 + c(e.children); } return n; })(snap.roots);
        const i = snap.info || {};
        return {
          connected: snap.connected,
          app: i.appName,
          platform: i.platform,
          framework: i.framework ? `${i.framework} ${i.frameworkVersion || ""}`.trim() : undefined,
          agentPort: i.agentPort,
          window: i.window,
          density: i.density,
          elementCount: count,
          selectedId: snap.selectedId,
          lastError: snap.lastError,
        };
      },
    },
    {
      name: "refresh",
      description: "Re-pull the live visual tree and capture a fresh screenshot from the running app.",
      handler: async (ctx) => {
        const snap = await requireInstance(ctx).store.refresh();
        return { ok: snap.connected, elementRoots: snap.roots.length, lastError: snap.lastError };
      },
    },
    {
      name: "list_agents",
      description:
        "List the running MAUI apps (DevFlow agents) the canvas can attach to. Each entry has a " +
        "platform, app name, and port, and one is marked active. Use this before select_agent when " +
        "the same app is running on more than one platform.",
      handler: async (ctx) => {
        const store = requireInstance(ctx).store;
        const agents = await store.listAgents();
        return { agents, activePort: store.snapshot().activePort };
      },
    },
    {
      name: "select_agent",
      description:
        "Switch the canvas to a different running app/platform (e.g. the Android build vs the Windows " +
        "build). Provide a port (preferred, from list_agents) and/or a platform hint. The canvas then " +
        "re-pulls the visual tree and screenshot for the newly selected app and clears any selection.",
      inputSchema: {
        type: "object",
        properties: {
          port: { type: "number", description: "DevFlow agent port to attach to (from list_agents)." },
          platform: { type: "string", description: "Platform hint, e.g. android or windows." },
        },
      },
      handler: async (ctx) => {
        const snap = await requireInstance(ctx).store.selectAgent({
          port: ctx.input?.port,
          platform: ctx.input?.platform,
        });
        return {
          ok: snap.connected,
          activePort: snap.activePort,
          app: snap.info?.appName,
          platform: snap.info?.platform,
          elementRoots: snap.roots.length,
          lastError: snap.lastError,
        };
      },
    },
    {
      name: "get_tree",
      description: "Return the current live visual tree as JSON (roots with nested children; each node has id, type, automationId, text, bounds).",
      handler: async (ctx) => {
        const snap = requireInstance(ctx).store.snapshot();
        return { roots: snap.roots, connected: snap.connected };
      },
    },
    {
      name: "get_element",
      timeoutMs: 10000, // pure-data agent call — fail fast (not the 30s cap) so a momentarily-stale agent can't wedge the turn.
      description: "Return details for a single element by id (type, automationId, text, value, bounds, isVisible, isEnabled, state).",
      inputSchema: { type: "object", required: ["id"], properties: { id: { type: "string" } } },
      handler: async (ctx) => {
        const el = await requireInstance(ctx).store.getElementLive(ctx.input?.id);
        if (!el) throw new CanvasError("not_found", `unknown element: ${ctx.input?.id}`);
        return el;
      },
    },
    {
      name: "query",
      timeoutMs: 10000, // pure-data agent call — fail fast (not the 30s cap) so a momentarily-stale agent can't wedge the turn.
      description: "Find elements by type, AutomationId, text, or CSS-like selector.",
      inputSchema: {
        type: "object",
        properties: {
          type: { type: "string" },
          automationId: { type: "string" },
          text: { type: "string" },
          selector: { type: "string" },
        },
      },
      handler: async (ctx) => {
        const r = await requireInstance(ctx).store.query(ctx.input || {});
        if (!r.ok) throw new CanvasError("bad_input", r.error || "query failed");
        return { elements: r.elements };
      },
    },
    {
      name: "hit_test",
      timeoutMs: 10000, // pure-data agent call — fail fast (not the 30s cap) so a momentarily-stale agent can't wedge the turn.
      description: "Find the deepest element at device coordinates (x, y) in the app's logical pixel space, and select it on the canvas.",
      inputSchema: {
        type: "object", required: ["x", "y"],
        properties: { x: { type: "number" }, y: { type: "number" } },
      },
      handler: async (ctx) => {
        const el = await requireInstance(ctx).store.hitTestSelect(Number(ctx.input?.x), Number(ctx.input?.y));
        if (!el) throw new CanvasError("not_found", `no element at (${ctx.input?.x}, ${ctx.input?.y})`);
        return el;
      },
    },
    {
      name: "select_element",
      description: "Select an element by id so it is highlighted on the canvas and shown in the property grid.",
      inputSchema: { type: "object", required: ["id"], properties: { id: { type: "string" } } },
      handler: async (ctx) => {
        const el = requireInstance(ctx).store.select(ctx.input?.id);
        if (!el) throw new CanvasError("not_found", `unknown element: ${ctx.input?.id}`);
        return el;
      },
    },
    {
      name: "get_selection",
      description:
        "Return the element the human currently has selected in the canvas, as rich context: " +
        "id, type, automationId, text, bounds, visibility/enabled state, plus a one-line summary " +
        "and suggested follow-up actions. Call this whenever the user refers to 'the selected/" +
        "highlighted element' or 'what I clicked'. Returns { selectedId:null, hint } when nothing is selected. " +
        "Note: the human can also press 'Attach to Copilot' in the canvas to push this selection into the " +
        "composer as a context pill, in which case you already have it as message context.",
      handler: async (ctx) => {
        return requireInstance(ctx).store.selectionContext();
      },
    },
    {
      name: "attach_selection",
      description:
        "Attach the human's current canvas selection as bounded context for the next user message. " +
        "Same as the human " +
        "pressing 'Attach to Copilot' in the canvas. Returns { ok:false } with a reason when nothing " +
        "is selected or the host build can't receive pushed context.",
      handler: async (ctx) => {
        return pushSelectionContext(ctx.instanceId, requireInstance(ctx).store);
      },
    },
    {
      name: "get_property",
      timeoutMs: 10000, // pure-data agent call — fail fast (not the 30s cap) so a momentarily-stale agent can't wedge the turn.
      description: "Read a single property value from an element on the running app.",
      inputSchema: {
        type: "object", required: ["id", "name"],
        properties: { id: { type: "string" }, name: { type: "string" } },
      },
      handler: async (ctx) => {
        const r = await requireInstance(ctx).store.getProperty(ctx.input?.id, ctx.input?.name);
        if (!r.ok) throw new CanvasError("not_found", r.error || "read failed");
        return { value: r.value };
      },
    },
    {
      name: "set_property",
      description: "Live-edit a property on an element in the running app (e.g. Text). The change is reflected on the next screenshot.",
      inputSchema: {
        type: "object", required: ["id", "value"],
        properties: {
          id: { type: "string" },
          name: { type: "string", description: "Property name, e.g. 'Text'.", default: "Text" },
          value: { type: "string" },
        },
      },
      handler: async (ctx) => {
        const { id, name, value } = ctx.input || {};
        const r = await requireInstance(ctx).store.setProperty(id, name || "Text", value);
        if (!r.ok) throw new CanvasError("bad_input", r.error || "set-property failed");
        return { ok: true };
      },
    },
    {
      name: "apply_and_verify",
      description: "Set a property, then read it back and confirm it changed — returns { verified, expected, actual }.",
      inputSchema: {
        type: "object", required: ["id", "value"],
        properties: {
          id: { type: "string" },
          name: { type: "string", default: "Text" },
          value: { type: "string" },
        },
      },
      handler: async (ctx) => {
        const { id, name, value } = ctx.input || {};
        const r = await requireInstance(ctx).store.applyAndVerify(id, name || "Text", value);
        if (!r.ok) throw new CanvasError("bad_input", r.error || "apply failed");
        return r;
      },
    },
    {
      name: "tap",
      description: "Tap an element by id (or provide automationId/text) on the running app.",
      inputSchema: {
        type: "object",
        properties: { id: { type: "string" }, automationId: { type: "string" }, text: { type: "string" } },
      },
      handler: async (ctx) => {
        const r = await requireInstance(ctx).store.tap(ctx.input || {});
        if (!r.ok) throw new CanvasError("not_found", r.error || "tap failed");
        return { ok: true };
      },
    },
    {
      name: "fill",
      description: "Type text into an Entry/Editor element by id on the running app.",
      inputSchema: {
        type: "object", required: ["id", "text"],
        properties: { id: { type: "string" }, text: { type: "string" } },
      },
      handler: async (ctx) => {
        const r = await requireInstance(ctx).store.fill({ id: ctx.input?.id }, ctx.input?.text ?? "");
        if (!r.ok) throw new CanvasError("bad_input", r.error || "fill failed");
        return { ok: true };
      },
    },
    {
      name: "scroll",
      description: "Scroll a scrollable element by delta, to an item index, or to a named position.",
      inputSchema: {
        type: "object",
        properties: {
          element: { type: "string" },
          dx: { type: "number" }, dy: { type: "number" },
          itemIndex: { type: "number" },
          position: { type: "string" },
        },
      },
      handler: async (ctx) => {
        const r = await requireInstance(ctx).store.scroll(ctx.input || {});
        if (!r.ok) throw new CanvasError("bad_input", r.error || "scroll failed");
        return { ok: true };
      },
    },
    {
      name: "navigate",
      description: "Navigate the app to a Shell route (e.g. '//home' or a registered route name).",
      inputSchema: {
        type: "object", required: ["route"],
        properties: { route: { type: "string" } },
      },
      handler: async (ctx) => {
        const r = await requireInstance(ctx).store.navigate(ctx.input?.route);
        if (!r.ok) throw new CanvasError("bad_input", r.error || "navigate failed");
        return { ok: true };
      },
    },
    {
      name: "back",
      description: "Navigate back in the app's navigation stack.",
      handler: async (ctx) => {
        const r = await requireInstance(ctx).store.back();
        if (!r.ok) throw new CanvasError("unavailable", r.error || "back failed");
        return { ok: true };
      },
    },
    {
      name: "resize",
      description: "Resize the app window to width×height (desktop platforms). Useful for testing adaptive/responsive layouts.",
      inputSchema: {
        type: "object", required: ["width", "height"],
        properties: { width: { type: "number" }, height: { type: "number" } },
      },
      handler: async (ctx) => {
        const r = await requireInstance(ctx).store.resize(Number(ctx.input?.width), Number(ctx.input?.height));
        if (!r.ok) throw new CanvasError("unavailable", r.error || "resize failed");
        return { ok: true, window: requireInstance(ctx).store.snapshot().info?.window };
      },
    },
    {
      name: "set_theme",
      description: "Set the app theme to 'light', 'dark', or 'system'.",
      inputSchema: {
        type: "object", required: ["theme"],
        properties: { theme: { type: "string", enum: ["light", "dark", "system"] } },
      },
      handler: async (ctx) => {
        const r = await requireInstance(ctx).store.setTheme(ctx.input?.theme || "light");
        if (!r.ok) throw new CanvasError("bad_input", r.error || "set-theme failed");
        return { ok: true };
      },
    },
    {
      name: "screenshot",
      description: "Capture a fresh screenshot of the running app. The image is shown live in the canvas; returns a text summary.",
      handler: async (ctx) => {
        const snap = await requireInstance(ctx).store.refresh({ shot: true });
        const count = (function c(rs) { let n = 0; for (const e of rs || []) { n += 1 + c(e.children); } return n; })(snap.roots);
        return { captured: snap.connected, app: snap.info?.appName, platform: snap.info?.platform, elements: count, shotSeq: snap.shotSeq };
      },
    },
    {
      name: "get_logs",
      description: "Return recent app logs (ILogger + WebView console) from the running app.",
      inputSchema: { type: "object", properties: { limit: { type: "number", default: 100 } } },
      handler: async (ctx) => {
        const r = await requireInstance(ctx).store.getLogs(ctx.input?.limit || 100);
        if (!r.ok) throw new CanvasError("unavailable", r.error || "logs unavailable");
        return r.data;
      },
    },
    // ── Workflow Test Recorder (record -> .md -> replay-to-verify) ─────────────
    {
      name: "start_recording",
      description:
        "Begin recording a workflow test. Every subsequent tap/fill/scroll/navigate/back/set-theme/set-property " +
        "performed on the app — by the human in the canvas OR by you — is captured as a normalized, replayable step " +
        "with a durable selector, a screenshot, and auto-assertions. Call stop_and_save_test to write the .md test.",
      inputSchema: {
        type: "object",
        properties: {
          name: { type: "string", description: "Scenario name, e.g. 'add a subscription'." },
          preconditions: { type: "string", description: "Optional starting-state note, e.g. 'App on the Subscriptions page'." },
        },
      },
      handler: async (ctx) => {
        const st = requireInstance(ctx);
        const r = await st.store.device.recordingStart({
          name: ctx.input?.name,
          preconditions: ctx.input?.preconditions,
        });
        st.store._recordingStatus = r;
        st.store._emit();
        return r;
      },
    },
    {
      name: "get_recording",
      description:
        "Return the current in-progress recording: scenario name, step count, and each captured step (action, target " +
        "selector, value, assertions, fragile flag). Use this to review or narrate the steps before saving.",
      handler: async (ctx) => {
        const st = requireInstance(ctx);
        const r = await st.store.device.recordingStatus();
        st.store._recordingStatus = r;
        st.store._emit();
        return r;
      },
    },
    {
      name: "stop_and_save_test",
      description:
        "Stop recording and write the workflow as a Markdown test into the MAUI project's maui-tests/ folder (with " +
        "per-step screenshots). Returns the saved file path. Optionally pass name/preconditions to set them before saving.",
      inputSchema: {
        type: "object",
        properties: {
          name: { type: "string", description: "Override the scenario name before saving." },
          preconditions: { type: "string", description: "Override the preconditions note before saving." },
        },
      },
      handler: async (ctx) => {
        const st = requireInstance(ctx);
        const stopped = await st.store.device.recordingStop(st.store._recordingStatus?.recordingId);
        st.store._recordingStatus = stopped;
        st.store._emit();
        if (!stopped.ok) throw new CanvasError("unavailable", stopped.error || "stop failed");
        const r = persistRecording(st, {
          markdown: stopped.markdown,
          name: ctx.input?.name || stopped.name,
        });
        if (!r.ok) throw new CanvasError("unavailable", r.error || "save failed");
        return r;
      },
    },
    {
      name: "save_test",
      description: "Stop the shared recording and write it to disk as a Markdown test. Returns the file path.",
      handler: async (ctx) => {
        const st = requireInstance(ctx);
        const stopped = await st.store.device.recordingStop(st.store._recordingStatus?.recordingId);
        st.store._recordingStatus = stopped;
        st.store._emit();
        if (!stopped.ok) throw new CanvasError("unavailable", stopped.error || "stop failed");
        const r = persistRecording(st, { markdown: stopped.markdown, name: stopped.name });
        if (!r.ok) throw new CanvasError("unavailable", r.error || "save failed");
        return r;
      },
    },
    {
      name: "list_tests",
      description: "List saved workflow tests (.md files) in the MAUI project's maui-tests/ folder.",
      handler: async (ctx) => {
        const st = requireInstance(ctx);
        return st.recorder.list(st.store);
      },
    },
    {
      name: "replay_test",
      description:
        "Replay a recorded workflow test through the shared Inspector's canonical C# FlowReplayer. Pass a scenario " +
        "'name' from list_tests, or a top-level Markdown 'file' inside the resolved maui-tests directory. Returns the " +
        "same per-step pass/fail report as CLI, MCP, and the Inspector UI.",
      timeoutMs: 120000,
      inputSchema: {
        type: "object",
        properties: {
          name: { type: "string", description: "Scenario name to replay (see list_tests)." },
          file: { type: "string", description: "Top-level test .md path inside the resolved maui-tests directory (alternative to name)." },
        },
      },
      handler: async (ctx) => {
        const st = requireInstance(ctx);
        const status = await st.store.device.recordingStatus();
        if (status.recording) {
          throw new CanvasError("bad_input", "Stop or cancel the active recording before replaying a test.");
        }
        const r = await replaySharedFlow(st, {
          name: ctx.input?.name,
          file: ctx.input?.file,
        });
        if (!r.ok && r.error) throw new CanvasError("bad_input", r.error);
        return r;
      },
    },
  ]),

  open: async (ctx) => {
    const url = await startServer(ctx.instanceId, {
      ...(ctx.input || {}),
      workingDirectory: ctx.session?.workingDirectory,
    });
    const st = requireInstance(ctx);
    // Kick off the first live pull (tree + screenshot) without blocking the panel open,
    // then start the live-sync poll loop so changes made directly in the app show up here.
    st.store
      .refresh()
      .catch(() => {})
      .finally(() => st.store.startLiveSync());
    return { title: "MAUI DevFlow Inspector", status: "ready", url };
  },

  onClose: async (ctx) => {
    const st = instances.get(ctx.instanceId);
    if (st) {
      try {
        st.store.stopLiveSync();
      } catch {
        /* ignore */
      }
      if (st.server) {
        try {
          st.server.close();
        } catch {
          /* ignore */
        }
      }
      for (const response of st.sse) {
        try {
          response.end();
        } catch {
          /* ignore */
        }
      }
      st.sse.clear();
      for (const response of st.recordingSse) {
        try {
          response.end();
        } catch {
          /* ignore */
        }
      }
      st.recordingSse.clear();
      try {
        await st.store.device.releaseMutationLease();
      } catch {
        /* best effort */
      }
      try {
        st.store.device.dispose();
      } catch {
        /* ignore */
      }
    }
    instances.delete(ctx.instanceId);
  },
});

sharedSession = await joinSession({ canvases: [canvas] });
await new Promise(() => {});
