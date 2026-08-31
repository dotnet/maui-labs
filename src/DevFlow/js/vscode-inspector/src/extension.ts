import * as vscode from "vscode";
import * as path from "path";
import { createHash, randomBytes } from "crypto";
import { createInspectorHostManifest } from "@maui-devflow/client";
import type { AgentRegistration } from "@maui-devflow/client";
import {
  agentRuntimeIdentity,
  selectRefreshedAgent,
} from "./agent-identity";
import {
  isBridgeResultMessage,
  requiresBridgeRequestId,
} from "./bridge-contract";
import type {
  DataSnapshot,
  InspectorOpenHints,
  InspectorPanelContext,
  SelectedElement,
} from "./context-store";
import {
  isNativeApprovalRequest,
  performNativeApproval,
} from "./native-approval";
import {
  inspectorTitle,
  reconnectDiscoveryAction,
  renderReconnectHost,
  type ReconnectState,
} from "./host-shells";
import { DevFlowDiagnosticsController } from "./diagnostics";
import type {
  DevFlowActiveApp,
  DevFlowEvidenceContext,
  DevFlowHostServices,
  DevFlowLayoutReport,
  DevFlowProblemBatch,
} from "./host-services";

/**
 * MAUI DevFlow Inspector — VS Code host shell.
 *
 * A thin shell over the SHARED DevFlow inspector: it discovers the broker via the shared
 * `@maui-devflow/client`, then opens a webview that embeds the broker-hosted inspector for a
 * running app (`http://localhost:{brokerPort}/inspector/{agentId}/`) — the same inspector the
 * browser and Copilot Canvas use, including the rich property grid, visual tree, and record/replay.
 * No UI is re-implemented here; the host contributes the authenticated bridge for relaying
 * Send-to-Copilot into Chat, opening a XAML source file, saving a recorded test, running the
 * trusted native approval ceremony, and — behind an off-by-default setting — publishing explicit
 * layout findings and runtime Problems into VS Code Diagnostics.
 *
 * The manifest advertises exactly what is registered here. A chat participant, language-model
 * tools, and an MCP definition provider are deliberately absent: announcing them without
 * registering them would offer the user commands that silently do nothing.
 */
export function activate(context: vscode.ExtensionContext): void {
  const services = createHostServices();
  context.subscriptions.push(
    vscode.commands.registerCommand(
      "mauiDevflow.openInspector",
      (startupHints?: InspectorStartupHints) => openInspector(startupHints),
    ),
    services,
  );
  services.setDiagnosticsController(new DevFlowDiagnosticsController(context, services));
}

type HostServicesWithDiagnostics = DevFlowHostServices & vscode.Disposable & {
  setDiagnosticsController(controller: DevFlowDiagnosticsController): void;
};

function createHostServices(): HostServicesWithDiagnostics {
  let session: import("@maui-devflow/client").DevFlowWorkspaceSession | null = null;
  let sessionKey = "";
  let diagnostics: DevFlowDiagnosticsController | null = null;
  let disposed = false;
  let refreshBusy = false;
  // One layout scan per minute against the running app, versus a Problems read every 5 seconds.
  const LAYOUT_REFRESH_TICKS = 12;
  let ticksSinceLayout = LAYOUT_REFRESH_TICKS;

  const ensureSession = async () => {
    const state = activePanelState;
    const config = vscode.workspace.getConfiguration("mauiDevflow");
    const configured = config.get<number>("brokerPort");
    const brokerPort = state?.brokerPort ??
      (typeof configured === "number" && configured > 0 ? configured : undefined);
    const agent = state?.agent;
    const key = [
      brokerPort ?? "",
      agent?.id ?? "",
      agent?.instanceId ?? agent?.sessionId ?? "",
      agent?.port ?? "",
    ].join("|");
    if (session && key === sessionKey) return session;

    session?.dispose();
    const client = await import("@maui-devflow/client");
    session = new client.DevFlowWorkspaceSession({
      brokerPort,
      bootstrapBroker: "never",
      agent: agent
        ? {
            agentId: agent.id,
            agentInstanceId: agent.instanceId ?? undefined,
            port: agent.port,
          }
        : undefined,
    });
    sessionKey = key;
    return session;
  };

  const resolveActiveApp = async (): Promise<DevFlowActiveApp | null> => {
    const state = activePanelState;
    if (state?.agent && state.brokerPort) {
      return { agent: state.agent, brokerPort: state.brokerPort };
    }
    const workspaceSession = await ensureSession();
    const connected = await workspaceSession.connect();
    if (!connected.ok) return null;
    const identity = connected.value;
    return {
      brokerPort: identity.brokerPort,
      agent: {
        id: identity.agentId,
        instanceId: identity.agentInstanceId,
        sessionId: identity.agentInstanceId,
        project: identity.project ?? "",
        tfm: identity.tfm ?? "",
        platform: identity.platform ?? "unknown",
        appName: identity.appName ?? identity.agentId,
        port: identity.port,
        connectedAt: identity.connectedAt,
      } as AgentRegistration,
    };
  };

  const services: HostServicesWithDiagnostics = {
    getPanelContext: () => activePanelState,
    getDataSnapshot: () => activePanelState?.dataSnapshot ?? null,
    getSelectedElement: () => activePanelState?.selection ?? null,
    getCurrentEvidence: async (): Promise<DevFlowEvidenceContext | null> => {
      const snapshot = activePanelState?.dataSnapshot;
      if (snapshot) return { kind: "dataSnapshot", value: snapshot };
      const workspaceSession = await ensureSession();
      const preview = await workspaceSession.previewEvidence({
        includeScreenshot: false,
        includeWorkflow: false,
      });
      return preview.ok
        ? { kind: "evidencePreview", value: preview.value.value }
        : null;
    },
    openInspector,
    resolveActiveApp,
    getProblems: async (elementId?: string): Promise<DevFlowProblemBatch | null> => {
      const workspaceSession = await ensureSession();
      const result = await workspaceSession.getProblems({ limit: 100, elementId });
      return result.ok ? (result.value.value as DevFlowProblemBatch) : null;
    },
    getLayoutDiagnostics: async (): Promise<DevFlowLayoutReport | null> => {
      const workspaceSession = await ensureSession();
      // 2.0 is what every shipped agent accepts, and privacy.text is pinned here rather than left
      // to a default so this host can never ask for text length or content it must not surface.
      const result = await workspaceSession.analyzeLayout({
        schemaVersion: "2.0",
        privacy: { text: "none" },
        maxElements: 2000,
      });
      if (!result.ok) return null;
      const report = result.value.value as DevFlowLayoutReport;
      await diagnostics?.publishLayout(report);
      return report;
    },
    setDiagnosticsController: (controller) => {
      diagnostics = controller;
    },
    dispose: () => {
      disposed = true;
      session?.dispose();
      session = null;
      clearInterval(refreshTimer);
    },
  };

  const refreshTimer = setInterval(async () => {
    if (disposed || refreshBusy || !activePanelState ||
        !vscode.workspace.getConfiguration("mauiDevflow").get<boolean>("publishDiagnostics", false)) {
      return;
    }
    refreshBusy = true;
    try {
      await diagnostics?.refreshProblems();
      // Layout findings are published too, or the setting would advertise them and only ever
      // deliver runtime Problems. A layout scan walks the live tree and resolves a workspace file
      // per finding, so it runs on a slower multiple of this tick than the bounded Problems read.
      if (++ticksSinceLayout >= LAYOUT_REFRESH_TICKS) {
        ticksSinceLayout = 0;
        await services.getLayoutDiagnostics();
      }
    } finally {
      refreshBusy = false;
    }
  }, 5000);
  refreshTimer.unref?.();

  return services;
}

interface BridgeResult {
  ok: boolean;
  message?: string;
  error?: string;
  confirmationCapability?: string;
  name?: string;
  markdown?: string;
  planJson?: string;
  steps?: number;
  value?: {
    name?: string;
    kind?: "flow-run" | "mauitrace";
    bytesBase64?: string;
    state?: string;
    reason?: string;
    applied?: boolean;
    reverted?: boolean;
    preContentDigest?: string;
    appliedContentDigest?: string;
    contentDigest?: string;
    patchDigest?: string;
    applyRunId?: string;
    errorCode?: string;
    error?: string;
    sourceFile?: string;
    sourceLine?: number;
    sourceColumn?: number;
    sourceHash?: string;
    sourceConfidence?: string;
    delivery?: "agent" | "clipboard";
  };
}
interface TestBundle {
  name?: unknown;
  markdown?: unknown;
  planJson?: unknown;
  flowDigest?: unknown;
  planDigest?: unknown;
}
type PanelBridgeState = InspectorPanelContext;
const panelStates = new WeakMap<vscode.WebviewPanel, PanelBridgeState>();
let activePanelState: PanelBridgeState | null = null;
// Source *apply* is deferred to a later layer: this host advertises `openSourceDiff` (read-only
// review of a reviewed patch) but no capability that writes C#/XAML source or hands the page an
// editor selection to build a source-writing authority from.
const VSCODE_HOST_CAPABILITIES = [
  "copilot", "copilotContext", "workflowFilePicker", "attachData", "openSource",
  "saveRecording", "selection", "saveTestBundle", "loadTestBundle", "pickTrace",
  "requestTestProposal", "openSourceDiff", "nativeApproval", "layoutPolicyMutation",
] as const;

type InspectorStartupHints = InspectorOpenHints;

async function openInspector(startupHints?: InspectorStartupHints): Promise<void> {
  // The client is ESM; this extension is CommonJS — load it via a dynamic import.
  const { discoverBroker, readBrokerState } = await import("@maui-devflow/client");

  const config = vscode.workspace.getConfiguration("mauiDevflow");
  const configured = config.get<number>("brokerPort");
  const brokerPort = typeof configured === "number" && configured > 0 ? configured : undefined;

  const requestedAgentId = typeof startupHints?.agent === "string"
    ? startupHints.agent.trim()
    : "";
  let discovery = await discoverBroker({ bootstrap: "never", brokerPort });
  const selectedAgent = requestedAgentId
    ? discovery?.agents.find((candidate) => candidate.id === requestedAgentId)
    : discovery?.agents.length
      ? await pickAgent(discovery.agents)
      : undefined;
  if (requestedAgentId && !selectedAgent) {
    vscode.window.showWarningMessage(
      `MAUI DevFlow: agent '${requestedAgentId}' is not connected.`,
    );
  }
  if (discovery?.agents.length && !selectedAgent) return;
  let agent: AgentRegistration | undefined = selectedAgent;
  let activeBrokerPort = discovery?.port;

  const panel = vscode.window.createWebviewPanel(
    "mauiDevflowInspector",
    inspectorTitle(agent?.appName ?? agent?.id),
    resolveViewColumn(config.get<string>("openLocation")),
    {
      enableScripts: true,
      retainContextWhenHidden: true,
      portMapping: activeBrokerPort
        ? [{ webviewPort: activeBrokerPort, extensionHostPort: activeBrokerPort }]
        : [],
    }
  );

  // Per-embed secrets: `nonce` gates the one inline relay script (strict CSP, no unsafe-inline);
  // `bridgeId` authenticates every postMessage on the host bridge. The bridgeId travels in the URL
  // *fragment*, so it never reaches the broker over HTTP — only the iframe's own script reads it.
  let nonce = randomToken();
  let bridgeId = randomToken();
  const panelState: PanelBridgeState = {
    bridgeId,
    selection: null,
    dataSnapshot: null,
    agent,
    brokerPort: activeBrokerPort,
  };
  panelStates.set(panel, panelState);
  activePanelState = panelState;

  // Register the message handler BEFORE the webview HTML loads so no early bridge message is lost.
  let disposed = false;
  let refreshing = false;
  let connected = false;
  let reconnectState: ReconnectState | undefined;
  let runtimeIdentity = agent ? agentRuntimeIdentity(agent) : "";
  let connectionSignature = "";

  const showReconnect = (state: ReconnectState): void => {
    if (!connected && reconnectState === state) return;
    connected = false;
    reconnectState = state;
    panel.title = inspectorTitle();
    nonce = randomToken();
    panel.webview.html = renderReconnectHost(state, nonce);
  };

  const connectPanel = (
    nextDiscovery: NonNullable<typeof discovery>,
    nextAgent: AgentRegistration,
  ): void => {
    const state = readBrokerState();
    const embedToken = state && state.port === nextDiscovery.port
      ? state.embedToken ?? undefined
      : undefined;
    const nextBase = `http://localhost:${nextDiscovery.port}/inspector/${encodeURIComponent(nextAgent.id)}/`;
    const inspectorUrl = withInspectorStartupHints(
      embedToken ? `${nextBase}?embed=${encodeURIComponent(embedToken)}` : nextBase,
      startupHints,
    );
    const nextRuntimeIdentity = agentRuntimeIdentity(nextAgent);
    if (runtimeIdentity && nextRuntimeIdentity !== runtimeIdentity) {
      panelState.selection = null;
      panelState.dataSnapshot = null;
    }
    agent = nextAgent;
    activeBrokerPort = nextDiscovery.port;
    panelState.agent = nextAgent;
    panelState.brokerPort = nextDiscovery.port;
    runtimeIdentity = nextRuntimeIdentity;
    connectionSignature = inspectorConnectionSignature(
      nextDiscovery.port,
      embedToken,
      nextAgent,
    );
    nonce = randomToken();
    bridgeId = randomToken();
    panelState.bridgeId = bridgeId;
    panel.title = inspectorTitle(nextAgent.appName ?? nextAgent.id);
    panel.webview.options = {
      enableScripts: true,
      retainContextWhenHidden: true,
      // Required for local desktop and Remote/WSL webview proxying.
      portMapping: [{ webviewPort: nextDiscovery.port, extensionHostPort: nextDiscovery.port }],
    } as any;
    panel.webview.html = renderHost(inspectorUrl, nextAgent.appName ?? nextAgent.id, nonce, bridgeId);
    connected = true;
    reconnectState = undefined;
  };

  const pollDiscovery = async (choose: boolean = false): Promise<void> => {
    if (disposed || refreshing) return;
    refreshing = true;
    try {
      const refreshed = await discoverBroker({ bootstrap: "never", brokerPort });
      discovery = refreshed;
      if (!refreshed) {
        if (!connected) showReconnect("broker");
        return;
      }
      if (!connected) {
        const action = reconnectDiscoveryAction(refreshed.agents.length, choose);
        if (action === "wait" && refreshed.agents.length === 0) {
          showReconnect("app");
          return;
        }
        if (action === "wait") {
          showReconnect("multiple");
          return;
        }
        if (action === "choose") {
          const picked = await pickAgent(refreshed.agents);
          if (!picked) return;
          connectPanel(refreshed, picked);
          return;
        }
        connectPanel(refreshed, refreshed.agents[0]);
        return;
      }

      if (!agent || refreshed.agents.length === 0) return;
      const nextAgent = selectRefreshedAgent(refreshed.agents, agent);
      if (!nextAgent) return;
      const refreshedState = readBrokerState();
      const refreshedEmbedToken = refreshedState && refreshedState.port === refreshed.port
        ? refreshedState.embedToken ?? undefined
        : undefined;
      const nextSignature = inspectorConnectionSignature(
        refreshed.port,
        refreshedEmbedToken,
        nextAgent,
      );
      if (nextSignature !== connectionSignature) connectPanel(refreshed, nextAgent);
    } catch {
      if (!connected) showReconnect("broker");
    } finally {
      refreshing = false;
    }
  };

  panel.webview.onDidReceiveMessage(async (msg: BridgeMessage | ReconnectMessage | undefined) => {
    if (msg?.type === "devflow:reconnectPoll") {
      await pollDiscovery(false);
      return;
    }
    if (msg?.type === "devflow:chooseApp") {
      await pollDiscovery(true);
      return;
    }
    if (!msg || !("bridgeId" in msg) ||
        typeof msg.bridgeId !== "string" || msg.bridgeId !== panelState.bridgeId) return;
    if (requiresBridgeRequestId(msg.type) && typeof msg.requestId !== "string") return;
    activePanelState = panelState;

    const requestBridgeId = bridgeId;
    let result: BridgeResult;
    try {
      result = await handleBridgeMessage(msg, panelState);
    } catch (error) {
      result = { ok: false, error: `The VS Code host could not handle the request: ${String(error)}` };
    }
    if (typeof msg.requestId === "string" && isBridgeResultMessage(msg.type)) {
      if (requestBridgeId !== bridgeId) return;
      await panel.webview.postMessage({
        type: "devflow:hostResult",
        v: 1,
        bridgeId: requestBridgeId,
        requestId: msg.requestId,
        ...result,
      });
    }
  });

  if (discovery && agent) connectPanel(discovery, agent);
  else showReconnect(discovery ? "app" : "broker");

  const restartWatcher = setInterval(() => void pollDiscovery(false), 2500);
  panel.onDidDispose(() => {
    disposed = true;
    clearInterval(restartWatcher);
    panelStates.delete(panel);
    if (activePanelState === panelState) activePanelState = null;
  });
}

interface ReconnectMessage {
  type: "devflow:reconnectPoll" | "devflow:chooseApp";
}

function withInspectorStartupHints(
  inspectorUrl: string,
  hints: InspectorStartupHints | undefined,
): string {
  const url = new URL(inspectorUrl);
  for (const [key, value] of [
    ["test", hints?.test],
    ["trace", hints?.trace],
    ["agentRequest", hints?.agentRequest],
    ["element", hints?.element],
    ["problem", hints?.problem],
    ["run", hints?.run],
    ["view", hints?.view],
  ] as const) {
    if (typeof value !== "string") continue;
    const bounded = value.trim();
    if (!bounded || bounded.length > 2048) continue;
    url.searchParams.set(key, bounded);
  }
  return url.toString();
}

function inspectorConnectionSignature(
  brokerPort: number,
  embedToken: string | undefined,
  agent: AgentRegistration,
): string {
  return [
    brokerPort,
    embedToken ?? "",
    agentRuntimeIdentity(agent),
  ].join("|");
}

// ── Host bridge handlers (the shared inspector calls these via postMessage → relay → extension) ──

interface BridgeMessage {
  type?: string;
  bridgeId?: string;
  payload?: CopilotPayload;
  prompt?: string;
  title?: string;
  intent?: string;
  file?: string;
  line?: number;
  column?: number;
  sourceHash?: string;
  name?: string;
  markdown?: string;
  bundle?: TestBundle;
  element?: SelectedElement | null;
  snapshot?: DataSnapshot;
  context?: "selection" | "workflow" | "combined";
  proposalId?: string;
  proposal?: unknown;
  fileRelativePath?: string;
  patchDigest?: string;
  diff?: string;
  approvalRequestId?: string;
  kind?: string;
  approvedScope?: unknown;
  grantDurationSeconds?: number;
  appName?: string;
  platform?: string;
  scopeSummary?: string;
  requestId?: string;
}

interface CopilotPayload {
  element?: { type?: string; automationId?: string | null; text?: string | null; id?: string | null } | null;
  markdown?: string | null;
  markdownTruncated?: boolean;
  appName?: string | null;
}

async function handleBridgeMessage(msg: BridgeMessage | undefined, panelState: PanelBridgeState): Promise<BridgeResult> {
  if (!msg || typeof msg.type !== "string") return { ok: false, error: "Invalid DevFlow bridge message." };
  if (typeof msg.bridgeId !== "string" || msg.bridgeId !== panelState.bridgeId) {
    return { ok: false, error: "Invalid or stale DevFlow bridge message." };
  }
  switch (msg.type) {
    case "devflow:sendToCopilot":
      await sendToCopilot(msg.payload, panelState);
      return { ok: true };
    case "devflow:attachCopilot":
      await sendToCopilot(msg.payload, panelState);
      return { ok: true, message: "Added Inspector context to Copilot." };
    case "devflow:requestTestProposal":
      return await sendAgentRequest(msg.prompt, msg.title);
    case "devflow:pickWorkflow":
      return await pickWorkflowFile();
    case "devflow:openSource":
      await openSource(msg.file, msg.line, msg.column, msg.sourceHash);
      return { ok: true };
    case "devflow:recordingComplete":
      await saveRecording(msg.name, msg.markdown);
      return { ok: true };
    case "devflow:saveTestBundle":
      return await saveTestBundle(msg.bundle);
    case "devflow:loadTestBundle":
      return await loadTestBundle();
    case "devflow:pickTrace":
      return await pickTraceArtifact();
    case "devflow:openSourceDiff":
      return await openSourceDiff(msg.diff, msg.fileRelativePath);
    case "devflow:nativeApproval":
      return await approveAgentRequestNatively(msg);
    case "devflow:layoutPolicyMutation":
      return await approveLayoutPolicyMutation(msg);
    case "devflow:selectionChanged":
      panelState.selection = msg.element ?? null;
      return { ok: true };
    case "devflow:attachData":
      return await attachDataToCopilot(msg.snapshot, panelState);
    default:
      return { ok: false, error: "Unsupported DevFlow bridge message." };
  }

  async function approveAgentRequestNatively(message: BridgeMessage): Promise<BridgeResult> {
    const request = {
      approvalRequestId: message.approvalRequestId,
      kind: message.kind,
      intent: message.intent,
      approvedScope: message.approvedScope,
      grantDurationSeconds: message.grantDurationSeconds,
      appName: message.appName,
      platform: message.platform,
      scopeSummary: message.scopeSummary,
    };
    if (!isNativeApprovalRequest(request)) {
      return { ok: false, error: "The Inspector supplied an invalid native approval request." };
    }

    const action = "Approve exact DevFlow request";
    const choice = await vscode.window.showWarningMessage(
      [
        `Approve this exact DevFlow ${request.kind} request for ${request.appName} (${request.platform})?`,
        `Intent: ${request.intent}`,
        `Scope: ${request.scopeSummary}`,
        `Actions: ${request.approvedScope.allowedActions.join(", ")}`,
        `Selectors: ${request.approvedScope.allowedSelectors.join(", ") || "none"}`,
        `Routes: ${request.approvedScope.allowedRoutes.join(", ") || "none"}`,
        `Side effects: ${request.approvedScope.allowedSideEffectClasses.join(", ") || "none"}`,
        `Limits: ${request.approvedScope.maxActionCount} actions; ${request.approvedScope.maxValueBytes} value bytes; up to ${request.grantDurationSeconds}s grant, capped by the authoring session.`,
      ].join("\n"),
      { modal: true },
      action,
    );
    if (choice !== action) {
      return { ok: false, error: "The VS Code user did not approve the exact DevFlow request." };
    }

    const { readBrokerState } = await import("@maui-devflow/client");
    return performNativeApproval(request, {
      brokerPort: panelState.brokerPort ?? 0,
      agentId: panelState.agent?.id ?? "",
    }, readBrokerState);
  }

  /// Layout suppression is a policy-file write, so VS Code — the only native approval host — asks
  /// the trusted local Inspector to issue a confirmation bound to the exact policy digest and the
  /// exact proposal digest it displays. A drifted file or a re-run scan invalidates the digests and
  /// the write fails closed.
  async function approveLayoutPolicyMutation(message: BridgeMessage): Promise<BridgeResult> {
    const proposal = message.proposal as Record<string, unknown> | undefined;
    const bounded = (value: unknown, maximum: number): value is string =>
      typeof value === "string" && value.length > 0 && value.length <= maximum;
    if (!proposal ||
        !bounded(proposal.proposalId, 256) ||
        !bounded(proposal.expectedPolicyDigest, 128) ||
        !bounded(proposal.proposalDigest, 128)) {
      return { ok: false, error: "The Inspector supplied an invalid layout policy proposal." };
    }

    const { readBrokerState } = await import("@maui-devflow/client");
    const state = readBrokerState() as ({ port: number; nativeApprovalToken?: string | null }) | null;
    const token = state?.nativeApprovalToken;
    if (!state || state.port !== panelState.brokerPort || !bounded(token, 256))
      return { ok: false, error: "The trusted broker approval token is unavailable or targets another Inspector." };

    // The human gate runs before the capability is issued. A confirmation is single-use and
    // short-lived, so issuing it first would burn its lifetime while the modal sits open — and a
    // declined modal would leave a valid capability behind for the rest of the window.
    if (!bounded(proposal.policyPath, 4096) ||
        !bounded(proposal.action, 64) ||
        !bounded(proposal.findingId, 512) ||
        !bounded(proposal.suppressionKey, 512) ||
        !bounded(proposal.reason, 512)) {
      return { ok: false, error: "The Inspector supplied an invalid layout policy proposal." };
    }
    const proposedUri = vscode.Uri.file(proposal.policyPath);
    const proposedWorkspace = vscode.workspace.getWorkspaceFolder(proposedUri);
    if (!proposedWorkspace) {
      return { ok: false, error: "The reviewed layout policy path is outside the current VS Code workspace." };
    }
    const proposedRelative = path.relative(proposedWorkspace.uri.fsPath, proposedUri.fsPath);
    if (proposedRelative === ".." ||
        proposedRelative.startsWith(`..${path.sep}`) ||
        path.isAbsolute(proposedRelative)) {
      return { ok: false, error: "The reviewed layout policy path escapes the current VS Code workspace." };
    }
    const action = proposal.action === "remove-exact-suppression"
      ? "Remove exact suppression"
      : "Add exact suppression";
    const choice = await vscode.window.showWarningMessage(
      [
        `${action} in ${proposal.policyPath}?`,
        `Finding: ${proposal.findingId}`,
        `Suppression key: ${proposal.suppressionKey}`,
        `Reason: ${proposal.reason}`,
        "The write will fail if the file or diagnostics revision changed after this review.",
      ].join("\n"),
      { modal: true },
      action,
    );
    if (choice !== action)
      return { ok: false, error: "The VS Code user did not approve the layout policy change." };

    const root = `http://localhost:${state.port}/inspector/${encodeURIComponent(panelState.agent?.id ?? "")}/api/workbench`;
    const response = await fetch(`${root}/approval-confirmations/issue`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-DevFlow-Host-Approval-Token": token,
      },
      body: JSON.stringify({
        action: "layout-policy-mutation",
        subjectId: proposal.proposalId,
        contentDigest: proposal.expectedPolicyDigest,
        patchDigest: proposal.proposalDigest,
      }),
    });
    const body = await response.json().catch(() => null) as any;
    if (!response.ok || !bounded(body?.confirmationCapability, 512)) {
      return {
        ok: false,
        error: typeof body?.error === "string"
          ? body.error
          : `The trusted host could not confirm the layout policy proposal (HTTP ${response.status}).`,
      };
    }
    // The trusted Inspector must describe the same change the human just read. Anything else means
    // the page and the broker disagree about what was approved.
    const review = body?.layoutPolicyReview as Record<string, unknown> | undefined;
    if (!review ||
        review.action !== proposal.action ||
        review.findingId !== proposal.findingId ||
        review.suppressionKey !== proposal.suppressionKey ||
        review.reason !== proposal.reason ||
        review.policyPath !== proposal.policyPath ||
        !bounded(review.projectFile, 128) ||
        review.expectedPolicyDigest !== proposal.expectedPolicyDigest ||
        review.proposalDigest !== proposal.proposalDigest) {
      return { ok: false, error: "The trusted Inspector returned mismatched layout policy review data." };
    }
    // The capability is single-use and digest-bound, and it is the authority the apply route
    // checks. Spending it here keeps it out of the webview entirely: the page only learns whether
    // the write succeeded.
    const applyResponse = await fetch(
      `http://localhost:${state.port}/inspector/${encodeURIComponent(panelState.agent?.id ?? "")}/api/diagnostics/suppression/apply`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          proposalId: proposal.proposalId,
          confirmationCapability: body.confirmationCapability,
        }),
      });
    const applyBody = await applyResponse.json().catch(() => null) as any;
    if (!applyResponse.ok || applyBody?.ok !== true) {
      return {
        ok: false,
        error: typeof applyBody?.error === "string"
          ? applyBody.error
          : `The layout suppression could not be written (HTTP ${applyResponse.status}).`,
      };
    }
    return {
      ok: true,
      message: proposal.action === "remove-exact-suppression"
        ? "Removed the exact project layout suppression."
        : "Saved an exact layout suppression in .mauidevflow.",
    };
  }


  async function openSourceDiff(diff: string | undefined, fileRelativePath: string | undefined): Promise<BridgeResult> {
    if (typeof diff !== "string" || !diff || Buffer.byteLength(diff, "utf8") > 1024 * 1024)
      return { ok: false, error: "The reviewed source diff is missing or exceeds 1 MB." };
    try {
      const document = await vscode.workspace.openTextDocument({
        content: diff,
        language: "diff",
      });
      await vscode.window.showTextDocument(document, { preview: true });
      return { ok: true, message: `Opened reviewed source diff${fileRelativePath ? ` for ${fileRelativePath}` : ""}.` };
    } catch {
      return { ok: false, error: "VS Code could not open the reviewed XAML diff." };
    }
  }

}

const FLOW_RUN_MAX_BYTES = 1024 * 1024;
const EVIDENCE_MAX_BYTES = 64 * 1024 * 1024;

async function pickTraceArtifact(): Promise<BridgeResult> {
  const picked = await vscode.window.showOpenDialog({
    canSelectMany: false,
    canSelectFiles: true,
    canSelectFolders: false,
    filters: { "DevFlow trace": ["json", "mauitrace"] },
    title: "Choose a bounded DevFlow flow-run report or evidence bundle",
  });
  const file = picked?.[0];
  if (!file) return { ok: false, error: "Trace selection was cancelled." };

  const name = path.basename(file.fsPath);
  const lower = name.toLowerCase();
  const kind = lower.endsWith(".mauitrace")
    ? "mauitrace"
    : lower.endsWith(".json")
      ? "flow-run"
      : null;
  if (!kind) return { ok: false, error: "Choose a .json flow-run report or .mauitrace v1 bundle." };

  try {
    const stat = await vscode.workspace.fs.stat(file);
    const maximum = kind === "flow-run" ? FLOW_RUN_MAX_BYTES : EVIDENCE_MAX_BYTES;
    if (stat.size <= 0 || stat.size > maximum) {
      return {
        ok: false,
        error: `${kind === "flow-run" ? "flow-run.json" : ".mauitrace"} must be 1 byte to ${Math.floor(maximum / (1024 * 1024))} MB.`,
      };
    }
    const bytes = await vscode.workspace.fs.readFile(file);
    if (bytes.byteLength !== stat.size || bytes.byteLength > maximum) {
      return { ok: false, error: "The selected trace changed or exceeded its bounded import size." };
    }
    return {
      ok: true,
      value: {
        name,
        kind,
        bytesBase64: Buffer.from(bytes).toString("base64"),
      },
    };
  } catch {
    return { ok: false, error: "Could not read the selected trace artifact." };
  }
}

async function sendToCopilot(payload: CopilotPayload | undefined, panelState: PanelBridgeState): Promise<void> {
  const carriesElement = !!payload && Object.prototype.hasOwnProperty.call(payload, "element");
  if (carriesElement) panelState.selection = payload?.element ?? null;
  const el = panelState.selection;


  const bits: string[] = [];
  if (el) {
    const parts = [el.type ?? "Element"];
    if (el.automationId) parts.push(`AutomationId="${el.automationId}"`);
    if (el.text) parts.push(`text="${el.text}"`);
    bits.push(`Selected .NET MAUI element: ${parts.join(" ")}.`);
  }
  if (payload?.markdown) bits.push("\nRecorded steps:\n" + payload.markdown + (payload.markdownTruncated ? "\n…(truncated)" : ""));
  const context = bits.join("\n") || "No element is selected in the MAUI DevFlow Inspector.";
  if (!el && payload?.markdown) {
    try {
      await vscode.commands.executeCommand("workbench.action.chat.open", {
        query: context,
        isPartialQuery: true,
      });
      return;
    } catch {
      await vscode.env.clipboard.writeText(context);
      vscode.window.showInformationMessage("DevFlow: workflow context copied for Copilot.");
      return;
    }
  }
  await attachToolContext(
    context,
    "Added the MAUI selection to Copilot.",
    "Copied the MAUI selection context for Copilot.",
    "DevFlow: Copilot Chat unavailable — selection context copied to the clipboard.");
}

async function sendAgentRequest(prompt: string | undefined, title: string | undefined): Promise<BridgeResult> {
  const request = typeof prompt === "string" ? prompt.trim() : "";
  if (!request || request.length > 8192) {
    return { ok: false, error: "The DevFlow agent request must contain 1 to 8192 characters." };
  }
  try {
    await vscode.commands.executeCommand("workbench.action.chat.open", {
      query: request,
      isPartialQuery: false,
    });
    const requestTitle = typeof title === "string" ? title.trim().slice(0, 120) : "";
    return {
      ok: true,
      message: requestTitle ? `Sent “${requestTitle}” to Copilot Chat.` : "Sent the DevFlow request to Copilot Chat.",
      value: { delivery: "agent" },
    };
  } catch {
    await vscode.env.clipboard.writeText(request);
    vscode.window.showInformationMessage("DevFlow: Copilot Chat unavailable — agent request copied.");
    return {
      ok: true,
      message: "Copilot Chat was unavailable, so the agent request was copied.",
      value: { delivery: "clipboard" },
    };
  }
}

async function pickWorkflowFile(): Promise<BridgeResult> {
  const picked = await vscode.window.showOpenDialog({
    canSelectMany: false,
    canSelectFiles: true,
    canSelectFolders: false,
    filters: { "DevFlow workflow": ["md"] },
    title: "Choose a DevFlow workflow test",
  });
  const file = picked?.[0];
  if (!file) return { ok: false, error: "Workflow selection was cancelled." };
  try {
    const stat = await vscode.workspace.fs.stat(file);
    if (stat.size > 1024 * 1024)
      return { ok: false, error: "Workflow test files larger than 1 MB cannot be loaded." };
    const bytes = await vscode.workspace.fs.readFile(file);
    return {
      ok: true,
      name: path.basename(file.fsPath),
      markdown: Buffer.from(bytes).toString("utf8"),
    };
  } catch {
    return { ok: false, error: "Could not read the selected workflow test." };
  }
}

const dataSnapshotScopes = new Set(["logs", "network", "preferences", "device", "sensors", "files", "alerts"]);
const DATA_SNAPSHOT_MAX_BYTES = 20_000;

async function attachDataToCopilot(snapshot: DataSnapshot | undefined, panelState: PanelBridgeState): Promise<BridgeResult> {
  if (!snapshot || snapshot.kind !== "dataSnapshot" || snapshot.redacted !== true
      || !dataSnapshotScopes.has(snapshot.scope) || typeof snapshot.title !== "string") {
    const error = "The Data snapshot was invalid and was not added to Copilot.";
    vscode.window.showWarningMessage(`DevFlow: ${error}`);
    return { ok: false, error };
  }
  let serialized: string;
  try {
    serialized = JSON.stringify(snapshot);
  } catch {
    return { ok: false, error: "The Data snapshot could not be serialized." };
  }
  if (Buffer.byteLength(serialized, "utf8") > DATA_SNAPSHOT_MAX_BYTES) {
    const error = "The Data snapshot exceeded the safe context size and was not added.";
    vscode.window.showWarningMessage(`DevFlow: ${error}`);
    return { ok: false, error };
  }
  panelState.dataSnapshot = JSON.parse(serialized) as DataSnapshot;
  const fallback = "MAUI DevFlow Data snapshot:\n" + JSON.stringify(panelState.dataSnapshot, null, 2);
  return await attachToolContext(
    fallback,
    `Added ${panelState.dataSnapshot.title} to Copilot.`,
    "Copied the Data context for Copilot.",
    "DevFlow: Copilot Chat unavailable — Data context copied to the clipboard.");
}

/**
 * Puts one bounded piece of Inspector context into Copilot Chat. This layer registers no
 * language-model tools and no chat participant, so there is no `#reference` or tool id to hand the
 * chat view: the context travels as literal text, and the clipboard is the last resort. Naming a
 * tool that is not contributed would open a chat turn that silently resolves to nothing.
 */
async function attachToolContext(
  fallbackText: string,
  attachedMessage: string,
  copiedMessage: string,
  clipboardMessage: string,
): Promise<BridgeResult> {
  try {
    await vscode.commands.executeCommand("workbench.action.chat.open", {
      query: fallbackText,
      isPartialQuery: true,
    });
    return { ok: true, message: attachedMessage };
  } catch {
    try {
      await vscode.env.clipboard.writeText(fallbackText);
      vscode.window.showInformationMessage(clipboardMessage);
      return { ok: true, message: copiedMessage };
    } catch {
      return { ok: false, error: "Copilot Chat was unavailable and the context could not be copied." };
    }
  }
}

async function openSource(
  file: string | undefined,
  line: number | undefined,
  column: number | undefined,
  sourceHash: string | undefined,
): Promise<void> {
  if (typeof file !== "string" || !file.trim()) return;
  const raw = file.trim();

  // Resolve file: URIs first so `file:///C:/…` works; reject any OTHER URL scheme (http:, vscode:, …)
  // but never mistake a Windows drive letter ("C:\") for a scheme. This is a hard security gate —
  // the path arrives over a postMessage bridge.
  let fsPath: string;
  const isDriveLetter = /^[a-zA-Z]:[\\/]/.test(raw);
  if (/^file:/i.test(raw)) {
    try {
      fsPath = vscode.Uri.parse(raw).fsPath;
    } catch {
      return;
    }
  } else if (/^[a-zA-Z][a-zA-Z0-9+.-]*:/.test(raw) && !isDriveLetter) {
    vscode.window.showWarningMessage("DevFlow: refusing to open a non-local source path.");
    return;
  } else {
    fsPath = raw;
  }

  // Hard-block UNC and Windows device namespaces in BOTH slash forms (\\server, //server, \\?\, //./)
  // on the RESOLVED path — these can reach network shares (NTLM credential exposure) or raw devices.
  if (/^[\\/]{2}/.test(fsPath)) {
    vscode.window.showWarningMessage("DevFlow: refusing to open a UNC/network source path.");
    return;
  }
  if (!path.isAbsolute(fsPath)) {
    vscode.window.showWarningMessage("DevFlow: source path is not absolute.");
    return;
  }

  const folders = vscode.workspace.workspaceFolders ?? [];
  const inside = folders.some((f) => isInside(f.uri.fsPath, fsPath));
  if (!inside) {
    const pick = await vscode.window.showWarningMessage(
      `Open a source file outside your workspace?\n${fsPath}`,
      { modal: true },
      "Open"
    );
    if (pick !== "Open") return;
  }

  const ln = Math.max(0, (Number(line) || 1) - 1);
  const col = Math.max(0, (Number(column) || 1) - 1);
  try {
    const doc = await vscode.workspace.openTextDocument(vscode.Uri.file(fsPath));
    if (sourceHash) {
      const currentHash = createHash("sha256").update(doc.getText(), "utf8").digest("hex").slice(0, 16);
      if (currentHash !== sourceHash.toLowerCase()) {
        const choice = await vscode.window.showWarningMessage(
          "DevFlow: this XAML file changed after the running app was built, so the recorded source line may be stale.",
          { modal: true },
          "Open Anyway",
        );
        if (choice !== "Open Anyway") return;
      }
    }
    const editor = await vscode.window.showTextDocument(doc, { preview: true });
    const pos = new vscode.Position(ln, col);
    editor.selection = new vscode.Selection(pos, pos);
    editor.revealRange(new vscode.Range(pos, pos), vscode.TextEditorRevealType.InCenter);
  } catch {
    vscode.window.showErrorMessage(`DevFlow: could not open ${fsPath}`);
  }
}

async function saveRecording(name: string | undefined, markdown: string | undefined): Promise<void> {
  if (typeof markdown !== "string" || markdown.length === 0) return;
  const safe = (typeof name === "string" && name ? name : "recording").replace(/[^A-Za-z0-9_.-]/g, "_");
  const folders = vscode.workspace.workspaceFolders ?? [];
  if (folders.length)
    await vscode.workspace.fs.createDirectory(vscode.Uri.joinPath(folders[0].uri, "maui-tests"));
  const defaultUri = folders.length
    ? vscode.Uri.joinPath(folders[0].uri, "maui-tests", `${safe}.md`)
    : vscode.Uri.file(`${safe}.md`);
  const target = await vscode.window.showSaveDialog({
    defaultUri,
    filters: { Markdown: ["md"] },
    title: "Save DevFlow recording",
  });
  if (!target) return;
  try {
    await vscode.workspace.fs.writeFile(target, Buffer.from(markdown, "utf8"));
    const doc = await vscode.workspace.openTextDocument(target);
    await vscode.window.showTextDocument(doc, { preview: false });
    vscode.window.showInformationMessage(`DevFlow recording saved: ${target.fsPath}`);
  } catch {
    vscode.window.showErrorMessage("DevFlow: could not save the recording.");
  }
}

const TEST_BUNDLE_MAX_BYTES = 1024 * 1024;

function safeTestBundleName(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const name = value.trim();
  if (!name || name.length > 255 || !/\.md$/i.test(name) ||
      name.includes("/") || name.includes("\\") || path.basename(name) !== name)
    return null;
  return name;
}

function safeDigest(value: unknown): string | null {
  return typeof value === "string" && /^[a-f0-9]{64}$/i.test(value) ? value.toLowerCase() : null;
}

function bundlePlanName(flowName: string): string {
  return flowName.replace(/\.md$/i, "") + ".maui-plan.json";
}

function canonicalBundleJson(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(canonicalBundleJson);
  if (value && typeof value === "object") {
    return Object.fromEntries(Object.keys(value as Record<string, unknown>)
      .sort()
      .map((key) => [key, canonicalBundleJson((value as Record<string, unknown>)[key])]));
  }
  return value;
}

function canonicalBundleDigest(value: unknown): string {
  return createHash("sha256").update(JSON.stringify(canonicalBundleJson(value)), "utf8").digest("hex");
}

function flowPayload(markdown: string): unknown | null {
  const match = /```json maui-test\s*\r?\n([\s\S]*?)\r?\n```/.exec(markdown);
  if (!match) return null;
  try { return JSON.parse(match[1]); } catch { return null; }
}

async function testRoot(): Promise<vscode.Uri | null> {
  const folder = vscode.workspace.workspaceFolders?.[0];
  if (!folder) return null;
  const root = vscode.Uri.joinPath(folder.uri, "maui-tests");
  try {
    await vscode.workspace.fs.createDirectory(root);
    const stat = await vscode.workspace.fs.stat(root);
    if ((stat.type & vscode.FileType.SymbolicLink) !== 0) return null;
    return root;
  } catch {
    return null;
  }
}

async function isSafeRegularFile(uri: vscode.Uri): Promise<boolean> {
  try {
    const stat = await vscode.workspace.fs.stat(uri);
    return (stat.type & vscode.FileType.SymbolicLink) === 0;
  } catch {
    return true; // a non-existing destination is safe to create beneath the checked root
  }
}

async function saveTestBundle(bundle: TestBundle | undefined): Promise<BridgeResult> {
  const name = safeTestBundleName(bundle?.name);
  const markdown = typeof bundle?.markdown === "string" ? bundle.markdown : null;
  const planJson = typeof bundle?.planJson === "string" ? bundle.planJson : null;
  const flowDigest = safeDigest(bundle?.flowDigest);
  const planDigest = bundle?.planDigest == null ? null : safeDigest(bundle.planDigest);
  if (!name || !markdown || !planJson || !flowDigest || (bundle?.planDigest != null && !planDigest))
    return { ok: false, error: "The test bundle must contain a bounded flow, plan, and SHA-256 digests." };
  if (Buffer.byteLength(markdown, "utf8") > TEST_BUNDLE_MAX_BYTES ||
      Buffer.byteLength(planJson, "utf8") > TEST_BUNDLE_MAX_BYTES)
    return { ok: false, error: "Flow and plan files must each be 1 MB or smaller." };

  let plan: any;
  try { plan = JSON.parse(planJson); } catch { return { ok: false, error: "The plan sidecar is not valid JSON." }; }
  if (!plan || typeof plan !== "object" || plan.schema !== 1 ||
      plan.flow?.path !== name || plan.flow?.digest !== flowDigest)
    return { ok: false, error: "The plan sidecar is not bound to the requested flow filename and digest." };
  const payload = flowPayload(markdown);
  if (!payload || canonicalBundleDigest(payload) !== flowDigest)
    return { ok: false, error: "The flow digest does not match the authoritative maui-test payload." };
  if (planDigest && canonicalBundleDigest(plan) !== planDigest)
    return { ok: false, error: "The plan digest does not match the submitted plan sidecar." };

  const root = await testRoot();
  if (!root) return { ok: false, error: "No safe workspace maui-tests directory is available." };
  const flow = vscode.Uri.joinPath(root, name);
  const sidecar = vscode.Uri.joinPath(root, bundlePlanName(name));
  if (!await isSafeRegularFile(flow) || !await isSafeRegularFile(sidecar))
    return { ok: false, error: "Refusing to overwrite a symbolic-link test artifact." };

  const nonce = randomBytes(12).toString("hex");
  const staged = [
    {
      target: flow,
      temporary: flow.with({ path: flow.path + `.${nonce}.tmp` }),
      backup: flow.with({ path: flow.path + `.${nonce}.bak` }),
      content: Buffer.from(markdown, "utf8"),
    },
    {
      target: sidecar,
      temporary: sidecar.with({ path: sidecar.path + `.${nonce}.tmp` }),
      backup: sidecar.with({ path: sidecar.path + `.${nonce}.bak` }),
      content: Buffer.from(planJson, "utf8"),
    },
  ];
  const previous = new Map<string, Uint8Array | null>();
  let preserveBackups = false;
  try {
    for (const item of staged) {
      let bytes: Uint8Array | null = null;
      try { bytes = await vscode.workspace.fs.readFile(item.target); } catch {}
      previous.set(item.target.fsPath, bytes);
      if (bytes) await vscode.workspace.fs.writeFile(item.backup, bytes);
      await vscode.workspace.fs.writeFile(item.temporary, item.content);
    }
    for (const item of staged)
      await vscode.workspace.fs.rename(item.temporary, item.target, { overwrite: true });
    const document = await vscode.workspace.openTextDocument(flow);
    await vscode.window.showTextDocument(document, { preview: false });
    return { ok: true, message: `Saved ${name} and ${bundlePlanName(name)} in maui-tests.` };
  } catch {
    let restored = true;
    for (const item of [...staged].reverse()) {
      try {
        const prior = previous.get(item.target.fsPath);
        if (prior) {
          try { await vscode.workspace.fs.rename(item.backup, item.target, { overwrite: true }); }
          catch { await vscode.workspace.fs.writeFile(item.target, prior); }
        }
        else {
          try { await vscode.workspace.fs.delete(item.target, { recursive: false, useTrash: false }); } catch {}
        }
      } catch { restored = false; }
    }
    preserveBackups = !restored;
    return {
      ok: false,
      error: restored
        ? "The bundle was not saved; the previous flow and plan were restored."
        : "The bundle write failed and the prior files could not be fully restored.",
    };
  } finally {
    for (const item of staged) {
      try { await vscode.workspace.fs.delete(item.temporary, { recursive: false, useTrash: false }); } catch {}
      try {
        if (!preserveBackups)
          await vscode.workspace.fs.delete(item.backup, { recursive: false, useTrash: false });
      } catch {}
    }
  }
}

async function loadTestBundle(): Promise<BridgeResult> {
  const root = await testRoot();
  if (!root) return { ok: false, error: "No safe workspace maui-tests directory is available." };
  const picked = await vscode.window.showOpenDialog({
    canSelectMany: false,
    canSelectFiles: true,
    canSelectFolders: false,
    defaultUri: root,
    filters: { "DevFlow workflow": ["md"] },
    title: "Load a DevFlow test bundle",
  });
  const flow = picked?.[0];
  if (!flow) return { ok: false, error: "Test bundle selection was cancelled." };
  const name = safeTestBundleName(path.basename(flow.fsPath));
  if (!name || path.dirname(flow.fsPath) !== root.fsPath || !isInside(root.fsPath, flow.fsPath))
    return { ok: false, error: "Only top-level maui-tests Markdown files can be loaded." };
  const sidecar = vscode.Uri.joinPath(root, bundlePlanName(name));
  try {
    if (!await isSafeRegularFile(flow) || !await isSafeRegularFile(sidecar))
      return { ok: false, error: "Refusing to load a symbolic-link test artifact." };
    const flowBytes = await vscode.workspace.fs.readFile(flow);
    if (flowBytes.byteLength > TEST_BUNDLE_MAX_BYTES)
      return { ok: false, error: "Workflow files larger than 1 MB cannot be loaded." };
    let planJson: string | undefined;
    try {
      const planBytes = await vscode.workspace.fs.readFile(sidecar);
      if (planBytes.byteLength > TEST_BUNDLE_MAX_BYTES)
        return { ok: false, error: "Plan sidecars larger than 1 MB cannot be loaded." };
      planJson = Buffer.from(planBytes).toString("utf8");
    } catch {
      // A Markdown flow remains useful without a plan; the shared UI makes that state explicit.
    }
    return { ok: true, name, markdown: Buffer.from(flowBytes).toString("utf8"), planJson };
  } catch {
    return { ok: false, error: "Could not load the selected test bundle." };
  }
}

function isInside(root: string, target: string): boolean {
  const rel = path.relative(root, target);
  return rel.length > 0 && !rel.startsWith("..") && !path.isAbsolute(rel);
}

// `mauiDevflow.openLocation` placement: "beside"/"active" are explicit; "auto" (default) picks the
// column that keeps the user's code visible — beside the active editor when one is open, or the
// active (otherwise-empty) group when it isn't, so the inspector doesn't strand itself in a second
// empty column.
function resolveViewColumn(openLocation: string | undefined): vscode.ViewColumn {
  if (openLocation === "beside") return vscode.ViewColumn.Beside;
  if (openLocation === "active") return vscode.ViewColumn.Active;
  return vscode.window.activeTextEditor ? vscode.ViewColumn.Beside : vscode.ViewColumn.Active;
}

async function pickAgent(agents: AgentRegistration[]): Promise<AgentRegistration | undefined> {
  if (agents.length === 1) return agents[0];
  const pick = await vscode.window.showQuickPick(
    agents.map((a) => ({
      label: a.appName ?? a.id,
      description: `${a.platform ?? "?"} · port ${a.port}`,
      agent: a,
    })),
    { placeHolder: "Select a running MAUI app to inspect" }
  );
  return pick?.agent;
}

function renderHost(inspectorUrl: string, appName: string, nonce: string, bridgeId: string): string {
  // The shared inspector runs on localhost; embed it in an iframe. On desktop the iframe keeps its
  // http://localhost origin, but in Remote/WSL/web VS Code serves it through a
  // `https://<port>-<uuid>.vscode-webview.net` proxy origin, so the webview CSP frame-src allows
  // both. The single relay <script> is the only script and is pinned to `nonce`.
  const frameSrc = jsString(`${inspectorUrl}#devflowBridge=${bridgeId}`);
  const bridgeLiteral = jsString(bridgeId);
  const hostManifestLiteral = jsJson(createInspectorHostManifest({
    hostId: "vscode",
    hostLabel: "VS Code Inspector",
    interactionSessionId: bridgeId,
    capabilities: VSCODE_HOST_CAPABILITIES,
  }));
  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta http-equiv="Content-Security-Policy"
        content="default-src 'none'; frame-src http://127.0.0.1:* http://localhost:* https://*.vscode-webview.net; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';" />
  <title>${escapeHtml(inspectorTitle(appName))}</title>
  <style>
    /* Fill the whole webview: no host chrome (the editor tab already names the panel) and no VS Code
       default body padding, so the shared inspector sits flush to the panel edges. */
    html, body { margin: 0 !important; padding: 0 !important; height: 100%; overflow: hidden; background: var(--vscode-editor-background, #1e1e1e); }
    #frame { position: fixed; inset: 0; width: 100%; height: 100%; border: 0; display: block; }
  </style>
</head>
<body>
  <iframe id="frame" sandbox="allow-scripts allow-forms allow-same-origin"></iframe>
  <script nonce="${nonce}">
    (function () {
      const vscode = acquireVsCodeApi();
      const frame = document.getElementById('frame');
      const bridgeId = ${bridgeLiteral};
      const hostManifest = ${hostManifestLiteral};
      const capabilities = hostManifest.capabilities;
      // Map the shared inspector's semantic theme tokens onto VS Code's theme colors so the panel
      // adopts the user's active color theme (light / dark / high-contrast). getComputedStyle resolves
      // each --vscode-* var to a concrete color; the inspector re-validates every value before use.
      // Values may list several VS Code color IDs in priority order — e.g. the high-contrast-only
      // '--vscode-contrastBorder'/'--vscode-contrastActiveBorder' tokens are empty outside an HC theme,
      // so listing them first gives HC users crisper borders/outlines without affecting other themes.
      const THEME_MAP = {
        '--df-bg': '--vscode-editor-background', '--df-surface': '--vscode-sideBar-background',
        '--df-surface-2': '--vscode-editorWidget-background', '--df-fg': '--vscode-editor-foreground',
        '--df-muted': '--vscode-descriptionForeground',
        '--df-border': ['--vscode-contrastBorder', '--vscode-panel-border'],
        '--df-border-subtle': ['--vscode-contrastBorder', '--vscode-editorWidget-border'],
        '--df-hover': '--vscode-toolbar-hoverBackground',
        '--df-hover-row': '--vscode-list-hoverBackground', '--df-accent': '--vscode-button-background',
        '--df-accent-fg': '--vscode-button-foreground', '--df-selected': '--vscode-list-activeSelectionBackground',
        '--df-selected-fg': '--vscode-list-activeSelectionForeground', '--df-danger': '--vscode-errorForeground',
        '--df-focus': ['--vscode-focusBorder'], '--df-warn': '--vscode-editorWarning-foreground',
        '--df-error': '--vscode-errorForeground',
        // Semantic tokens the shared inspector uses for tree/property-grid syntax coloring.
        '--df-type': '--vscode-symbolIcon-classForeground', '--df-name': '--vscode-symbolIcon-variableForeground',
        '--df-source': '--vscode-symbolIcon-fileForeground', '--df-success': '--vscode-testing-iconPassed',
        '--df-outline-hover': ['--vscode-focusBorder'],
        '--df-outline-select': ['--vscode-contrastActiveBorder', '--vscode-list-focusOutline']
      };
      function currentModeKind() {
        // VS Code sets the theme class (vscode-light / vscode-dark / vscode-high-contrast[-light]) and
        // data-vscode-theme-kind on <body>; check <html> too for resilience across VS Code versions.
        const b = document.body, r = document.documentElement;
        return (b.getAttribute('data-vscode-theme-kind') || r.getAttribute('data-vscode-theme-kind') || b.className || r.className || '');
      }
      function currentMode() {
        const k = currentModeKind();
        if (/light/i.test(k)) return 'light';           // covers vscode-light and vscode-high-contrast-light
        return 'dark';                                   // vscode-dark + vscode-high-contrast (HC colors ride in via palette)
      }
      function readVars(el) {
        const cs = getComputedStyle(el);
        const out = {};
        for (const key in THEME_MAP) {
          const candidates = Array.isArray(THEME_MAP[key]) ? THEME_MAP[key] : [THEME_MAP[key]];
          for (const cand of candidates) {
            const v = cs.getPropertyValue(cand).trim();
            if (v) { out[key] = v; break; }
          }
        }
        return out;
      }
      function buildTheme() {
        // Theme vars are defined on :root; body inherits them. Read both and prefer the :root values so a
        // missing var on one element still resolves. Even if the palette ends up empty, mode alone fixes
        // the cross-origin iframe's wrong prefers-color-scheme (the white-in-dark-VS-Code symptom).
        const palette = Object.assign({}, readVars(document.body), readVars(document.documentElement));
        return { mode: currentMode(), palette: palette };
      }
      function prefersReducedMotion() {
        // VS Code adds a reduce-motion class to <body> when "workbench.reduceMotion" is on; also
        // honor the OS-level media query as a fallback (both are booleans, never sent as CSS values).
        try {
          if (/reduce[-_]?motion/i.test(document.body.className)) return true;
          return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
        } catch (e) { return false; }
      }
      function safeFontFamily(v) {
        const s = typeof v === 'string' ? v.trim() : '';
        if (!s || s.length > 120 || !/^[A-Za-z0-9 ,'"_.\-]+$/.test(s)) return undefined;
        return s;
      }
      function safeFontSize(v) {
        const s = typeof v === 'string' ? v.trim() : '';
        return /^[0-9]{1,3}(\.[0-9]+)?px$/.test(s) ? s : undefined;
      }
      function safeFontWeight(v) {
        const s = typeof v === 'string' ? v.trim() : '';
        return /^(normal|bold|[1-9]00)$/.test(s) ? s : undefined;
      }
      function buildFont() {
        // Host font metadata only — never routed through theme.palette (that channel is colors-only).
        try {
          const cs = getComputedStyle(document.documentElement);
          const family = safeFontFamily(cs.getPropertyValue('--vscode-font-family'));
          const size = safeFontSize(cs.getPropertyValue('--vscode-font-size'));
          const weight = safeFontWeight(cs.getPropertyValue('--vscode-font-weight'));
          const out = {};
          if (family) out.family = family;
          if (size) out.size = size;
          if (weight) out.weight = weight;
          return Object.keys(out).length ? out : undefined;
        } catch (e) { return undefined; }
      }
      function buildProfile() {
        const profile = { surface: 'editor' };
        if (/high-contrast/i.test(currentModeKind())) profile.contrast = 'high';
        if (prefersReducedMotion()) profile.reducedMotion = true;
        const font = buildFont();
        if (font) profile.font = font;
        return profile;
      }
      function announce() {
        try {
          if (frame.contentWindow) {
            frame.contentWindow.postMessage(Object.assign({}, hostManifest, {
              bridgeId: bridgeId,
              theme: buildTheme(),
              profile: buildProfile()
            }), '*');
          }
        } catch (e) { /* cross-origin during teardown */ }
      }
      function sendTheme() {
        try {
          if (frame.contentWindow) {
            frame.contentWindow.postMessage(Object.assign({ type: 'devflow:theme', v: 1, bridgeId: bridgeId, profile: buildProfile() }, buildTheme()), '*');
          }
        } catch (e) { /* cross-origin during teardown */ }
      }
      // Announce capabilities ONLY in response to the inspector's nonce-authenticated
      // 'devflow:ready' — never unconditionally on iframe 'load', so the bridge secret can't leak to
      // a page the iframe later navigates to (it wouldn't know the fragment nonce to send 'ready').
      window.addEventListener('message', function (e) {
        const d = e.data;
        if (e.source !== frame.contentWindow) {
          if (d && d.type === 'devflow:hostResult' && d.bridgeId === bridgeId && frame.contentWindow) {
            frame.contentWindow.postMessage(d, '*');
          }
          return;
        }
        if (!d || d.bridgeId !== bridgeId) return;                // nonce-authenticated
        if (d.type === 'devflow:ready') { announce(); return; }
        if (d.type === 'devflow:sendToCopilot' || d.type === 'devflow:attachCopilot' || d.type === 'devflow:requestTestProposal' || d.type === 'devflow:pickWorkflow' || d.type === 'devflow:attachData' || d.type === 'devflow:openSource' || d.type === 'devflow:recordingComplete' || d.type === 'devflow:selectionChanged' || d.type === 'devflow:saveTestBundle' || d.type === 'devflow:loadTestBundle' || d.type === 'devflow:pickTrace' || d.type === 'devflow:openSourceDiff' || d.type === 'devflow:nativeApproval' || d.type === 'devflow:layoutPolicyMutation') {
          vscode.postMessage(d);                                  // relay to the extension host
        }
      });
      // Attach the listener BEFORE navigating the iframe so no early 'ready' is lost.
      // Re-push the theme whenever the user switches VS Code color theme (body class / theme-kind flip).
      try {
        new MutationObserver(function () { sendTheme(); }).observe(document.body, { attributes: true, attributeFilter: ['class', 'data-vscode-theme-kind', 'data-vscode-theme-name', 'style'] });
      } catch (e) { /* MutationObserver is always available in a webview */ }
      frame.src = ${frameSrc};
    })();
  </script>
</body>
</html>`;
}

function randomToken(): string {
  // 128-bit URL-safe token (base64url) — safe in a URL fragment and in the [A-Za-z0-9_-] the shared
  // inspector accepts for the bridge nonce.
  return randomBytes(16).toString("base64").replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function escapeHtml(s: string): string {
  return s.replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c] as string);
}

function jsString(s: string): string {
  // Safe string literal for inlining into the <script>: JSON-encode, then neutralize `<` so a value
  // can never terminate the script element.
  return JSON.stringify(s).replace(/</g, "\\u003c");
}

function jsJson(value: unknown): string {
  return JSON.stringify(value).replace(/</g, "\\u003c");
}

export function deactivate(): void {
  // Webview panels are disposed with the extension context; nothing extra to clean up.
}
