import { discoverBroker, readBrokerState } from "./broker.js";
import { httpJson, httpRaw } from "./http.js";
import { isInspectorQueryResult, isInspectorSnapshot } from "./inspect-contracts.js";
import type { InspectorQueryResult, InspectorSnapshot } from "./inspect-contracts.js";
import { err, ok } from "./types.js";
import type {
  AgentRegistration,
  BootstrapPolicy,
  DevFlowError,
  DevFlowResult,
} from "./types.js";
import {
  isDiagnosticProblems,
  isEvidencePreview,
  isLayoutDiagnosticsResponse,
} from "./workspace-contracts.js";
import type {
  DiagnosticProblems,
  EvidencePreviewPlan,
  EvidencePreviewRequest,
  LayoutDiagnosticsReport,
  LayoutInspectionRequest,
} from "./workspace-contracts.js";

const MAX_REQUEST_BYTES = 1_048_576;
const MAX_RESPONSE_BYTES = 16 * 1024 * 1024;
const MAX_INSPECTOR_HTML_BYTES = 2 * 1024 * 1024;
const MAX_ROUTE_CHARS = 4096;
const MAX_QUERY_VALUE_CHARS = 1024;
const MAX_ELEMENT_ID_CHARS = 256;
const TOKEN_GATED_ENDPOINTS = new Set([
  "/api/problems",
  "/api/diagnostics/layout",
  "/api/evidence/preview",
]);

interface InspectorReadToken {
  brokerPort: number;
  agentId: string;
  agentInstanceId: string;
  token: string;
}

export interface DevFlowAgentSelector {
  agentId?: string;
  agentInstanceId?: string;
  port?: number;
}

export interface DevFlowWorkspaceSessionOptions {
  brokerPort?: number;
  bootstrapBroker?: BootstrapPolicy;
  mauiCliPath?: string;
  requestTimeoutMs?: number;
  agent?: DevFlowAgentSelector;
}

export interface WorkspaceAgentIdentity {
  brokerPort: number;
  agentId: string;
  agentInstanceId: string;
  port: number;
  appName?: string;
  platform?: string;
  project?: string;
  tfm?: string;
  connectedAt?: string;
}

export interface WorkspaceCaptureReference {
  brokerPort: number;
  agentId: string;
  agentInstanceId: string;
  snapshotRevision?: string;
  capturedAt: string;
}

export interface WorkspaceRead<T> {
  value: T;
  identity: WorkspaceAgentIdentity;
  capturedAt: string;
  snapshotRevision?: string;
}

export type WorkspaceLifecycleEventType =
  | "connected"
  | "restarted"
  | "disconnected"
  | "selected-agent-changed";

export interface WorkspaceLifecycleEvent {
  type: WorkspaceLifecycleEventType;
  at: string;
  previous: WorkspaceAgentIdentity | null;
  current: WorkspaceAgentIdentity | null;
}

export interface InspectorQuery {
  type?: string;
  automationId?: string;
  text?: string;
  selector?: string;
}

export interface ProblemsRequest {
  limit?: number;
  elementId?: string;
}

export interface WorkspaceLifecycleHandle {
  close(): void;
}

type LifecycleListener = (event: WorkspaceLifecycleEvent) => void;

export class DevFlowWorkspaceSession {
  private readonly brokerPort?: number;
  private readonly bootstrapBroker: BootstrapPolicy;
  private readonly mauiCliPath?: string;
  private readonly requestTimeoutMs: number;
  private selector?: DevFlowAgentSelector;
  private currentIdentity: WorkspaceAgentIdentity | null = null;
  private snapshotRevision: string | null = null;
  private inspectorReadToken: InspectorReadToken | null = null;
  private readonly listeners = new Set<LifecycleListener>();
  private readonly monitors = new Set<NodeJS.Timeout>();
  private disposed = false;

  constructor(options: DevFlowWorkspaceSessionOptions = {}) {
    this.brokerPort = options.brokerPort;
    this.bootstrapBroker = options.bootstrapBroker ?? "never";
    this.mauiCliPath = options.mauiCliPath;
    this.requestTimeoutMs = boundedInteger(options.requestTimeoutMs ?? 8000, 100, 120_000);
    if (options.agent) {
      const selectorError = validateSelector(options.agent);
      if (selectorError) throw new TypeError(selectorError);
      this.selector = { ...options.agent };
    }
  }

  get identity(): WorkspaceAgentIdentity | null {
    return this.currentIdentity ? { ...this.currentIdentity } : null;
  }

  get lastSnapshotRevision(): string | null {
    return this.snapshotRevision;
  }

  onLifecycle(listener: LifecycleListener): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  watchLifecycle(intervalMs = 1000): WorkspaceLifecycleHandle {
    const delay = boundedInteger(intervalMs, 250, 60_000);
    void this.refresh();
    const timer = setInterval(() => void this.refresh(), delay);
    timer.unref?.();
    this.monitors.add(timer);
    return {
      close: () => {
        clearInterval(timer);
        this.monitors.delete(timer);
      },
    };
  }

  async selectAgent(selector: DevFlowAgentSelector): Promise<DevFlowResult<WorkspaceAgentIdentity>> {
    if (this.disposed) return this.disposedError("selectAgent");
    const selectorError = validateSelector(selector);
    if (selectorError) return invalid("selectAgent", selectorError);
    const previous = this.currentIdentity;
    this.selector = { ...selector };
    this.currentIdentity = null;
    this.snapshotRevision = null;
    this.inspectorReadToken = null;
    const connected = await this.refresh(false);
    if (!connected.ok) return connected;
    this.emit("selected-agent-changed", previous, connected.value);
    return connected;
  }

  async connect(): Promise<DevFlowResult<WorkspaceAgentIdentity>> {
    return this.refresh();
  }

  async refresh(emitLifecycle = true): Promise<DevFlowResult<WorkspaceAgentIdentity>> {
    if (this.disposed) return this.disposedError("refresh");
    const previous = this.currentIdentity;
    const broker = await discoverBroker({
      bootstrap: this.bootstrapBroker,
      cliPath: this.mauiCliPath,
      brokerPort: this.brokerPort,
    });
    if (!broker) {
      this.noteDisconnected(previous, emitLifecycle);
      return err({
        kind: "broker-not-found",
        message: "DevFlow broker not found or unreachable.",
        operation: "refresh",
        retriable: true,
      });
    }

    const match = resolveExactAgent(broker.agents, this.selector, previous);
    if (!match.ok) {
      this.noteDisconnected(previous, emitLifecycle);
      return match;
    }
    if (!match.value.instanceId) {
      this.noteDisconnected(previous, emitLifecycle);
      return err({
        kind: "bad-json",
        message: "The broker registration does not contain an agent instance ID.",
        operation: "refresh",
        retriable: true,
      });
    }

    const next = identityFrom(broker.port, match.value);
    this.currentIdentity = next;
    if (!sameInspectorInstance(previous, next)) this.inspectorReadToken = null;
    if (!previous) {
      if (emitLifecycle) this.emit("connected", null, next);
    } else if (previous.agentInstanceId !== next.agentInstanceId) {
      this.snapshotRevision = null;
      if (emitLifecycle) this.emit("restarted", previous, next);
    }
    return ok(next);
  }

  buildInspectorUrl(
    path = "/",
    query: Readonly<Record<string, string | number | boolean | null | undefined>> = {},
  ): DevFlowResult<string> {
    if (this.disposed) return this.disposedError("buildInspectorUrl");
    const identity = this.currentIdentity;
    if (!identity) {
      return err({
        kind: "agent-unreachable",
        message: "Connect the workspace session before constructing an Inspector URL.",
        operation: "buildInspectorUrl",
        retriable: true,
      });
    }
    const route = buildInspectorPath(identity.agentId, path, query);
    if (!route.ok) return route;
    return ok(`http://localhost:${identity.brokerPort}${route.value}`);
  }

  captureReference(snapshotRevision = this.snapshotRevision ?? undefined): DevFlowResult<WorkspaceCaptureReference> {
    if (this.disposed) return this.disposedError("captureReference");
    if (!this.currentIdentity) {
      return err({
        kind: "agent-unreachable",
        message: "No connected workspace agent.",
        operation: "captureReference",
        retriable: true,
      });
    }
    return ok({
      brokerPort: this.currentIdentity.brokerPort,
      agentId: this.currentIdentity.agentId,
      agentInstanceId: this.currentIdentity.agentInstanceId,
      snapshotRevision,
      capturedAt: new Date().toISOString(),
    });
  }

  isStale(reference: WorkspaceCaptureReference): boolean {
    const current = this.currentIdentity;
    return !current ||
      reference.brokerPort !== current.brokerPort ||
      reference.agentId !== current.agentId ||
      reference.agentInstanceId !== current.agentInstanceId ||
      (reference.snapshotRevision != null && reference.snapshotRevision !== this.snapshotRevision);
  }

  async assertCurrent(reference: WorkspaceCaptureReference): Promise<DevFlowResult<WorkspaceAgentIdentity>> {
    const current = await this.refresh();
    if (!current.ok) return current;
    if (reference.brokerPort !== current.value.brokerPort ||
        reference.agentId !== current.value.agentId ||
        reference.agentInstanceId !== current.value.agentInstanceId) {
      return staleAgent("assertCurrent", current.value);
    }
    if (reference.snapshotRevision != null && reference.snapshotRevision !== this.snapshotRevision) {
      return err({
        kind: "stale-snapshot",
        message: "The Inspector snapshot revision is stale.",
        operation: "assertCurrent",
        retriable: true,
      });
    }
    return current;
  }

  async getSnapshot(
    reference?: WorkspaceCaptureReference,
  ): Promise<DevFlowResult<WorkspaceRead<InspectorSnapshot>>> {
    return this.readInspector(
      "getSnapshot",
      "GET",
      "/api/inspect/snapshot",
      undefined,
      isInspectorSnapshot,
      reference,
      (value) => value.revision,
      (value, identity) => value.target.agentId == null || value.target.agentId === identity.agentId,
    );
  }

  async query(
    query: InspectorQuery,
    reference?: WorkspaceCaptureReference,
  ): Promise<DevFlowResult<WorkspaceRead<InspectorQueryResult>>> {
    const body = boundedInspectorQuery(query);
    if (!body.ok) return body;
    return this.readInspector(
      "query",
      "POST",
      "/api/inspect/query",
      body.value,
      isInspectorQueryResult,
      reference,
      (value) => value.revision,
    );
  }

  async getProblems(
    request: ProblemsRequest = {},
    reference?: WorkspaceCaptureReference,
  ): Promise<DevFlowResult<WorkspaceRead<DiagnosticProblems>>> {
    const limit = request.limit ?? 100;
    if (!Number.isInteger(limit) || limit < 1 || limit > 500) {
      return invalid("getProblems", "limit must be an integer from 1 through 500.");
    }
    if (!boundedOptionalString(request.elementId, MAX_ELEMENT_ID_CHARS)) {
      return invalid("getProblems", `elementId cannot exceed ${MAX_ELEMENT_ID_CHARS} characters.`);
    }
    return this.readInspector(
      "getProblems",
      "POST",
      "/api/problems",
      { limit, ...(request.elementId ? { elementId: request.elementId } : {}) },
      isDiagnosticProblems,
      reference,
    );
  }

  async analyzeLayout(
    request: LayoutInspectionRequest = {},
    reference?: WorkspaceCaptureReference,
  ): Promise<DevFlowResult<WorkspaceRead<LayoutDiagnosticsReport>>> {
    const requestError = validateLayoutRequest(request);
    if (requestError) return invalid("analyzeLayout", requestError);
    const response = await this.readInspector(
      "analyzeLayout",
      "POST",
      "/api/diagnostics/layout",
      request,
      isLayoutDiagnosticsResponse,
      reference,
    );
    if (!response.ok) return response;
    if (!response.value.value.ok) {
      return err({
        kind: "capability-missing",
        message: response.value.value.error,
        operation: "analyzeLayout",
        status: 200,
        retriable: response.value.value.retryable ?? false,
      });
    }
    return ok({
      ...response.value,
      value: response.value.value.report,
    });
  }

  async previewEvidence(
    request: EvidencePreviewRequest = {},
    reference?: WorkspaceCaptureReference,
  ): Promise<DevFlowResult<WorkspaceRead<EvidencePreviewPlan>>> {
    const requestError = validateEvidenceRequest(request);
    if (requestError) return invalid("previewEvidence", requestError);
    const response = await this.readInspector(
      "previewEvidence",
      "POST",
      "/api/evidence/preview",
      request,
      isEvidencePreview,
      reference,
    );
    if (!response.ok) return response;
    return ok({
      ...response.value,
      value: response.value.value.plan,
    });
  }

  dispose(): void {
    if (this.disposed) return;
    this.disposed = true;
    for (const timer of this.monitors) clearInterval(timer);
    this.monitors.clear();
    this.listeners.clear();
    this.currentIdentity = null;
    this.snapshotRevision = null;
    this.inspectorReadToken = null;
  }

  private async readInspector<T>(
    operation: string,
    method: "GET" | "POST",
    endpoint: string,
    body: unknown,
    guard: (value: unknown) => value is T,
    reference?: WorkspaceCaptureReference,
    revision?: (value: T) => string,
    identityGuard?: (value: T, identity: WorkspaceAgentIdentity) => boolean,
  ): Promise<DevFlowResult<WorkspaceRead<T>>> {
    if (this.disposed) return this.disposedError(operation);
    if (reference) {
      const current = await this.assertCurrent(reference);
      if (!current.ok) return current;
    }
    const connected = await this.refresh();
    if (!connected.ok) return connected;
    const identity = connected.value;
    const route = buildInspectorPath(identity.agentId, endpoint);
    if (!route.ok) return route;
    const requestBody = serializeBounded(body);
    if (!requestBody.ok) return invalid(operation, requestBody.error);

    const tokenGated = TOKEN_GATED_ENDPOINTS.has(endpoint);
    let token: string | undefined;
    if (tokenGated) {
      const acquired = await this.getInspectorReadToken(identity);
      if (!acquired.ok) return acquired;
      token = acquired.value;
    }

    let response = await this.requestInspectorJson(identity, method, route.value, body, token);
    if (tokenGated && response.status === 403) {
      this.inspectorReadToken = null;
      const refreshed = await this.getInspectorReadToken(identity);
      if (!refreshed.ok) return refreshed;
      response = await this.requestInspectorJson(identity, method, route.value, body, refreshed.value);
    }
    if (!response.ok) return responseError(operation, identity, response.status, response.error, response.data);
    if (!guard(response.data)) {
      return err({
        kind: "bad-json",
        message: `The broker returned an invalid ${operation} response.`,
        operation,
        status: response.status,
        retriable: false,
      });
    }
    if (identityGuard && !identityGuard(response.data, identity)) return staleAgent(operation, identity);

    const after = await this.refresh();
    if (!after.ok) return after;
    if (after.value.agentInstanceId !== identity.agentInstanceId) return staleAgent(operation, after.value);

    const nextRevision = revision?.(response.data);
    if (nextRevision != null) this.snapshotRevision = nextRevision;
    return ok({
      value: response.data,
      identity,
      capturedAt: new Date().toISOString(),
      ...(nextRevision ? { snapshotRevision: nextRevision } : {}),
    });
  }

  private noteDisconnected(previous: WorkspaceAgentIdentity | null, emit: boolean): void {
    this.currentIdentity = null;
    this.snapshotRevision = null;
    this.inspectorReadToken = null;
    if (previous && emit) this.emit("disconnected", previous, null);
  }

  private emit(
    type: WorkspaceLifecycleEventType,
    previous: WorkspaceAgentIdentity | null,
    current: WorkspaceAgentIdentity | null,
  ): void {
    const event = { type, at: new Date().toISOString(), previous, current };
    for (const listener of this.listeners) {
      try {
        listener(event);
      } catch {
        // A host listener cannot break session lifecycle tracking.
      }
    }
  }

  private disposedError<T>(operation: string): DevFlowResult<T> {
    return err({ kind: "disposed", message: "Workspace session is disposed.", operation, retriable: false });
  }

  private async requestInspectorJson(
    identity: WorkspaceAgentIdentity,
    method: "GET" | "POST",
    route: string,
    body: unknown,
    readToken?: string,
  ) {
    const headers = readToken ? { "X-DevFlow-Inspector-Token": readToken } : undefined;
    let response = await httpJson(identity.brokerPort, method, route, {
      json: body,
      timeoutMs: this.requestTimeoutMs,
      maxResponseBytes: MAX_RESPONSE_BYTES,
      headers,
    });
    if (!response.ok && (response.status === 0 || response.status === 404)) {
      response = await httpJson(identity.brokerPort, method, route, {
        json: body,
        timeoutMs: this.requestTimeoutMs,
        host: "localhost",
        hostHeader: `localhost:${identity.brokerPort}`,
        maxResponseBytes: MAX_RESPONSE_BYTES,
        headers,
      });
    }
    return response;
  }

  private async getInspectorReadToken(
    identity: WorkspaceAgentIdentity,
  ): Promise<DevFlowResult<string>> {
    const cached = this.inspectorReadToken;
    if (cached &&
        cached.brokerPort === identity.brokerPort &&
        cached.agentId === identity.agentId &&
        cached.agentInstanceId === identity.agentInstanceId) {
      return ok(cached.token);
    }

    const state = readBrokerState();
    if (!state || state.port !== identity.brokerPort ||
        typeof state.embedToken !== "string" ||
        state.embedToken.length < 16 ||
        state.embedToken.length > 512 ||
        /[\u0000-\u001f\u007f]/.test(state.embedToken)) {
      return err({
        kind: "permission-denied",
        message: "The local broker state does not authorize Inspector read-token acquisition.",
        operation: "acquireInspectorReadToken",
        retriable: true,
      });
    }

    const route = buildInspectorPath(identity.agentId, "/", { embed: state.embedToken });
    if (!route.ok) return route;
    let response = await httpRaw(identity.brokerPort, "GET", route.value, {
      timeoutMs: this.requestTimeoutMs,
      maxResponseBytes: MAX_INSPECTOR_HTML_BYTES,
      headers: { Accept: "text/html" },
    });
    if (!response.ok && (response.status === 0 || response.status === 404)) {
      response = await httpRaw(identity.brokerPort, "GET", route.value, {
        timeoutMs: this.requestTimeoutMs,
        host: "localhost",
        hostHeader: `localhost:${identity.brokerPort}`,
        maxResponseBytes: MAX_INSPECTOR_HTML_BYTES,
        headers: { Accept: "text/html" },
      });
    }
    if (!response.ok || !response.buffer) {
      return responseError(
        "acquireInspectorReadToken",
        identity,
        response.status,
        response.error,
        null,
      );
    }

    const metadata = extractInspectorMetadata(response.buffer.toString("utf8"));
    if (!metadata ||
        metadata.agentId !== identity.agentId ||
        metadata.agentInstanceId !== identity.agentInstanceId) {
      return err({
        kind: "bad-json",
        message: "The broker returned malformed or stale Inspector metadata.",
        operation: "acquireInspectorReadToken",
        status: response.status,
        retriable: true,
      });
    }
    this.inspectorReadToken = {
      brokerPort: identity.brokerPort,
      agentId: identity.agentId,
      agentInstanceId: identity.agentInstanceId,
      token: metadata.token,
    };
    return ok(metadata.token);
  }
}

function resolveExactAgent(
  agents: AgentRegistration[],
  selector: DevFlowAgentSelector | undefined,
  current: WorkspaceAgentIdentity | null,
): DevFlowResult<AgentRegistration> {
  let matches: AgentRegistration[];
  if (selector) {
    matches = agents.filter((agent) =>
      (selector.agentId == null || agent.id === selector.agentId) &&
      (selector.agentInstanceId == null || agent.instanceId === selector.agentInstanceId) &&
      (selector.port == null || agent.port === selector.port));
  } else if (current) {
    matches = agents.filter((agent) => agent.id === current.agentId);
  } else {
    matches = agents;
  }
  if (matches.length === 1) return ok(matches[0]!);
  if (matches.length === 0) {
    return err({
      kind: agents.length ? "not-found" : "no-agents",
      message: agents.length ? "The selected DevFlow agent is not connected." : "No DevFlow agents are connected.",
      operation: "refresh",
      retriable: true,
    });
  }
  return err({
    kind: "agent-ambiguous",
    message: "The agent selector matched more than one connected agent.",
    operation: "refresh",
    candidates: matches,
    retriable: false,
  });
}

function identityFrom(brokerPort: number, agent: AgentRegistration): WorkspaceAgentIdentity {
  return {
    brokerPort,
    agentId: agent.id,
    agentInstanceId: agent.instanceId!,
    port: agent.port,
    appName: agent.appName,
    platform: agent.platform,
    project: agent.project,
    tfm: agent.tfm,
    connectedAt: agent.connectedAt,
  };
}

function sameInspectorInstance(
  left: WorkspaceAgentIdentity | null,
  right: WorkspaceAgentIdentity | null,
): boolean {
  return !!left && !!right &&
    left.brokerPort === right.brokerPort &&
    left.agentId === right.agentId &&
    left.agentInstanceId === right.agentInstanceId;
}

interface InspectorMetadata {
  token: string;
  agentId: string;
  agentInstanceId: string;
}

function extractInspectorMetadata(html: string): InspectorMetadata | null {
  if (Buffer.byteLength(html, "utf8") > MAX_INSPECTOR_HTML_BYTES) return null;
  const values = new Map<string, string>();
  const metaPattern = /<meta\b[^>]{0,2048}>/gi;
  let count = 0;
  for (const match of html.matchAll(metaPattern)) {
    if (++count > 256) return null;
    const attributes = parseHtmlAttributes(match[0]);
    const name = attributes.get("name");
    const content = attributes.get("content");
    if (!name || content == null ||
        !["devflow-inspector-token", "devflow-agent-id", "devflow-agent-instance-id"].includes(name)) {
      continue;
    }
    if (values.has(name)) return null;
    values.set(name, content);
  }
  const token = values.get("devflow-inspector-token");
  const agentId = values.get("devflow-agent-id");
  const agentInstanceId = values.get("devflow-agent-instance-id");
  if (!token || !/^[a-f0-9]{32}$/i.test(token) ||
      !agentId || agentId.length > 256 ||
      !agentInstanceId || agentInstanceId.length > 256) {
    return null;
  }
  return { token, agentId, agentInstanceId };
}

function parseHtmlAttributes(tag: string): Map<string, string> {
  const attributes = new Map<string, string>();
  const pattern = /([A-Za-z_:][A-Za-z0-9_.:-]*)\s*=\s*(?:"([^"]*)"|'([^']*)')/g;
  for (const match of tag.matchAll(pattern)) {
    const name = match[1]!.toLowerCase();
    const value = decodeHtmlAttribute(match[2] ?? match[3] ?? "");
    if (value == null || attributes.has(name)) return new Map();
    attributes.set(name, value);
  }
  return attributes;
}

function decodeHtmlAttribute(value: string): string | null {
  if (value.length > 512 || /[\u0000-\u001f\u007f]/.test(value)) return null;
  return value.replace(
    /&(?:amp|quot|apos|lt|gt|#39|#x27);/gi,
    (entity) => ({
      "&amp;": "&",
      "&quot;": "\"",
      "&apos;": "'",
      "&lt;": "<",
      "&gt;": ">",
      "&#39;": "'",
      "&#x27;": "'",
    })[entity.toLowerCase()] ?? entity,
  );
}

function validateSelector(selector: DevFlowAgentSelector): string | null {
  if (selector.agentId == null && selector.agentInstanceId == null && selector.port == null) {
    return "An agent selector requires agentId, agentInstanceId, or port.";
  }
  if (!boundedOptionalString(selector.agentId, 256) ||
      !boundedOptionalString(selector.agentInstanceId, 256)) {
    return "Agent IDs cannot exceed 256 characters.";
  }
  if (selector.port != null &&
      (!Number.isInteger(selector.port) || selector.port < 1 || selector.port > 65535)) {
    return "Agent port must be an integer from 1 through 65535.";
  }
  return null;
}

function boundedInspectorQuery(query: InspectorQuery): DevFlowResult<Record<string, string>> {
  const entries = Object.entries(query).filter((entry): entry is [string, string] =>
    typeof entry[1] === "string" && entry[1].trim().length > 0);
  if (entries.length === 0) return invalid("query", "Provide type, automationId, text, or selector.");
  if (entries.some(([, value]) => value.length > MAX_QUERY_VALUE_CHARS)) {
    return invalid("query", `Query values cannot exceed ${MAX_QUERY_VALUE_CHARS} characters.`);
  }
  if (query.selector && entries.some(([key]) => key !== "selector")) {
    return invalid("query", "selector cannot be combined with type, automationId, or text.");
  }
  return ok(Object.fromEntries(entries));
}

function validateLayoutRequest(request: LayoutInspectionRequest): string | null {
  if (request.schemaVersion != null && request.schemaVersion !== "2.0") return "schemaVersion must be 2.0.";
  if (!boundedOptionalString(request.elementId, MAX_ELEMENT_ID_CHARS) ||
      !boundedOptionalString(request.scope?.rootElementId, MAX_ELEMENT_ID_CHARS)) {
    return `Layout element IDs cannot exceed ${MAX_ELEMENT_ID_CHARS} characters.`;
  }
  if (request.maxElements != null &&
      (!Number.isInteger(request.maxElements) || request.maxElements < 1 || request.maxElements > 5_000)) {
    return "maxElements must be an integer from 1 through 5000.";
  }
  if (request.rules && (request.rules.length > 100 ||
      request.rules.some((rule) => typeof rule !== "string" || rule.length > 256))) {
    return "rules can contain at most 100 values of at most 256 characters.";
  }
  const serialized = serializeBounded(request);
  return serialized.ok ? null : serialized.error;
}

function validateEvidenceRequest(request: EvidencePreviewRequest): string | null {
  if (!boundedOptionalString(request.elementId, MAX_ELEMENT_ID_CHARS)) {
    return `elementId cannot exceed ${MAX_ELEMENT_ID_CHARS} characters.`;
  }
  for (const [name, value] of [["logLimit", request.logLimit], ["networkLimit", request.networkLimit]] as const) {
    if (value != null && (!Number.isInteger(value) || value < 1 || value > 500)) {
      return `${name} must be an integer from 1 through 500.`;
    }
  }
  if (request.workflow != null && Buffer.byteLength(request.workflow, "utf8") > MAX_REQUEST_BYTES) {
    return "workflow cannot exceed 1 MB of UTF-8 data.";
  }
  const serialized = serializeBounded(request);
  return serialized.ok ? null : serialized.error;
}

function buildInspectorPath(
  agentId: string,
  path: string,
  query: Readonly<Record<string, string | number | boolean | null | undefined>> = {},
): DevFlowResult<string> {
  if (!path.startsWith("/") || path.length > MAX_ROUTE_CHARS ||
      path.includes("\\") || path.includes("#") || path.includes("?") ||
      path.split("/").includes("..") || /[\u0000-\u001f\u007f]/.test(path)) {
    return invalid("buildInspectorUrl", "Inspector paths must be bounded absolute paths without traversal, query, or fragment text.");
  }
  const params = new URLSearchParams();
  for (const [key, raw] of Object.entries(query)) {
    if (!/^[A-Za-z0-9_.~-]{1,64}$/.test(key)) {
      return invalid("buildInspectorUrl", "Inspector query keys must be 1-64 URL-safe characters.");
    }
    if (raw == null) continue;
    const value = String(raw);
    if (value.length > MAX_QUERY_VALUE_CHARS) {
      return invalid("buildInspectorUrl", `Inspector query values cannot exceed ${MAX_QUERY_VALUE_CHARS} characters.`);
    }
    params.append(key, value);
  }
  const route = `/inspector/${encodeURIComponent(agentId)}${path}`;
  const result = params.size ? `${route}?${params.toString()}` : route;
  return result.length <= MAX_ROUTE_CHARS
    ? ok(result)
    : invalid("buildInspectorUrl", `Inspector route cannot exceed ${MAX_ROUTE_CHARS} characters.`);
}

function serializeBounded(value: unknown): { ok: true } | { ok: false; error: string } {
  if (value == null) return { ok: true };
  try {
    const json = JSON.stringify(value);
    return Buffer.byteLength(json, "utf8") <= MAX_REQUEST_BYTES
      ? { ok: true }
      : { ok: false, error: "Inspector request body cannot exceed 1 MB." };
  } catch {
    return { ok: false, error: "Inspector request body must be JSON serializable." };
  }
}

function boundedOptionalString(value: string | undefined, max: number): boolean {
  return value == null || (typeof value === "string" && value.length <= max);
}

function boundedInteger(value: number, min: number, max: number): number {
  if (!Number.isFinite(value)) return min;
  return Math.max(min, Math.min(max, Math.trunc(value)));
}

function invalid<T>(operation: string, message: string): DevFlowResult<T> {
  return err({ kind: "invalid-argument", message, operation, retriable: false });
}

function staleAgent<T>(operation: string, identity: WorkspaceAgentIdentity): DevFlowResult<T> {
  return err({
    kind: "stale-agent",
    message: "The selected DevFlow agent instance changed while the request was in flight.",
    operation,
    retriable: true,
    cause: `${identity.agentId}:${identity.agentInstanceId}`,
  });
}

function responseError<T>(
  operation: string,
  identity: WorkspaceAgentIdentity,
  status: number,
  transportError: string | undefined,
  data: unknown,
): DevFlowResult<T> {
  const message = responseMessage(data) ?? transportError ?? `Inspector request failed with HTTP ${status}.`;
  const kind: DevFlowError["kind"] =
    transportError === "timeout" ? "timeout" :
    status === 404 ? "not-found" :
    status === 400 ? "invalid-argument" :
    status === 0 ? "broker-unreachable" : "http";
  return err({
    kind,
    message,
    operation,
    status,
    target: { port: identity.port, appName: identity.appName, platform: identity.platform },
    retriable: status === 0 || status >= 500,
  });
}

function responseMessage(data: unknown): string | null {
  if (!data || typeof data !== "object") return null;
  const value = data as { error?: unknown };
  if (typeof value.error === "string") return value.error;
  if (value.error && typeof value.error === "object") {
    const message = (value.error as { message?: unknown }).message;
    if (typeof message === "string") return message;
  }
  return null;
}
