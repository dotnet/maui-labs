const PRODUCT_NAME = "MAUI DevFlow Inspector";

export type ReconnectState = "broker" | "app" | "multiple" | "target";
export type ReconnectDiscoveryAction = "wait" | "connect" | "choose";

export interface ReconnectDiscovery<TAgent> {
  port: number;
  agents: TAgent[];
}

export interface ReconnectControllerOptions<TAgent extends { id: string }> {
  discover: () => Promise<ReconnectDiscovery<TAgent> | null>;
  pickAgent: (agents: TAgent[]) => Promise<TAgent | undefined>;
  targetAgent: () => TAgent | undefined;
  matchesTarget: (candidate: TAgent, target: TAgent) => boolean;
  applyConnected: (discovery: ReconnectDiscovery<TAgent>, agent: TAgent) => void;
  applyReconnect: (state: ReconnectState) => void;
  isConnected?: () => boolean;
  isDisposed: () => boolean;
}

export interface ReconnectPollOptions {
  choose?: boolean;
  queueIfBusy?: boolean;
}

export function inspectorTitle(appName?: string | null): string {
  const app = typeof appName === "string" ? appName.trim() : "";
  return app ? `${PRODUCT_NAME} · ${app}` : PRODUCT_NAME;
}

export function reconnectDiscoveryAction(
  agentCount: number,
  chooseRequested: boolean,
): ReconnectDiscoveryAction {
  if (agentCount <= 0) return "wait";
  if (agentCount === 1) return "connect";
  return chooseRequested ? "choose" : "wait";
}

export function sameAgentIdentity(
  left: Pick<ReconnectableAgent, "project" | "tfm" | "platform" | "appName" | "sessionId">,
  right: Pick<ReconnectableAgent, "project" | "tfm" | "platform" | "appName" | "sessionId">,
): boolean {
  const project = normalizeProject(left.project);
  // Filename-only project and default session identities can collide across worktrees.
  if (!project.startsWith("/") && !/^[a-z]:\//i.test(project)) return false;
  const fields: (keyof ReconnectableAgent)[] = ["project", "tfm", "platform", "appName"];
  const leftValues = fields.map((field) =>
    field === "project" ? normalizeProject(left[field]) : normalizeIdentity(left[field]));
  const rightValues = fields.map((field) =>
    field === "project" ? normalizeProject(right[field]) : normalizeIdentity(right[field]));
  if (!(leftValues.every(Boolean) &&
    rightValues.every(Boolean) &&
    leftValues.every((value, index) => value === rightValues[index])))
    return false;

  const leftSession = normalizeIdentity(left.sessionId);
  const rightSession = normalizeIdentity(right.sessionId);
  return !leftSession && !rightSession ||
    !!leftSession && !!rightSession && leftSession === rightSession;
}

interface ReconnectableAgent {
  project?: string | null;
  tfm?: string | null;
  platform?: string | null;
  appName?: string | null;
  sessionId?: string | null;
}

function normalizeIdentity(value: string | null | undefined): string {
  return String(value ?? "").trim().toLowerCase();
}

function normalizeProject(value: string | null | undefined): string {
  const normalized = String(value ?? "").trim().replaceAll("\\", "/");
  return process.platform === "win32" ? normalized.toLowerCase() : normalized;
}

export function createReconnectController<TAgent extends { id: string }>(
  options: ReconnectControllerOptions<TAgent>,
): { poll: (pollOptions?: ReconnectPollOptions) => Promise<void> } {
  let refreshing = false;
  let queued = false;
  let queuedChoose = false;
  let consecutiveMisses = 0;
  let requestedTarget: TAgent | undefined;
  let pendingChoice = false;

  const applyReconnect = (state: ReconnectState): void => {
    if (options.isDisposed()) return;
    if (!pendingChoice && options.isConnected?.() && ++consecutiveMisses < 2) return;
    consecutiveMisses = 0;
    options.applyReconnect(state);
  };

  const applyConnected = (
    discovery: ReconnectDiscovery<TAgent>,
    agent: TAgent,
  ): void => {
    if (options.isDisposed()) return;
    consecutiveMisses = 0;
    requestedTarget = agent;
    pendingChoice = false;
    options.applyConnected(discovery, agent);
  };

  const run = async (choose: boolean): Promise<void> => {
    let discovery = await options.discover();
    if (options.isDisposed()) return;
    if (!discovery) {
      applyReconnect("broker");
      return;
    }

    const target = requestedTarget ?? options.targetAgent();
    if (target && !(choose && discovery.agents.length > 0)) {
      const exact = discovery.agents.find((candidate) => candidate.id === target.id);
      if (exact) {
        applyConnected(discovery, exact);
        return;
      }

      const matches = discovery.agents.filter((candidate) => options.matchesTarget(candidate, target));
      if (matches.length === 1) {
        applyConnected(discovery, matches[0]);
      } else {
        applyReconnect(matches.length > 1 ? "multiple" : "target");
      }
      return;
    }

    const action = choose && discovery.agents.length > 0
      ? "choose"
      : reconnectDiscoveryAction(discovery.agents.length, choose);
    if (action === "connect") {
      applyConnected(discovery, discovery.agents[0]);
      return;
    }
    if (action !== "choose") {
      applyReconnect(discovery.agents.length === 0 ? "app" : "multiple");
      return;
    }

    const selected = await options.pickAgent(discovery.agents);
    if (options.isDisposed()) return;
    if (!selected) {
      applyReconnect("multiple");
      return;
    }

    requestedTarget = selected;
    pendingChoice = true;
    // The picker can remain open while apps or the broker restart. Resolve the selected id against
    // a fresh registry rather than connecting with stale ports from the original picker entries.
    discovery = await options.discover();
    if (options.isDisposed()) return;
    if (!discovery) {
      applyReconnect("broker");
      return;
    }

    const exact = discovery.agents.find((candidate) => candidate.id === selected.id);
    if (exact) {
      applyConnected(discovery, exact);
    } else {
      const matches = discovery.agents.filter((candidate) => options.matchesTarget(candidate, selected));
      if (matches.length === 1) {
        applyConnected(discovery, matches[0]);
      } else {
        applyReconnect(matches.length > 1 ? "multiple" : "target");
      }
    }
  };

  const poll = async (pollOptions: ReconnectPollOptions = {}): Promise<void> => {
    if (options.isDisposed()) return;
    if (refreshing) {
      if (pollOptions.queueIfBusy) {
        queued = true;
        queuedChoose ||= pollOptions.choose === true;
      }
      return;
    }

    refreshing = true;
    try {
      await run(pollOptions.choose === true);
    } catch {
      applyReconnect("broker");
    } finally {
      refreshing = false;
      if (queued && !options.isDisposed()) {
        const choose = queuedChoose;
        queued = false;
        queuedChoose = false;
        await poll({ choose });
      }
    }
  };

  return { poll };
}

export function renderReconnectHost(
  state: ReconnectState,
  nonce: string,
  bridgeId: string,
): string {
  const waitingForBroker = state === "broker";
  const multipleApps = state === "multiple";
  const waitingForTarget = state === "target";
  const heading = waitingForBroker
    ? "Waiting for the DevFlow broker"
    : multipleApps
      ? "Choose a running app"
      : waitingForTarget
        ? "Waiting for the selected MAUI app"
        : "Waiting for a running MAUI app";
  const detail = waitingForBroker
    ? "Start or restart MAUI DevFlow. The Inspector will reconnect automatically."
    : multipleApps
      ? "More than one app is available. Choose the app you want to inspect."
      : waitingForTarget
        ? "The selected process is unavailable. Choose the restarted app explicitly if it reports a relative project path."
        : "Launch your app with the DevFlow agent. The Inspector will reconnect automatically.";

  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <meta http-equiv="Content-Security-Policy"
        content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';" />
  <title>${PRODUCT_NAME}</title>
  <style>
    :root { color-scheme: light dark; }
    * { box-sizing: border-box; }
    html, body { margin: 0; padding: 0; height: 100%; background: var(--vscode-editor-background, #1e1e1e);
                 color: var(--vscode-editor-foreground, #cccccc); font: var(--vscode-font-size, 13px)/1.5 var(--vscode-font-family, "Segoe UI", sans-serif); }
    main { min-height: 100%; display: grid; place-items: center; padding: 32px; }
    section { width: min(480px, 100%); }
    .eyebrow { margin: 0 0 8px; color: var(--vscode-descriptionForeground, #999999); }
    h1 { margin: 0 0 8px; font-size: 20px; font-weight: 600; }
    p { margin: 0 0 20px; color: var(--vscode-descriptionForeground, #999999); }
    .actions { display: flex; flex-wrap: wrap; gap: 8px; }
    button { border: 1px solid var(--vscode-button-border, transparent); border-radius: 2px; padding: 6px 12px;
             background: var(--vscode-button-background, #0e639c); color: var(--vscode-button-foreground, #ffffff);
             font: inherit; cursor: pointer; }
    button:hover { background: var(--vscode-button-hoverBackground, #1177bb); }
    button.secondary { background: var(--vscode-button-secondaryBackground, #3a3d41);
                       color: var(--vscode-button-secondaryForeground, #ffffff); }
    button.secondary:hover { background: var(--vscode-button-secondaryHoverBackground, #45494e); }
    button:focus-visible { outline: 1px solid var(--vscode-focusBorder, #007fd4); outline-offset: 2px; }
  </style>
</head>
<body>
  <main>
    <section aria-live="polite">
      <p class="eyebrow">${PRODUCT_NAME}</p>
      <h1>${heading}</h1>
      <p>${detail}</p>
      <div class="actions">
        ${multipleApps || waitingForTarget ? `<button id="choose" type="button">${waitingForTarget ? "Choose another app" : "Choose app"}</button>` : ""}
        <button id="retry" class="${multipleApps || waitingForTarget ? "secondary" : ""}" type="button">Retry</button>
      </div>
    </section>
  </main>
  <script nonce="${nonce}">
    (function () {
      const vscode = acquireVsCodeApi();
      const bridgeId = ${JSON.stringify(bridgeId)};
      const retry = document.getElementById('retry');
      function poll() {
        retry.disabled = true;
        retry.textContent = 'Checking…';
        setTimeout(function () {
          retry.disabled = false;
          retry.textContent = 'Retry';
        }, 7000);
        vscode.postMessage({ type: 'devflow:reconnectPoll', bridgeId: bridgeId });
      }
      document.getElementById('retry').addEventListener('click', poll);
      const choose = document.getElementById('choose');
      if (choose) choose.addEventListener('click', function () {
        vscode.postMessage({ type: 'devflow:chooseApp', bridgeId: bridgeId });
      });
    })();
  </script>
</body>
</html>`;
}
