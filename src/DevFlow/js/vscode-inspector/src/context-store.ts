import { randomBytes } from "crypto";
import type { AgentRegistration } from "@maui-devflow/client";

export interface SelectedElement {
  id?: string | null;
  type?: string;
  automationId?: string | null;
  text?: string | null;
  hasSource?: boolean;
  sourceFile?: string | null;
  sourceLine?: number | null;
  sourceColumn?: number | null;
}

export interface DataSnapshot {
  kind: "dataSnapshot";
  scope: "logs" | "network" | "preferences" | "device" | "sensors" | "files" | "alerts";
  title: string;
  appName?: string | null;
  capturedAt: string;
  itemCount?: number | null;
  truncated?: boolean;
  redacted?: boolean;
  dataFormat?: string;
  data: unknown;
  agent?: {
    id?: string | null;
    appName?: string | null;
    platform?: string | null;
    port?: number | null;
  };
  metadata?: Record<string, unknown>;
  followUpTools?: string[];
}

/**
 * Everything the host needs to answer a bridge request for one open Inspector panel. It is panel
 * scoped on purpose: a second panel targets a different app, and reusing one panel's agent or
 * selection for another would act on the wrong running app.
 */
export interface InspectorPanelContext {
  bridgeId: string;
  selection: SelectedElement | null;
  dataSnapshot: DataSnapshot | null;
  agent?: AgentRegistration;
  brokerPort?: number;
  revision?: string;
}

/** Startup hints accepted by the `mauiDevflow.openInspector` command. */
export interface InspectorOpenHints {
  test?: unknown;
  trace?: unknown;
  agentRequest?: unknown;
  agent?: unknown;
  instance?: unknown;
  element?: unknown;
  problem?: unknown;
  run?: unknown;
  view?: unknown;
}

interface StoredReference<T> {
  value: T;
  expiresAt: number;
  bytes: number;
  lastAccess: number;
}

/**
 * Bounded, TTL-scoped store for the small reference records a published diagnostic points back at.
 * The diagnostic itself carries only an opaque token, so nothing about the running app leaks into
 * the editor's diagnostic model, and a long-lived window cannot accumulate them without bound.
 */
export class BoundedReferenceStore<T> {
  private readonly values = new Map<string, StoredReference<T>>();
  private totalBytes = 0;

  constructor(
    private readonly maximumEntries = 128,
    private readonly maximumBytes = 256 * 1024,
    private readonly ttlMs = 15 * 60 * 1000,
  ) {}

  put(value: T): string {
    const serialized = JSON.stringify(value);
    const bytes = Buffer.byteLength(serialized, "utf8");
    if (bytes > this.maximumBytes) {
      throw new Error("The DevFlow context is too large to retain.");
    }
    const token = randomBytes(18).toString("base64url");
    const now = Date.now();
    this.values.set(token, {
      value: JSON.parse(serialized) as T,
      bytes,
      expiresAt: now + this.ttlMs,
      lastAccess: now,
    });
    this.totalBytes += bytes;
    this.evict(now);
    return token;
  }

  get(token: string): T | null {
    const item = this.values.get(token);
    if (!item) return null;
    const now = Date.now();
    if (item.expiresAt <= now) {
      this.delete(token);
      return null;
    }
    item.lastAccess = now;
    return item.value;
  }

  clear(): void {
    this.values.clear();
    this.totalBytes = 0;
  }

  private delete(token: string): void {
    const item = this.values.get(token);
    if (!item) return;
    this.values.delete(token);
    this.totalBytes -= item.bytes;
  }

  private evict(now: number): void {
    for (const [token, item] of this.values) {
      if (item.expiresAt <= now) this.delete(token);
    }
    while (this.values.size > this.maximumEntries || this.totalBytes > this.maximumBytes) {
      let oldestToken: string | null = null;
      let oldestAccess = Number.POSITIVE_INFINITY;
      for (const [token, item] of this.values) {
        if (item.lastAccess < oldestAccess) {
          oldestAccess = item.lastAccess;
          oldestToken = token;
        }
      }
      if (!oldestToken) break;
      this.delete(oldestToken);
    }
  }
}