const PRODUCT_NAME = "MAUI DevFlow Inspector";

export type ReconnectState = "broker" | "app" | "multiple";
export type ReconnectDiscoveryAction = "wait" | "connect" | "choose";

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

export function renderReconnectHost(state: ReconnectState, nonce: string): string {
  const waitingForBroker = state === "broker";
  const multipleApps = state === "multiple";
  const heading = waitingForBroker
    ? "Waiting for the DevFlow broker"
    : multipleApps
      ? "Choose a running app"
      : "Waiting for a running MAUI app";
  const detail = waitingForBroker
    ? "Start or restart MAUI DevFlow. The Inspector will reconnect automatically."
    : multipleApps
      ? "More than one app is available. Choose the app you want to inspect."
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
    @media (prefers-reduced-motion: reduce) { * { scroll-behavior: auto !important; } }
  </style>
</head>
<body>
  <main>
    <section aria-live="polite">
      <p class="eyebrow">${PRODUCT_NAME}</p>
      <h1>${heading}</h1>
      <p>${detail}</p>
      <div class="actions">
        ${multipleApps ? '<button id="choose" type="button">Choose app</button>' : ""}
        <button id="retry" class="${multipleApps ? "secondary" : ""}" type="button">Retry</button>
      </div>
    </section>
  </main>
  <script nonce="${nonce}">
    (function () {
      const vscode = acquireVsCodeApi();
      function poll() { vscode.postMessage({ type: 'devflow:reconnectPoll' }); }
      document.getElementById('retry').addEventListener('click', poll);
      const choose = document.getElementById('choose');
      if (choose) choose.addEventListener('click', function () {
        vscode.postMessage({ type: 'devflow:chooseApp' });
      });
      setInterval(poll, 2500);
    })();
  </script>
</body>
</html>`;
}
