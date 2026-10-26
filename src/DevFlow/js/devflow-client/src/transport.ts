// The Transport seam: a STRICT typed allow-list of operations a host (Copilot Canvas
// loopback server, VS Code extension host) may expose to an untrusted webview. This is a
// security boundary, NOT a generic "forward any HTTP" pipe: an XSS in a shared UI
// bundle must not become a local-network / workspace escape. Beyond the allow-list it
// adds (1) permission modes (read / screenshot / mutate / setProperty) and (2) strict
// payload validation (bounded coordinates, capped text, key allow-list, guarded property
// writes). Every result carries the target identity so the host can show what is controlled.

import { err } from "./types.js";
import type { AgentTarget, DevFlowError, DevFlowResult, ElementInfo } from "./types.js";
import type { DevFlowEvent, EventStreamHandle } from "./events.js";
import type { ScrollArgs, GestureArgs } from "./agent.js";

/** The complete set of operations a webview may request through a host proxy. */
export type SeamOp =
  | { kind: "getState" }
  | { kind: "getTree"; depth?: number }
  | { kind: "getScreenshot"; elementId?: string }
  | { kind: "tap"; x?: number; y?: number; elementId?: string }
  | { kind: "scroll"; x: number; y: number; deltaX: number; deltaY: number }
  | { kind: "gesture"; points: Array<{ x: number; y: number; t?: number }> }
  | { kind: "back" }
  | { kind: "fill"; elementId: string; text: string }
  | { kind: "key"; elementId?: string; key: string; text?: string }
  | { kind: "setProperty"; elementId: string; name: string; value: string };

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
}

/** Structural subset of DevFlowClient the transport needs (so this module has no cycle). */
export interface TransportClient {
  readonly target: AgentTarget | null;
  getTree(depth?: number): Promise<DevFlowResult<ElementInfo[]>>;
  screenshot(opts?: { elementId?: string }): Promise<DevFlowResult<Buffer>>;
  tap(target: { x?: number; y?: number; elementId?: string }): Promise<DevFlowResult<void>>;
  scroll(args: ScrollArgs): Promise<DevFlowResult<void>>;
  gesture(args: GestureArgs): Promise<DevFlowResult<void>>;
  back(): Promise<DevFlowResult<void>>;
  fill(elementId: string, text: string): Promise<DevFlowResult<void>>;
  key(key: string, elementId?: string, text?: string): Promise<DevFlowResult<void>>;
  setProperty(elementId: string, name: string, value: string): Promise<DevFlowResult<void>>;
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

/** Create a validated, permission-gated Transport over a DevFlow client. */
export function createTransport(client: TransportClient, opts: TransportOptions = {}): Transport {
  const perms = opts.permissions ?? READ_ONLY;
  const maxCoord = opts.maxCoordinate ?? 100000;
  const maxText = opts.maxTextLength ?? 100000;
  const maxPoints = opts.maxGesturePoints ?? 512;
  const allowList = opts.propertyAllowList ? new Set(opts.propertyAllowList) : null;
  const handles = new Set<EventStreamHandle>();

  const deny = (message: string, kind: DevFlowError["kind"] = "permission-denied"): DevFlowResult<never> =>
    err({ kind, message, target: client.target ?? undefined, retriable: false });

  const finite = (n: unknown, limit: number): boolean =>
    typeof n === "number" && Number.isFinite(n) && Math.abs(n) <= limit;

  async function request(op: SeamOp): Promise<DevFlowResult<unknown>> {
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
        return client.tap({ x: op.x, y: op.y, elementId: op.elementId });
      }
      case "scroll": {
        if (!perms.mutate) return deny("mutate permission required for scroll");
        if (!finite(op.x, maxCoord) || !finite(op.y, maxCoord) || !finite(op.deltaX, maxCoord) || !finite(op.deltaY, maxCoord)) {
          return deny("scroll requires finite x/y/deltaX/deltaY", "invalid-argument");
        }
        return client.scroll({ x: op.x, y: op.y, deltaX: op.deltaX, deltaY: op.deltaY });
      }
      case "gesture": {
        if (!perms.mutate) return deny("mutate permission required for gesture");
        if (!Array.isArray(op.points) || op.points.length < 2 || op.points.length > maxPoints) {
          return deny(`gesture requires 2..${maxPoints} points`, "invalid-argument");
        }
        for (const p of op.points) {
          if (!finite(p?.x, maxCoord) || !finite(p?.y, maxCoord)) return deny("gesture point out of range", "invalid-argument");
        }
        return client.gesture({ points: op.points });
      }
      case "back": {
        if (!perms.mutate) return deny("mutate permission required for back");
        return client.back();
      }
      case "fill": {
        if (!perms.mutate) return deny("mutate permission required for fill");
        if (typeof op.elementId !== "string" || !op.elementId) return deny("fill requires elementId", "invalid-argument");
        if (typeof op.text !== "string" || op.text.length > maxText) return deny(`fill text must be a string <= ${maxText} chars`, "invalid-argument");
        return client.fill(op.elementId, op.text);
      }
      case "key": {
        if (!perms.mutate) return deny("mutate permission required for key");
        if (typeof op.key !== "string" || !op.key || op.key.length > 32) return deny("invalid key", "invalid-argument");
        if (op.key.length > 1 && !NAMED_KEYS.has(op.key.toLowerCase())) return deny(`key not allowed: ${op.key}`, "invalid-argument");
        if (op.text !== undefined && (typeof op.text !== "string" || op.text.length > maxText)) return deny("invalid key text", "invalid-argument");
        return client.key(op.key, op.elementId, op.text);
      }
      case "setProperty": {
        if (!perms.setProperty) return deny("setProperty permission required (off by default)");
        if (typeof op.elementId !== "string" || !op.elementId) return deny("setProperty requires elementId", "invalid-argument");
        if (typeof op.name !== "string" || !op.name) return deny("setProperty requires name", "invalid-argument");
        if (allowList && !allowList.has(op.name)) return deny(`property not allowed: ${op.name}`);
        if (typeof op.value !== "string" || op.value.length > maxText) return deny("setProperty value must be a string within the length cap", "invalid-argument");
        return client.setProperty(op.elementId, op.name, op.value);
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
