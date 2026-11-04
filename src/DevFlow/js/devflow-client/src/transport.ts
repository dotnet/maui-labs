// The Transport seam: a STRICT typed allow-list of operations a host (Copilot Canvas
// loopback server, VS Code extension host) may expose to an untrusted webview. This is a
// security boundary, NOT a generic "forward any HTTP" pipe: an XSS in a shared UI
// bundle must not become a local-network / workspace escape. Beyond the allow-list it
// adds (1) permission modes (read / screenshot / mutate / setProperty) and (2) strict
// payload validation (bounded coordinates, capped text, key allow-list, guarded property
// writes). Every result carries the target identity so the host can show what is controlled.

import { err } from "./types.js";
import type {
  ActionCorrelation,
  AgentTarget,
  DevFlowError,
  DevFlowResult,
  ElementInfo,
  PointerSessionResult,
  PointerSessionSample,
} from "./types.js";
import type { DevFlowEvent, EventStreamHandle } from "./events.js";
import type { ScrollArgs, GestureArgs, PointerSessionStartArgs } from "./agent.js";

/** The complete set of operations a webview may request through a host proxy. */
export type SeamOp =
  | { kind: "getState" }
  | { kind: "getTree"; depth?: number }
  | { kind: "getScreenshot"; elementId?: string }
  | { kind: "tap"; x?: number; y?: number; elementId?: string; correlation?: ActionCorrelation }
  | { kind: "scroll"; x: number; y: number; deltaX: number; deltaY: number; correlation?: ActionCorrelation }
  | { kind: "gesture"; points: Array<{ x: number; y: number; t?: number }>; correlation?: ActionCorrelation }
  | { kind: "pointerStart"; value: PointerSessionStartArgs }
  | { kind: "pointerMove"; sessionId: string; sequence: number; samples: PointerSessionSample[] }
  | { kind: "pointerEnd"; sessionId: string; sequence: number; sample: PointerSessionSample }
  | { kind: "pointerCancel"; sessionId: string; reason?: string }
  | { kind: "back"; correlation?: ActionCorrelation }
  | { kind: "fill"; elementId: string; text: string; correlation?: ActionCorrelation }
  | { kind: "key"; elementId?: string; key: string; text?: string; correlation?: ActionCorrelation }
  | { kind: "setProperty"; elementId: string; name: string; value: string; correlation?: ActionCorrelation };

export interface SeamPermissions {
  /** Read the tree / current state. */
  read: boolean;
  /** Capture screenshots (privacy-sensitive — pixels can contain user data). */
  screenshot: boolean;
  /** tap / scroll / gesture / back / fill / key. */
  mutate: boolean;
  /** Live property edits — the riskiest op; gated separately and off by default. */
  setProperty: boolean;
}

export const READ_ONLY: SeamPermissions = { read: true, screenshot: false, mutate: false, setProperty: false };
export const READ_SCREENSHOT: SeamPermissions = { read: true, screenshot: true, mutate: false, setProperty: false };
export const INTERACT: SeamPermissions = { read: true, screenshot: true, mutate: true, setProperty: false };
export const FULL: SeamPermissions = { read: true, screenshot: true, mutate: true, setProperty: true };

export interface TransportOptions {
  permissions?: SeamPermissions;
  /**
   * If provided, `setProperty` may only target property names in this set (in addition
   * to requiring the `setProperty` permission). Recommended for webview-exposed proxies.
   */
  propertyAllowList?: Iterable<string>;
  /** Max absolute coordinate / delta value. Default 100000. */
  maxCoordinate?: number;
  /** Max text length for fill/key. Default 100000. */
  maxTextLength?: number;
  /** Max gesture point count. Default 512. */
  maxGesturePoints?: number;
  /** Max coalesced samples in one pointer move. Default 64. */
  maxPointerSamples?: number;
}

/** Structural subset of DevFlowClient the transport needs (so this module has no cycle). */
export interface TransportClient {
  readonly target: AgentTarget | null;
  getTree(depth?: number): Promise<DevFlowResult<ElementInfo[]>>;
  screenshot(opts?: { elementId?: string }): Promise<DevFlowResult<Buffer>>;
  tap(target: { x?: number; y?: number; elementId?: string; correlation?: ActionCorrelation }): Promise<DevFlowResult<void>>;
  scroll(args: ScrollArgs): Promise<DevFlowResult<void>>;
  gesture(args: GestureArgs): Promise<DevFlowResult<void>>;
  pointerStart(args: PointerSessionStartArgs): Promise<DevFlowResult<PointerSessionResult>>;
  pointerMove(sessionId: string, sequence: number, samples: PointerSessionSample[]): Promise<DevFlowResult<PointerSessionResult>>;
  pointerEnd(sessionId: string, sequence: number, sample: PointerSessionSample): Promise<DevFlowResult<PointerSessionResult>>;
  pointerCancel(sessionId: string, reason?: string): Promise<DevFlowResult<PointerSessionResult>>;
  back(correlation?: ActionCorrelation): Promise<DevFlowResult<void>>;
  fill(elementId: string, text: string, correlation?: ActionCorrelation): Promise<DevFlowResult<void>>;
  key(key: string, elementId?: string, text?: string, correlation?: ActionCorrelation): Promise<DevFlowResult<void>>;
  setProperty(elementId: string, name: string, value: string, correlation?: ActionCorrelation): Promise<DevFlowResult<void>>;
  openEvents(handlers: {
    onEvent: (e: DevFlowEvent) => void;
    onStatus?: (s: { connected: boolean }) => void;
  }): EventStreamHandle;
}

export interface Transport {
  request(op: SeamOp): Promise<DevFlowResult<unknown>>;
  subscribe(onEvent: (e: DevFlowEvent) => void): () => void;
}

const NAMED_KEYS = new Set([
  "enter", "return", "tab", "escape", "esc", "backspace", "delete", "del", "space",
  "up", "down", "left", "right", "home", "end", "pageup", "pagedown", "insert",
  "f1", "f2", "f3", "f4", "f5", "f6", "f7", "f8", "f9", "f10", "f11", "f12",
]);
const ACTION_SOURCES = new Set(["inspector", "cli", "mcp", "replay"]);
const COORDINATE_SPACES = new Set([
  "appLogical",
  "windowLogical",
  "screenshotPixel",
  "inspectorCss",
  "transformedViewport",
]);

/** Create a validated, permission-gated Transport over a DevFlow client. */
export function createTransport(client: TransportClient, opts: TransportOptions = {}): Transport {
  const perms = opts.permissions ?? READ_ONLY;
  const maxCoord = opts.maxCoordinate ?? 100000;
  const maxText = opts.maxTextLength ?? 100000;
  const maxPoints = opts.maxGesturePoints ?? 512;
  const maxPointerSamples = opts.maxPointerSamples ?? 64;
  const allowList = opts.propertyAllowList ? new Set(opts.propertyAllowList) : null;
  const handles = new Set<EventStreamHandle>();

  const deny = (message: string, kind: DevFlowError["kind"] = "permission-denied"): DevFlowResult<never> =>
    err({ kind, message, target: client.target ?? undefined, retriable: false });

  const finite = (n: unknown, limit: number): boolean =>
    typeof n === "number" && Number.isFinite(n) && Math.abs(n) <= limit;
  const validCorrelation = (value: unknown): value is ActionCorrelation => {
    if (!value || typeof value !== "object") return false;
    const correlation = value as Record<string, unknown>;
    if (typeof correlation.actionId !== "string" || !correlation.actionId || correlation.actionId.length > 128) return false;
    if (typeof correlation.frameId !== "string" || !correlation.frameId || correlation.frameId.length > 128) return false;
    if (typeof correlation.source !== "string" || !ACTION_SOURCES.has(correlation.source)) return false;
    if (typeof correlation.coordinateSpace !== "string" || !COORDINATE_SPACES.has(correlation.coordinateSpace)) return false;
    if (correlation.streamId !== undefined &&
        (typeof correlation.streamId !== "string" || !correlation.streamId || correlation.streamId.length > 128)) return false;
    return correlation.frameRevision === undefined ||
      (typeof correlation.frameRevision === "number" &&
        Number.isSafeInteger(correlation.frameRevision) &&
        correlation.frameRevision > 0);
  };

  function validPointerSessionOperation(sessionId: unknown, sequence: unknown): boolean {
    return typeof sessionId === "string" && sessionId.length > 0 && sessionId.length <= 96
      && Number.isSafeInteger(sequence) && (sequence as number) > 0;
  }

  function validPointerSample(sample: PointerSessionSample | undefined): boolean {
    return !!sample
      && finite(sample.x, maxCoord)
      && finite(sample.y, maxCoord)
      && finite(sample.elapsedMs, 300000)
      && sample.elapsedMs >= 0
      && (sample.pressure === undefined
        || (finite(sample.pressure, 1) && sample.pressure >= 0))
      && (sample.buttons === undefined
        || (Number.isSafeInteger(sample.buttons) && sample.buttons >= 0));
  }

  async function request(op: SeamOp): Promise<DevFlowResult<unknown>> {
    const correlation = (op as { correlation?: unknown }).correlation;
    if (correlation !== undefined && !validCorrelation(correlation)) {
      return deny("invalid action correlation", "invalid-argument");
    }
    switch (op.kind) {
      case "getState": {
        if (!perms.read) return deny("read permission required for getState");
        const tree = await client.getTree();
        if (!tree.ok) return tree;
        return { ok: true, value: { tree: tree.value }, target: tree.target };
      }
      case "getTree": {
        if (!perms.read) return deny("read permission required for getTree");
        if (op.depth !== undefined && !finite(op.depth, 1000)) return deny("invalid depth", "invalid-argument");
        return client.getTree(op.depth);
      }
      case "getScreenshot": {
        if (!perms.screenshot) return deny("screenshot permission required");
        return client.screenshot(op.elementId ? { elementId: op.elementId } : undefined);
      }
      case "tap": {
        if (!perms.mutate) return deny("mutate permission required for tap");
        if (op.elementId === undefined) {
          if (!finite(op.x, maxCoord) || !finite(op.y, maxCoord)) return deny("tap requires elementId or finite x/y", "invalid-argument");
        }
        return client.tap({ x: op.x, y: op.y, elementId: op.elementId, correlation: op.correlation });
      }
      case "scroll": {
        if (!perms.mutate) return deny("mutate permission required for scroll");
        if (!finite(op.x, maxCoord) || !finite(op.y, maxCoord) || !finite(op.deltaX, maxCoord) || !finite(op.deltaY, maxCoord)) {
          return deny("scroll requires finite x/y/deltaX/deltaY", "invalid-argument");
        }
        return client.scroll({
          x: op.x,
          y: op.y,
          deltaX: op.deltaX,
          deltaY: op.deltaY,
          correlation: op.correlation,
        });
      }
      case "gesture": {
        if (!perms.mutate) return deny("mutate permission required for gesture");
        if (!Array.isArray(op.points) || op.points.length < 2 || op.points.length > maxPoints) {
          return deny(`gesture requires 2..${maxPoints} points`, "invalid-argument");
        }
        for (const p of op.points) {
          if (!finite(p?.x, maxCoord) || !finite(p?.y, maxCoord)) return deny("gesture point out of range", "invalid-argument");
        }
        return client.gesture({ points: op.points, correlation: op.correlation });
      }
      case "pointerStart": {
        if (!perms.mutate) return deny("mutate permission required for pointerStart");
        const value = op.value;
        if (!value || typeof value.sessionId !== "string" || !value.sessionId || value.sessionId.length > 96) {
          return deny("pointerStart requires a bounded sessionId", "invalid-argument");
        }
        if (!Number.isSafeInteger(value.pointerId)
            || !finite(value.x, maxCoord)
            || !finite(value.y, maxCoord)
            || (value.elapsedMs !== undefined && !finite(value.elapsedMs, 300000))) {
          return deny("pointerStart requires finite pointerId/x/y/elapsedMs", "invalid-argument");
        }
        if ((value.coordinateSpace ?? "windowLogical") !== "windowLogical") {
          return deny("pointerStart supports only windowLogical coordinates", "invalid-argument");
        }
        if (value.correlation !== undefined
            && (!validCorrelation(value.correlation)
              || value.correlation.coordinateSpace !== "windowLogical")) {
          return deny("pointerStart correlation must describe windowLogical coordinates", "invalid-argument");
        }
        return client.pointerStart(value);
      }
      case "pointerMove": {
        if (!perms.mutate) return deny("mutate permission required for pointerMove");
        if (!validPointerSessionOperation(op.sessionId, op.sequence)) {
          return deny("pointerMove requires sessionId and a positive sequence", "invalid-argument");
        }
        if (!Array.isArray(op.samples) || op.samples.length > maxPointerSamples
            || op.samples.some(sample => !validPointerSample(sample))) {
          return deny(`pointerMove accepts at most ${maxPointerSamples} finite samples`, "invalid-argument");
        }
        return client.pointerMove(op.sessionId, op.sequence, op.samples);
      }
      case "pointerEnd": {
        if (!perms.mutate) return deny("mutate permission required for pointerEnd");
        if (!validPointerSessionOperation(op.sessionId, op.sequence) || !validPointerSample(op.sample)) {
          return deny("pointerEnd requires sessionId, sequence and a finite final sample", "invalid-argument");
        }
        return client.pointerEnd(op.sessionId, op.sequence, op.sample);
      }
      case "pointerCancel": {
        if (!perms.mutate) return deny("mutate permission required for pointerCancel");
        if (typeof op.sessionId !== "string" || !op.sessionId || op.sessionId.length > 96
            || (op.reason !== undefined && (typeof op.reason !== "string" || op.reason.length > 64))) {
          return deny("pointerCancel requires a bounded sessionId/reason", "invalid-argument");
        }
        return client.pointerCancel(op.sessionId, op.reason);
      }
      case "back": {
        if (!perms.mutate) return deny("mutate permission required for back");
        return client.back(op.correlation);
      }
      case "fill": {
        if (!perms.mutate) return deny("mutate permission required for fill");
        if (typeof op.elementId !== "string" || !op.elementId) return deny("fill requires elementId", "invalid-argument");
        if (typeof op.text !== "string" || op.text.length > maxText) return deny(`fill text must be a string <= ${maxText} chars`, "invalid-argument");
        return client.fill(op.elementId, op.text, op.correlation);
      }
      case "key": {
        if (!perms.mutate) return deny("mutate permission required for key");
        if (typeof op.key !== "string" || !op.key || op.key.length > 32) return deny("invalid key", "invalid-argument");
        if (op.key.length > 1 && !NAMED_KEYS.has(op.key.toLowerCase())) return deny(`key not allowed: ${op.key}`, "invalid-argument");
        if (op.text !== undefined && (typeof op.text !== "string" || op.text.length > maxText)) return deny("invalid key text", "invalid-argument");
        return client.key(op.key, op.elementId, op.text, op.correlation);
      }
      case "setProperty": {
        if (!perms.setProperty) return deny("setProperty permission required (off by default)");
        if (typeof op.elementId !== "string" || !op.elementId) return deny("setProperty requires elementId", "invalid-argument");
        if (typeof op.name !== "string" || !op.name) return deny("setProperty requires name", "invalid-argument");
        if (allowList && !allowList.has(op.name)) return deny(`property not allowed: ${op.name}`);
        if (typeof op.value !== "string" || op.value.length > maxText) return deny("setProperty value must be a string within the length cap", "invalid-argument");
        return client.setProperty(op.elementId, op.name, op.value, op.correlation);
      }
      default: {
        const _exhaustive: never = op;
        return deny(`unknown op: ${JSON.stringify(_exhaustive)}`, "invalid-argument");
      }
    }
  }

  function subscribe(onEvent: (e: DevFlowEvent) => void): () => void {
    if (!perms.read) {
      // Events are read-ish; without read permission return a no-op unsubscribe.
      return () => undefined;
    }
    const handle = client.openEvents({ onEvent });
    handles.add(handle);
    return () => {
      handles.delete(handle);
      handle.close();
    };
  }

  return { request, subscribe };
}
