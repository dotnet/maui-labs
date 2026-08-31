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
