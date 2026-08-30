import type { ElementInfo } from "./types.js";

export interface InspectorSnapshotTarget {
  agentId?: string | null;
  appName?: string | null;
  platform?: string | null;
}

export interface InspectorSnapshotViewport {
  width: number;
  height: number;
  rootOffsetX: number;
  rootOffsetY: number;
}

export interface InspectorSnapshot {
  ok: true;
  protocolVersion: 1;
  projection: "activeVisual";
  snapshotId: string;
  revision: string;
  capturedAt: string;
  target: InspectorSnapshotTarget;
  viewport: InspectorSnapshotViewport;
  screenshotUrl: string;
  roots: ElementInfo[];
}

export interface InspectorQueryResult {
  ok: true;
  protocolVersion: 1;
  projection: "activeVisual";
  snapshotId: string;
  revision: string;
  elements: ElementInfo[];
}

export function isInspectorSnapshot(value: unknown): value is InspectorSnapshot {
  if (!value || typeof value !== "object") return false;
  const snapshot = value as Partial<InspectorSnapshot>;
  return snapshot.ok === true &&
    snapshot.protocolVersion === 1 &&
    snapshot.projection === "activeVisual" &&
    typeof snapshot.snapshotId === "string" &&
    typeof snapshot.revision === "string" &&
    typeof snapshot.capturedAt === "string" &&
    typeof snapshot.screenshotUrl === "string" &&
    Array.isArray(snapshot.roots) &&
    !!snapshot.target &&
    (snapshot.target.agentId == null || typeof snapshot.target.agentId === "string") &&
    !!snapshot.viewport &&
    Number.isFinite(snapshot.viewport.width) &&
    Number.isFinite(snapshot.viewport.height) &&
    Number.isFinite(snapshot.viewport.rootOffsetX) &&
    Number.isFinite(snapshot.viewport.rootOffsetY);
}

export function isInspectorQueryResult(value: unknown): value is InspectorQueryResult {
  if (!value || typeof value !== "object") return false;
  const result = value as Partial<InspectorQueryResult>;
  return result.ok === true &&
    result.protocolVersion === 1 &&
    result.projection === "activeVisual" &&
    typeof result.snapshotId === "string" &&
    typeof result.revision === "string" &&
    Array.isArray(result.elements);
}
