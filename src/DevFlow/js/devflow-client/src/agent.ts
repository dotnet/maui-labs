// Typed agent operations, expressed as pure request descriptors. The facade
// (index.ts) executes them through the resolver so retry/target/error handling lives in
// ONE place. Keeping these pure (no dependency on resolve.ts) keeps the module graph
// one-way and makes parsing/query-building unit-testable.

import type { AgentStatus, ElementInfo, ThemeResult } from "./types.js";

const API = "/api/v1";
const UI = `${API}/ui`;
const DEVICE = `${API}/device`;

/**
 * A single agent request. `idempotent` marks reads (safe to auto-retry on a dropped
 * socket); mutations are false and only retried when the caller opts into
 * `retryMutations`. `appError` reports an application-level failure on an HTTP 2xx (e.g.
 * an action whose body is `{ success:false }`).
 */
export interface AgentRequest<T> {
  method: "GET" | "POST" | "PUT" | "DELETE";
  path: string;
  body?: unknown;
  idempotent: boolean;
  operation: string;
  appError?: (data: unknown) => string | null;
  parse: (data: unknown) => T;
}

// ── Parsing helpers ──────────────────────────────────────────────────────────

/** Normalize a tree/hit-test/query payload into an array of nodes (array | {elements} | {tree} | bare). */
export function toRoots(data: unknown): ElementInfo[] {
  if (!data) return [];
  if (Array.isArray(data)) return data as ElementInfo[];
  const o = data as Record<string, unknown>;
  if (Array.isArray(o.elements)) return o.elements as ElementInfo[];
  if (Array.isArray(o.tree)) return o.tree as ElementInfo[];
  if (o.tree && typeof o.tree === "object") return [o.tree as ElementInfo];
  if (o.root && typeof o.root === "object") return [o.root as ElementInfo];
  if (o.id || o.type) return [data as ElementInfo];
  return [];
}

function parseSingleElement(data: unknown): ElementInfo | null {
  if (data && typeof data === "object") {
    const o = data as Record<string, unknown>;
    if (o.id || o.type) return data as ElementInfo;
    if (o.element && typeof o.element === "object") return o.element as ElementInfo;
  }
  return null;
}

function parsePropertyValue(data: unknown, name?: string): string | null {
  if (data == null) return null;
  if (typeof data === "object") {
    const o = data as Record<string, unknown>;
    // Preserve the compatibility fallback order so agents that report a property under
    // `result` or the property's own name (rather than `value`) still resolve.
    const v = o.value ?? o.result ?? (name != null ? o[name] : undefined);
    return v == null ? null : String(v);
  }
  return String(data);
}

/** Extract an agent-provided error/message string from a response body, if any. */
export function agentErrorMessage(data: unknown): string | null {
  if (data && typeof data === "object") {
    const o = data as Record<string, unknown>;
    if (typeof o.error === "string" && o.error) return o.error;
    if (typeof o.message === "string" && o.message) return o.message;
  }
  return null;
}

/** App-level failure check for POST actions (HTTP was already 2xx). */
function actionAppError(data: unknown): string | null {
  if (data && typeof data === "object" && (data as Record<string, unknown>).success === false) {
    return agentErrorMessage(data) ?? "action reported failure";
  }
  return null;
}

/** Build a `?a=b&c=d` query string, skipping undefined/null values. */
export function buildQuery(
  params: Record<string, string | number | boolean | undefined | null>,
): string {
  const parts: string[] = [];
  for (const [k, v] of Object.entries(params)) {
    if (v === undefined || v === null) continue;
    parts.push(`${encodeURIComponent(k)}=${encodeURIComponent(String(v))}`);
  }
  return parts.length ? `?${parts.join("&")}` : "";
}

/**
 * Derive a swipe direction + distance from a drag path (first→last point). The agent's
 * gesture endpoint takes a typed swipe, not a raw points array — this mirrors the
 * InspectorServer's translation so coordinate drags work end-to-end.
 */
export function directionFromPoints(
  points: Array<{ x: number; y: number; t?: number }>,
): { direction: string; distance: number } | null {
  if (!points || points.length < 2) return null;
  const f = points[0];
  const l = points[points.length - 1];
  const dx = l.x - f.x;
  const dy = l.y - f.y;
  const direction = Math.abs(dx) > Math.abs(dy) ? (dx > 0 ? "right" : "left") : (dy > 0 ? "down" : "up");
  return { direction, distance: Math.hypot(dx, dy) };
}

const noop = (): void => undefined;

// ── Request builders ─────────────────────────────────────────────────────────

export interface ScrollArgs {
  elementId?: string;
  deltaX?: number;
  deltaY?: number;
  animated?: boolean;
  itemIndex?: number;
  groupIndex?: number;
  scrollToPosition?: string;
  window?: number;
  // Coordinate form (inspector-style): scroll at a point by a wheel delta.
  x?: number;
  y?: number;
}

export interface GestureArgs {
  type?: string;
  elementId?: string;
  direction?: string;
  distance?: number;
  durationMs?: number;
  points?: Array<{ x: number; y: number; t?: number }>;
}

export const requests = {
  status(window?: number): AgentRequest<AgentStatus | null> {
    return {
      method: "GET",
      path: `${API}/agent/status${buildQuery({ window })}`,
      idempotent: true,
      operation: "status",
      parse: (d) => (d && typeof d === "object" ? (d as AgentStatus) : null),
    };
  },

  tree(depth?: number, window?: number): AgentRequest<ElementInfo[]> {
    return {
      method: "GET",
      path: `${UI}/tree${buildQuery({ depth: depth && depth > 0 ? depth : undefined, window })}`,
      idempotent: true,
      operation: "tree",
      parse: toRoots,
    };
  },

  element(id: string): AgentRequest<ElementInfo | null> {
    return {
      method: "GET",
      path: `${UI}/elements/${encodeURIComponent(id)}`,
      idempotent: true,
      operation: "element",
      parse: parseSingleElement,
    };
  },

  query(q: { type?: string; automationId?: string; text?: string }): AgentRequest<ElementInfo[]> {
    return {
      method: "GET",
      path: `${UI}/elements${buildQuery({ type: q.type, automationId: q.automationId, text: q.text })}`,
      idempotent: true,
      operation: "query",
      parse: toRoots,
    };
  },

  queryCss(selector: string): AgentRequest<ElementInfo[]> {
    return {
      method: "GET",
      path: `${UI}/elements${buildQuery({ selector })}`,
      idempotent: true,
      operation: "queryCss",
      parse: toRoots,
    };
  },

  hitTest(x: number, y: number, window?: number): AgentRequest<ElementInfo[]> {
    return {
      method: "GET",
      path: `${UI}/hit-test${buildQuery({ x, y, window })}`,
      idempotent: true,
      operation: "hitTest",
      parse: toRoots,
    };
  },

  getProperty(id: string, name: string): AgentRequest<string | null> {
    return {
      method: "GET",
      path: `${UI}/elements/${encodeURIComponent(id)}/properties/${encodeURIComponent(name)}`,
      idempotent: true,
      operation: "getProperty",
      parse: (d) => parsePropertyValue(d, name),
    };
  },

  setProperty(id: string, name: string, value: string): AgentRequest<void> {
    return {
      method: "PUT",
      path: `${UI}/elements/${encodeURIComponent(id)}/properties/${encodeURIComponent(name)}`,
      body: { value: String(value) },
      idempotent: false,
      operation: "setProperty",
      appError: agentErrorMessage,
      parse: noop,
    };
  },

  tapElement(elementId: string): AgentRequest<void> {
    return action("tap", `${UI}/actions/tap`, { elementId });
  },
  fill(elementId: string, text: string): AgentRequest<void> {
    return action("fill", `${UI}/actions/fill`, { elementId, text: String(text) });
  },
  clear(elementId: string): AgentRequest<void> {
    return action("clear", `${UI}/actions/clear`, { elementId });
  },
  focus(elementId: string): AgentRequest<void> {
    return action("focus", `${UI}/actions/focus`, { elementId });
  },
  back(): AgentRequest<void> {
    return action("back", `${UI}/actions/back`, {});
  },
  navigate(route: string): AgentRequest<void> {
    return action("navigate", `${UI}/actions/navigate`, { route: String(route) });
  },
  key(key: string, elementId?: string, text?: string): AgentRequest<void> {
    return action("key", `${UI}/actions/key`, { key, elementId, text });
  },
  resize(width: number, height: number, window?: number): AgentRequest<void> {
    return action("resize", `${UI}/actions/resize${buildQuery({ window })}`, { width, height });
  },

  // Element/delta scroll. Coordinate (x/y) scrolls are resolved to an element by the
  // facade via hit-test (the agent's scroll endpoint has no x/y), so x/y are ignored here.
  scroll(args: ScrollArgs): AgentRequest<void> {
    const body: Record<string, unknown> = {};
    body.deltaX = args.deltaX ?? 0;
    body.deltaY = args.deltaY ?? 0;
    body.animated = args.animated ?? false;
    if (args.elementId) body.elementId = args.elementId;
    if (args.itemIndex !== undefined) body.itemIndex = args.itemIndex;
    if (args.groupIndex !== undefined) body.groupIndex = args.groupIndex;
    if (args.scrollToPosition) body.scrollToPosition = args.scrollToPosition;
    return action("scroll", `${UI}/actions/scroll${buildQuery({ window: args.window })}`, body);
  },

  gesture(args: GestureArgs): AgentRequest<void> {
    const body: Record<string, unknown> = {};
    // The agent requires a typed gesture (`type`). A raw drag "points" array (inspector
    // style) is translated to a typed swipe + direction; the agent does not accept points.
    if (!args.type && args.points && args.points.length >= 2) {
      const d = directionFromPoints(args.points);
      body.type = "swipe";
      if (d) {
        body.direction = d.direction;
        body.distance = d.distance;
      }
      if (args.elementId) body.elementId = args.elementId;
    } else {
      body.type = args.type ?? "swipe";
      if (args.direction) body.direction = args.direction;
      if (args.distance !== undefined) body.distance = args.distance;
      if (args.elementId) body.elementId = args.elementId;
    }
    if (args.durationMs !== undefined) body.durationMs = args.durationMs;
    return action("gesture", `${UI}/actions/gesture`, body);
  },

  themeGet(): AgentRequest<ThemeResult | null> {
    return {
      method: "GET",
      path: `${DEVICE}/app/theme`,
      idempotent: true,
      operation: "themeGet",
      parse: (d) => (d && typeof d === "object" ? (d as ThemeResult) : null),
    };
  },
  themeSet(theme: string): AgentRequest<ThemeResult | null> {
    return {
      method: "PUT",
      path: `${DEVICE}/app/theme`,
      body: { theme },
      idempotent: false,
      operation: "themeSet",
      parse: (d) => (d && typeof d === "object" ? (d as ThemeResult) : null),
    };
  },
} as const;

function action(operation: string, path: string, body: unknown): AgentRequest<void> {
  return { method: "POST", path, body, idempotent: false, operation, appError: actionAppError, parse: noop };
}
