import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const sourceRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../../..");
const webRoot = resolve(sourceRoot, "Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web");
const read = (file) => readFileSync(resolve(webRoot, file), "utf8");
const readVscodeHost = () => readFileSync(
  resolve(sourceRoot, "DevFlow/js/vscode-inspector/src/extension.ts"),
  "utf8",
);

async function loadWorkbenchStateModule() {
  const source = read("inspector-workbench.js")
    .replace(/^import .*;\r?\n/gm, "");
  const stubs = [
    "const describeHostCapability = () => '';",
    "const renderPlanPanel = () => null;",
    "const renderStepsPanel = () => null;",
    "const renderRunPanel = () => null;",
    "const renderTracePanel = () => null;",
    "const renderRepairPanel = () => null;",
    "const renderImprovePanel = () => null;",
    "const renderSourceProposalPanel = () => null;",
  ].join("\n");
  return import(`data:text/javascript;base64,${Buffer.from(`${stubs}\n${source}`).toString("base64")}`);
}
async function loadPanelModule(file) {
  return import(`data:text/javascript;base64,${Buffer.from(read(file)).toString("base64")}`);
}

test("workbench shell separates the guided journey from contextual test tools", () => {
  const html = read("inspector.html");
  const css = read("devflow.css");
  const workbenchCss = read("inspector-workbench.css");

  assert.match(html, /id="df-toggle-workbench"[^>]*>.*?Tests/s);
  assert.match(html, /aria-label="Tests"/);
  assert.match(html, /class="df-tool-btn df-destination df-workbench-entry"/);
  assert.match(html, /id="df-workbench-tabs"[^>]*class="df-workbench-tabs"/);
  assert.match(html, /class="df-panel-tab-list df-workbench-stage-list"[^>]*role="tablist"[^>]*aria-label="Test workflow"/);
  assert.match(html, /id="df-workbench-advanced-tools"[^>]*class="df-workbench-tools"/);
  assert.match(html, /class="df-panel-tab-list df-workbench-tool-list"[^>]*role="tablist"[^>]*aria-label="Test tools"/);
  for (const stage of ["Goal", "Steps", "Review", "Run", "Results &amp; import"]) {
    assert.match(html, new RegExp(`data-workbench-stage="[^"]+"[^>]*[\\s\\S]*?>${stage}<`));
  }
  for (const tab of ["requests", "repair", "improve", "source"]) {
    assert.match(html, new RegExp(`data-workbench-tab="${tab}"[^>]*hidden`));
  }
  assert.match(html, /id="df-workbench-advanced-tools"[^>]*hidden/);
  assert.match(html, /Review agent requests/);
  assert.match(html, /both this broker and embedding host advertise trusted native approval/);
  assert.match(html, /id="df-test-agent-request-badge"/);
  for (const tab of ["record", "review", "run", "results"]) {
    assert.match(html, new RegExp(`id="df-workbench-stage-${tab}"[^>]*disabled`));
  }
  assert.doesNotMatch(html, /id="df-import-result"/);
  assert.doesNotMatch(html, /id="df-load-flow"/);
  assert.doesNotMatch(html, /id="df-toggle-replay"/);
  assert.doesNotMatch(html, /id="df-resume-(save|restore|clear)"/);
  for (const tab of ["requests", "repair", "improve", "source"]) {
    assert.match(html, new RegExp(`id="df-workbench-tab-${tab}"[^>]*disabled`));
  }
  assert.match(html, /class="df-workbench-journey"/);
  assert.doesNotMatch(html, /<summary>Advanced tools<\/summary>/);
  assert.equal((html.match(/id="df-workbench-panel-[^"]+"[^>]*role="tabpanel"/g) || []).length, 8);
  assert.match(html, /id="df-agent-requests"[^>]*role="tabpanel"/);
  assert.match(css, /#df-dock-tabs,\s*\.df-panel-tabs/);
  assert.match(css, /\.df-dock-tab-list,\s*\.df-panel-tab-list/);
  assert.match(workbenchCss, /\.df-review-step-list[\s\S]*position: sticky/);
  assert.match(workbenchCss, /\.df-review-step-summary[\s\S]*opacity: \.9/);
  assert.match(html, /id="df-timeline"/);
  assert.match(html, /id="df-workbench-alert"[^>]*role="alert"/);
});

test("workbench state preserves hints and safety transitions without invoking a host", async () => {
  const workbench = await loadWorkbenchStateModule();
  let state = workbench.createInitialWorkbenchState("?test=flows%2Flogin.md&trace=run.json&agentRequest=approval_123");
  assert.equal(state.startupHints.test, "flows/login.md");
  assert.equal(state.startupHints.trace, "run.json");
  assert.equal(state.startupHints.agentRequest, "approval_123");
  assert.equal(state.selectedStage, "goal");
  state = workbench.normalizeWorkbenchState(state, {
    selectedTab: "repair",
    run: "unknown-completion",
    repair: "verification-failed",
  });
  assert.equal(state.selectedTab, "repair");
  assert.equal(state.selectedStage, "goal");
  assert.match(workbench.workbenchSafetyMessage(state), /Unknown or orphaned completion/);
  state = workbench.normalizeWorkbenchState(state, { run: "failed", repair: "rollback-required" });
  assert.match(workbench.workbenchSafetyMessage(state), /Rollback requires explicit human handling/);
});

test("guided stage tabs preserve Review as a distinct destination", async () => {
  const workbench = await loadWorkbenchStateModule();
  let state = workbench.createInitialWorkbenchState();
  state = workbench.normalizeWorkbenchState(state, { selectedTab: "review" });
  assert.equal(state.selectedTab, "review");
  assert.equal(state.selectedStage, "review");
  state = workbench.normalizeWorkbenchState(state, { run: "preflight" });
  assert.equal(state.selectedStage, "review");
  assert.match(read("inspector-workbench.js"), /STAGE_TABS/);
  assert.match(read("inspector-workbench.js"), /renderJourney/);
  assert.match(read("inspector-workbench.js"), /const reviewed = saved && readiness\.hardOutcomeCheck === true/);
  assert.match(read("inspector-workbench.js"), /function toolAvailability/);
  assert.match(read("inspector-workbench.js"), /Math\.max\(Number\(draft\.recordingSteps\) \|\| 0, visibleSteps\)/);
  assert.match(read("devflow.js"), /timelineAdd\(Number\(j\.seq\) \|\| j\.stepCount, action, el, extra\)/);
  assert.match(
    read("inspector-workbench.js"),
    /const enabled = enabledWorkbenchTabs\(tabs, tabList\)/,
  );
  assert.match(read("inspector-workbench.js"), /scrollIntoView/);
});

test("workbench state model includes safety states and confirmation-only shortcuts", () => {
  const workbench = read("inspector-workbench.js");

  for (const state of [
    "unknown-completion", "orphaned", "approval-expired", "awaiting-host-apply",
    "verification-failed", "rollback-required", "rollback-failed",
  ]) {
    assert.match(workbench, new RegExp(`['"]${state}['"]`));
  }
  assert.match(workbench, /Run check opened\. Review and explicitly approve before starting\./);
  assert.match(workbench, /Cancellation confirmation opened\. No run was changed yet\./);
  assert.match(workbench, /isEditableTarget/);
});

test("repair panel exposes human-only selector repair boundaries", () => {
  const repair = read("inspector-repair.js");

  for (const text of [
    "Nothing to repair yet", "Check latest failure", "Create suggested update",
    "Review suggested update", "Try this update", "Approve update",
    "Diagnose with your agent",
    "How this stays safe",
  ]) {
    assert.match(repair, new RegExp(text));
  }
  assert.match(repair, /one selector only; actions, checks, values, order, and source stay unchanged/i);
  assert.match(repair, /only a current local missing-selector failure can qualify/i);
  assert.match(repair, /agent-originated suggestions are never applied directly/i);
  assert.match(repair, /trusted native host confirmation/i);
  assert.match(repair, /browser does not receive the grant/i);
});

test("failed-result agent guidance prepares one exact restricted handoff", () => {
  const trace = read("inspector-trace.js");
  const repair = read("inspector-repair.js");
  const source = read("devflow.js");
  const shell = read("inspector-workbench.js");

  assert.match(source, /\/api\/workbench\/agent-handoff/);
  assert.match(source, /Call maui_test_failure exactly once/);
  assert.match(source, /Do not call maui_test_author begin, status, abandon, or migrate-preview/);
  assert.match(source, /Treat omitted checkpoint or route facts as unknown, never as a mismatch/);
  assert.match(source, /selectorRepair\.status is exactly "eligible"/);
  assert.doesNotMatch(trace, /latest failed local DevFlow test/i);
  assert.doesNotMatch(repair, /latest failed local DevFlow test/i);
  assert.match(trace, /helpers\.run\?\.prepareFailureAgentPrompt/);
  assert.match(repair, /prepareFailureAgentPrompt/);
  assert.match(shell, /typeof prompt === 'function' \? await prompt\(\)/);
  assert.match(shell, /Preparing agent request/);
  assert.match(source, /agent: inspectorAgent/);
  assert.match(source, /agentId: inspectorAgent\.id/);
  assert.match(source, /agentInstanceId: inspectorAgent\.instanceId/);
});

test("source proposal panel keeps XAML/C# review and flow repair separate", () => {
  const source = read("inspector-source.js");

  for (const text of [
    "Select a control first", "Check source", "Create source proposal",
    "Preview exact change",
    "Download patch", "How this stays safe",
  ]) {
    assert.match(source, new RegExp(text));
  }
  assert.match(source, /Exact \$\{languageLabel\} diff/);
  // Source apply is deferred in this layer: the panel reviews, downloads and rejects only.
  assert.match(source, /Proposals are inert here: nothing on this page writes source/i);
  assert.match(source, /Only a trusted native host capability can approve or narrow a proposal/i);
  assert.doesNotMatch(source, /Approve source change/);
  assert.doesNotMatch(source, /Rollback source change/);
  assert.doesNotMatch(source, /canApplySource|canApplyCSharpSource/);
  // The proposal carries no verification block in this layer; reading one back — by dot, optional
  // chain or index — would be dead code pretending a removed contract still arrives.
  assert.doesNotMatch(source, /(?:\??\.|\[['"])verification/i);
  assert.match(read("devflow.js"), /hasSource: info\.hasSource/);
  assert.match(read("devflow.js"), /\/api\/workbench\/source\/csharp/);
  assert.doesNotMatch(read("devflow.js"), /applySourceProposal|applyCSharpSourceProposal|getCSharpSourceSelection/);
});

test("workbench assets are embedded, routed, and responsive", () => {
  const server = read("../InspectorServer.Routes.cs");
  const css = read("inspector-workbench.css");

  for (const asset of [
    "inspector-host-bridge.js", "inspector-agent-requests.js", "inspector-workbench.js", "inspector-plan.js",
    "inspector-steps.js", "inspector-run.js", "inspector-trace.js",
    "inspector-repair.js", "inspector-improve.js", "inspector-source.js", "inspector-study.js", "inspector-workbench.css",
  ]) {
    assert.match(server, new RegExp(`/${asset.replace(".", "\\.")}`));
  }
  assert.match(css, /clamp\(300px, var\(--df-workbench-height, 46vh\)/);
  // Layout is keyed on geometry, split into independent width and height axes.
  assert.match(css, /data-layout-width="compact"/);
  assert.match(css, /data-layout-width="narrow"/);
  assert.match(css, /data-layout-height="short"/);
  assert.doesNotMatch(css, /data-host-layout/);
  assert.match(css, /forced-colors: active/);
  assert.match(css, /\.df-agent-action/);
});

test("preview capabilities hide immature surfaces but reveal already-pending safe review", () => {
  const devflow = read("devflow.js");
  const requests = read("inspector-agent-requests.js");

  assert.match(devflow, /devflow-preview-workbench/);
  assert.match(devflow, /devflow-preview-agent-authoring/);
  assert.match(devflow, /devflow-preview-repair/);
  assert.match(devflow, /devflow-preview-source/);
  assert.match(devflow, /workbenchToggle\.hidden = !previewFeatures\.workbench && !previewFeatures\.agentAuthoring/);
  assert.match(devflow, /repairTab\.hidden = !previewFeatures\.repair/);
  assert.match(devflow, /sourceTab\.hidden = !previewFeatures\.source/);
  assert.match(devflow, /featureCapabilities: previewFeatures/);
  assert.match(devflow, /baseWorkbenchAvailable: previewFeatures\.workbench \|\| previewFeatures\.agentAuthoring/);
  assert.match(requests, /workbenchToggle\.hidden = !baseWorkbenchAvailable && !available/);
  assert.doesNotMatch(devflow, /df-workbench-tab-requests['"]\)\.hidden/);
});

test("the Tests entry point matches the preview surface the broker actually enabled", async () => {
  const devflow = read("devflow.js");
  const requests = read("inspector-agent-requests.js");

  // Preview off: nothing generic is offered, so the Tests toggle stays hidden until an agent
  // request makes it useful. With agent authoring alone the toggle is available, but every guided
  // stage is hidden, so the panel opens on Agent requests rather than an empty Tests shell.
  const toggleHidden = (workbench, agentAuthoring) => !workbench && !agentAuthoring;
  assert.equal(toggleHidden(false, false), true);
  assert.equal(toggleHidden(false, true), false);
  assert.equal(toggleHidden(true, false), false);

  assert.match(
    devflow,
    /if \(!previewFeatures\.workbench\) \{[\s\S]{0,400}df-workbench-stage-goal[\s\S]{0,400}element\.hidden = true;/);
  assert.match(devflow, /openPanel: \(\) => testWorkbench\?\.open\?\.\('requests', false\)/);
  // "requests" must stay reachable even when no guided tool is enabled, or an approval could be
  // pending with no way to reach it.
  const workbench = read("inspector-workbench.js");
  assert.match(
    workbench,
    /WORKBENCH_TABS\.includes\(tab\) && \(tab === 'requests' \|\| toolAvailability\(tab\)\.enabled\)/);
  assert.match(requests, /baseWorkbenchAvailable/);
});

test("an authoring-only Inspector lands on Agent requests instead of a hidden guided stage", () => {
  const devflow = read("devflow.js");
  const requests = read("inspector-agent-requests.js");
  const source = read("inspector-workbench.js");

  // The stored default belongs to the guided journey...
  assert.match(source, /selectedTab: 'plan'/);
  // ...so the controller reroutes it when that journey is not a capability of this broker.
  assert.match(
    source,
    /if \(!featureCapabilities\.workbench && !state\.startupHints\.agentRequest\) \{[\s\S]{0,300}selectedTab: 'requests'/);
  // Every guided tab is refused, not merely hidden, so no panel can be rendered behind a hidden tab.
  assert.match(source, /const GUIDED_TABS = Object\.freeze\(new Set\(\[\.\.\.Object\.values\(STAGE_TABS\), 'improve'\]\)\)/);
  assert.match(
    source,
    /if \(!featureEnabled\('workbench'\) && GUIDED_TABS\.has\(tab\)\) \{[\s\S]{0,200}enabled: false/);
  // Agent requests stays selectable while empty in that mode, and its tab stays visible.
  assert.match(source, /\|\| !featureEnabled\('workbench'\)/);
  assert.match(source, /workbench: options\.featureCapabilities\?\.workbench === true/);
  assert.match(requests, /tab\.hidden = !available && !requestsArePrimary/);
  assert.match(devflow, /requestsArePrimary: !previewFeatures\.workbench && previewFeatures\.agentAuthoring/);
  assert.match(devflow, /workbench: metaFlag\('devflow-preview-workbench'\)/);
});

test("an unserved layout surface has no entry point that can strand the Data dock", () => {
  const devflow = read("devflow.js");
  const properties = read("inspector-properties.js");

  assert.match(properties, /layoutDiagnosticsSupported = true/);
  assert.match(
    properties,
    /function createDiagnostics\(elementId\) \{[\s\S]{0,400}if \(!layoutDiagnosticsSupported\) return null;/);
  assert.match(devflow, /layoutDiagnosticsSupported: inspectorSurfaces\.layoutDiagnostics/);
  assert.match(
    devflow,
    /function openLayoutDiagnostics\(options = \{\}\) \{[\s\S]{0,200}if \(!inspectorSurfaces\.layoutDiagnostics\) return;/);
  assert.match(
    devflow,
    /async function loadTab\(name, options = \{\}\) \{[\s\S]{0,300}if \(name === 'layout' && !inspectorSurfaces\.layoutDiagnostics\) return;/);
  assert.match(
    devflow,
    /async function runLayoutScan\(\) \{\s*if \(!inspectorSurfaces\.layoutDiagnostics\) return;/);
});

test("agent request scope comparison rejects expansion while preview remains read-only", async () => {
  const approvals = await loadPanelModule("inspector-agent-requests.js");
  const requested = {
    allowedActions: ["fill", "tap"],
    allowedSelectors: ["automationId:NewTodoEntry", "automationId:AddButton"],
    allowedSideEffectClasses: ["ui"],
    maxActionCount: 2,
    maxValueBytes: 64,
  };

  assert.equal(approvals.isNarrowedAgentRequestScope(requested, {
    allowedActions: ["tap"],
    allowedSelectors: ["automationId:AddButton"],
    allowedSideEffectClasses: ["ui"],
    maxActionCount: 1,
    maxValueBytes: 0,
  }), true);
  assert.equal(approvals.isNarrowedAgentRequestScope(requested, {
    allowedActions: ["tap", "run"],
    allowedSelectors: ["automationId:AddButton"],
    allowedSideEffectClasses: ["ui"],
    maxActionCount: 2,
    maxValueBytes: 64,
  }), false);
  assert.equal(approvals.isNarrowedAgentRequestScope(requested, {
    allowedActions: ["tap"],
    allowedSelectors: ["automationId:AddButton"],
    allowedSideEffectClasses: ["ui"],
    maxActionCount: 3,
    maxValueBytes: 64,
  }), false);
  assert.match(approvals.agentRequestSummary({ requestedScope: requested }), /2 action types.*2 exact selectors.*up to 2 actions/);
  assert.match(
    approvals.agentRequestSummary({
      requestedScope: requested,
      deviceEffects: ["Battery level -> 5%", "Device tap after app step 1: native id \"allow\" at (10, 20)"],
    }),
    /exact device changes: Battery level -> 5%.*native id "allow"/,
  );
  assert.equal(approvals.agentRequestGrantDurationSeconds({ kind: "commit" }), 600);
  assert.equal(approvals.agentRequestGrantDurationSeconds({ kind: "run" }), 300);
  assert.match(
    approvals.agentRequestStarterPrompt("MauiTodo", "WinUI", "agent-42", "instance-99"),
    /restricted DevFlow test-agent tools.*MauiTodo on WinUI.*commit review.*separate run request/i
  );
  assert.match(
    approvals.agentRequestStarterPrompt("MauiTodo", "WinUI", "agent-42", "instance-99"),
    /agentId "agent-42".*agentInstanceId "instance-99".*Chat approval.*current Test Workbench broker grant/is
  );

  const source = read("inspector-agent-requests.js");
  assert.match(source, /humanConfirmed: true/);
  assert.match(source, /\/api\/workbench\/agent-requests/);
  assert.match(source, /nativeApprovalAvailable/);
  assert.match(source, /Approve in native host/);
  assert.match(source, /hostBridge\.request\('nativeApproval'/);
  assert.match(source, /renderScope\(request, true, expandByDefault\)/);
  assert.match(source, /type = 'checkbox'/);
  assert.match(source, /type = 'number'/);
  assert.doesNotMatch(source, /Your agent can continue/i);
  assert.match(source, /approvedScope/);
  assert.doesNotMatch(source, /agent-requests\/\$\{encodeURIComponent\(id\)\}\/approve/);
  assert.match(source, /scrollIntoView/);
  assert.match(source, /openPanel/);
  assert.match(source, /df-workbench-tab-requests/);
  assert.doesNotMatch(source, /Back to tests|df-agent-requests-open|setOpen\(|hasNewPending|seenPending/);
  assert.doesNotMatch(source, /Allow one run/);
  assert.match(source, /Your agent prepared a test/);
  assert.match(source, /Copy prompt for your agent/);
  assert.doesNotMatch(source, /approvalGrantId/);
});

test("agent-request polling updates badges without replacing a focused review", () => {
  const requests = read("inspector-agent-requests.js");

  assert.match(requests, /let needsRender = false/);
  assert.match(requests, /const panelFocused = panel\.contains\?\.\(doc\.activeElement\)/);
  assert.match(requests, /if \(!force && panelFocused\) \{[\s\S]*needsRender = true;[\s\S]*syncChrome\(\);[\s\S]*return;/);
  assert.match(requests, /nextFingerprint === responseFingerprint && !needsRender/);
  assert.match(requests, /render\(\);\s+needsRender = false;/);
});

test("focused agent-request review receives pending counts without a destructive rerender", async () => {
  const approvals = await loadPanelModule("inspector-agent-requests.js");
  const node = () => ({
    children: [],
    attributes: new Map(),
    classList: { contains() { return false; }, toggle() {} },
    dataset: {},
    append(...children) { this.children.push(...children); },
    addEventListener() {},
    setAttribute(name, value) { this.attributes.set(name, String(value)); },
    removeAttribute(name) { this.attributes.delete(name); },
  });
  const body = {
    ...node(),
    replaceCount: 0,
    replaceChildren(...children) {
      this.replaceCount += 1;
      this.children = children;
    },
  };
  const focusedReviewControl = {};
  const panel = { contains: (candidate) => candidate === focusedReviewControl };
  const tab = node();
  const toolbarBadge = node();
  const tabBadge = node();
  const documentLike = {
    activeElement: null,
    createElement() { return node(); },
    getElementById(id) {
      return {
        "df-workbench-tab-requests": tab,
        "df-test-agent-request-badge": toolbarBadge,
        "df-agent-requests-badge": tabBadge,
      }[id] || null;
    },
  };
  const responses = [
    { ok: true, body: { ok: true, requests: [] } },
    {
      ok: true,
      body: {
        ok: true,
        requests: [{
          approvalRequestId: "approval-one",
          state: "pending",
          kind: "commit",
          requestedScope: { allowedActions: ["tap"] },
        }],
      },
    },
  ];
  const inbox = approvals.createAgentRequestController({
    document: documentLike,
    window: { setInterval() {}, clearInterval() {} },
    inspectorApi: { getDetailed: async () => responses.shift() },
    panel,
    body,
    tab,
    toolbarBadge,
    tabBadge,
  });

  await inbox.refresh(true);
  const rendersBeforeFocusedPoll = body.replaceCount;
  documentLike.activeElement = focusedReviewControl;
  await inbox.refresh();

  assert.equal(body.replaceCount, rendersBeforeFocusedPoll);
  assert.equal(inbox.pendingCount(), 1);
  assert.equal(toolbarBadge.textContent, "1");
  assert.equal(tabBadge.textContent, "1");
});

test("host bridge registry covers every negotiated capability", () => {
  const bridge = read("inspector-host-bridge.js");

  for (const capability of [
    "saveTestBundle", "loadTestBundle", "pickTrace", "requestTestProposal", "openSourceDiff",
    // The formerly separate legacy vocabulary now lives in the same registry.
    "selection", "copilot", "copilotContext", "attachData", "openSource", "saveRecording",
    "workflowFilePicker",
    // This layer serves the layout suppression policy bridge.
    "layoutPolicyMutation",
  ]) {
    assert.match(bridge, new RegExp(`\\b${capability}: Object\\.freeze\\(`));
  }
  // A capability no host implements must not survive as user-facing copy.
  assert.doesNotMatch(bridge, /attachTestContext/);
  // Source apply belongs to a later layer. Advertising it here would offer the page an authority
  // no host in this layer can honour.
  assert.doesNotMatch(bridge, /applySourceProposal|applyCSharpSourceProposal|getCSharpSourceSelection/);
  assert.match(bridge, /capability-missing/);
  assert.match(bridge, /testProposalApprovalResult/);
  assert.match(bridge, /grantId/);
});

test("agent assistance is a direct host-aware button with browser copy fallback", () => {
  const workbench = read("inspector-workbench.js");

  assert.match(workbench, /df-workbench-action df-agent-action/);
  assert.match(workbench, /bridge\?\.has\?\.\('requestTestProposal'\)/);
  assert.match(workbench, /bridge\.request\('requestTestProposal'/);
  assert.match(workbench, /Copied the agent request/);
  assert.doesNotMatch(workbench, /df-agent-guide/);
});

test("agent requests dispatch directly when supported and copy safely otherwise", async () => {
  const { bindAgentPrompt, deliverAgentRequest } = await loadWorkbenchStateModule();
  const sent = [];
  let copied = "";
  const bridge = {
    has: (capability) => capability === "requestTestProposal",
    request: async (capability, payload) => {
      sent.push({ capability, payload });
      return { ok: true, message: "Sent to Copilot." };
    },
  };

  const direct = await deliverAgentRequest({
    bridge,
    prompt: " Prepare the test ",
    label: "Create this test with your agent",
    copyText: async (value) => { copied = value; return true; },
  });
  assert.equal(direct.delivery, "agent");
  assert.equal(direct.buttonLabel, "Sent to agent");
  assert.equal(copied, "");
  assert.equal(sent[0].capability, "requestTestProposal");
  assert.equal(sent[0].payload.prompt, "Prepare the test");

  bridge.request = async () => ({ ok: false, error: "Host session closed." });
  const fallback = await deliverAgentRequest({
    bridge,
    prompt: "Diagnose the run",
    copyText: async (value) => { copied = value; return true; },
  });
  assert.equal(fallback.delivery, "clipboard");
  assert.equal(copied, "Diagnose the run");
  assert.match(fallback.status, /Host session closed/);

  const hostClipboard = await deliverAgentRequest({
    bridge: {
      has: () => true,
      request: async () => ({
        ok: true,
        message: "Copied by VS Code.",
        value: { delivery: "clipboard" },
      }),
    },
    prompt: "Improve the test",
    copyText: async () => { throw new Error("must not copy twice"); },
  });
  assert.equal(hostClipboard.delivery, "clipboard");
  assert.equal(hostClipboard.buttonLabel, "Prompt copied");

  const bound = bindAgentPrompt("Prepare the test", {
    id: "agent-exact",
    instanceId: "instance-exact",
  });
  assert.match(bound, /agentId "agent-exact"/);
  assert.match(bound, /agentInstanceId "instance-exact"/);
  assert.match(bound, /Chat approval or affirmative text expresses intent only/);
  assert.match(bound, /current Test Workbench broker grant is required separately for each commit or run/);
});

test("disabled Source capability blocks keyboard and programmatic navigation without stealing focus", () => {
  const workbench = read("inspector-workbench.js");
  const improve = read("inspector-improve.js");
  const devflow = read("devflow.js");

  assert.match(workbench, /if \(!featureEnabled\('source'\)\)/);
  assert.match(workbench, /Source preview is disabled in this Inspector/);
  assert.match(workbench, /const retainedFocus = doc\.activeElement/);
  assert.match(workbench, /retainWorkbenchFocus\(retainedFocus/);
  assert.match(workbench, /if \(!availability\.enabled\)/);
  assert.match(workbench, /!button\.hidden/);
  assert.match(improve, /Ask agent about testability/);
  assert.match(improve, /Ask agent about source anchor/);
  assert.match(devflow, /testWorkbench\?\.featureEnabled\?\.\('source'\) !== true/);
  const improveStart = devflow.indexOf("improveTestability(match)");
  const capabilityCheck = devflow.indexOf("featureEnabled?.('source') !== true", improveStart);
  const selection = devflow.indexOf("selectElement(candidate.id);", improveStart);
  assert.ok(
    improveStart >= 0 && capabilityCheck > improveStart && selection > capabilityCheck,
    "disabled Source must be checked before changing selection or focus",
  );
});

test("repair kill switch removes repair actions and blocks every Inspector repair entry point", () => {
  const workbench = read("inspector-workbench.js");
  const trace = read("inspector-trace.js");
  const devflow = read("devflow.js");

  assert.match(workbench, /repair: options\.featureCapabilities\?\.repair === true/);
  assert.match(workbench, /if \(!featureEnabled\('repair'\)\)/);
  assert.match(workbench, /Selector repair is disabled in this Inspector/);
  assert.match(trace, /repairEligible && helpers\.featureEnabled\?\.\('repair'\)/);
  assert.match(devflow, /function repairDisabled\(\)/);
  for (const action of ["classify", "propose", "preview", "refresh", "reject", "requestApproval", "validate", "apply"]) {
    assert.match(devflow, new RegExp(`async ${action}\\(\\) \\{\\s+if \\(repairDisabled\\(\\)\\) return;`));
  }
});

test("data overflow keeps a focused command visible and trace pickers clean up cancellation", () => {
  const dataUi = read("inspector-data-ui.js");
  const devflow = read("devflow.js");

  assert.match(dataUi, /const focused = host\.contains\(documentLike\.activeElement\)/);
  assert.match(dataUi, /if \(focused && moreMenu\.contains\(focused\)\)/);
  assert.match(dataUi, /more\.open = true/);
  assert.match(dataUi, /focused\.focus\(\{ preventScroll: true \}\)/);
  assert.match(devflow, /input\.tabIndex = -1/);
  assert.match(devflow, /input\.setAttribute\('aria-hidden', 'true'\)/);
  assert.match(devflow, /input\.addEventListener\('cancel', \(\) => finish\(null, true\)/);
  assert.match(devflow, /window\.addEventListener\('focus', onWindowFocus, \{ once: true \}\)/);
  assert.match(devflow, /if \(!input\.files\?\.length\) finish\(null, true\);\s*\}, 500\)/);
  assert.match(devflow, /if \(!kind \|\| file\.size > maximumBytes\)/);
  assert.ok(
    devflow.indexOf("if (!kind || file.size > maximumBytes)") <
      devflow.indexOf("new Uint8Array(await file.arrayBuffer())"),
    "artifact size must be checked before browser bytes are read",
  );
  assert.match(devflow, /picked = await browserPickTrace\(pickerReturnFocus\)/);
});

test("stale authoring responses preserve the unsaved local flow and plan", () => {
  const devflow = read("devflow.js");

  assert.match(devflow, /const preserveLocalDraft = response\.stale === true && response\.ok !== true/);
  assert.match(devflow, /if \(response\.flow && !preserveLocalDraft\)/);
  assert.match(devflow, /if \(response\.plan && !preserveLocalDraft\)/);
});

test("review selection keys are stable with persisted ids and unique for legacy collisions", () => {
  const steps = read("inspector-steps.js");

  assert.match(steps, /if \(step\?\.stepId\) return `stepId:\$\{step\.stepId\}`/);
  assert.match(steps, /return Number\.isFinite\(seq\) \? `seq:\$\{seq\}:\$\{index\}` : `legacy:\$\{index\}`/);
});

test("keyboard navigation excludes hidden Source and blocked navigation restores the visible control", async () => {
  const { enabledWorkbenchTabs, retainWorkbenchFocus } = await loadWorkbenchStateModule();
  const tabList = {};
  const visible = {
    disabled: false,
    hidden: false,
    closest: () => tabList,
  };
  const hiddenSource = {
    disabled: false,
    hidden: true,
    closest: () => tabList,
  };
  const otherList = {
    disabled: false,
    hidden: false,
    closest: () => ({}),
  };
  assert.deepEqual(enabledWorkbenchTabs([visible, hiddenSource, otherList], tabList), [visible]);

  let focused = 0;
  const control = {
    isConnected: true,
    getClientRects: () => [{}],
    focus: ({ preventScroll }) => {
      assert.equal(preventScroll, true);
      focused += 1;
    },
  };
  assert.equal(retainWorkbenchFocus(control, (callback) => callback()), true);
  assert.equal(focused, 1);
  assert.equal(retainWorkbenchFocus({ ...control, getClientRects: () => [] }), false);
});

test("disabled trace import removes file disclosure and blocks pickers before selection", async () => {
  const trace = read("inspector-trace.js");
  const devflow = read("devflow.js");
  const traceModule = await loadPanelModule("inspector-trace.js");

  assert.equal(traceModule.shouldShowTraceImport({ importEnabled: false }), false);
  assert.equal(traceModule.shouldShowTraceImport({ importEnabled: true }), true);
  assert.equal(traceModule.shouldShowTraceImport({ importEnabled: true, run: {} }), false);
  assert.match(trace, /if \(shouldShowTraceImport\(state\)\)/);
  assert.match(devflow, /state: \(\) => \(\{ \.\.\.state, importEnabled: previewFeatures\.traceImport \}\)/);
  const guard = devflow.indexOf("if (!previewFeatures.traceImport) {");
  const hostPicker = devflow.indexOf("if (hostBridge.has('pickTrace'))");
  const browserPicker = devflow.indexOf("picked = await browserPickTrace(pickerReturnFocus)");
  assert.ok(guard >= 0 && guard < hostPicker && guard < browserPicker);
  assert.match(devflow, /No file picker was opened/);
});

test("run preflight summaries retain action classes but never fill values", async () => {
  const run = await loadPanelModule("inspector-run.js");
  const summary = run.summarizePlannedEffects({
    steps: [
      { action: "tap", args: { selector: { automationId: "save" } } },
      { action: "fill", args: { selector: { automationId: "password" }, value: "CorrectHorseBatteryStaple", secretRef: "test-login" } },
      { action: "navigate", args: { route: "/private/orders" } },
      { action: "setProperty", args: { name: "Text", value: "hidden" } },
    ],
  }).join("\n");

  assert.match(summary, /tap/);
  assert.match(summary, /fill/);
  assert.match(summary, /navigation/);
  assert.match(summary, /property change/);
  assert.match(summary, /secret reference: test-login/);
  assert.doesNotMatch(summary, /CorrectHorseBatteryStaple|\/private\/orders|hidden/);
  assert.match(
    run.summarizePlannedEffects({ deviceEffects: ["Battery level -> 5%"] }).join("\n"),
    /device: Battery level -> 5%/,
  );
  assert.equal(run.runStateIsTerminal("unknown-completion"), true);
  assert.equal(run.runStateIsTerminal("running"), false);

  const limited = run.runReadinessIssues({
    preflight: {
      admission: {
        reasons: [{
          code: "independent-oracle-absent",
          message: "No required independent business oracle is declared.",
          blocking: false,
        }],
      },
    },
  });
  assert.deepEqual(limited.blockers, []);
  assert.match(limited.notes[0], /can run.*not be marked independently verified/i);

  const stale = run.runReadinessIssues({
    preflight: { ok: true, admission: { reasons: [] } },
    stalePlan: true,
  });
  assert.match(stale.blockers[0], /plan no longer matches/i);
});

test("trace rendering selects divergence and uses disclosure envelopes only", async () => {
  const trace = await loadPanelModule("inspector-trace.js");
  const report = {
    divergenceStepId: "step-2",
    steps: [
      { stepId: "step-1", action: "tap" },
      { stepId: "step-2", action: "fill", failureClass: "assertion-failed" },
    ],
  };
  assert.equal(trace.firstDivergenceStepId(report), "step-2");
  const disclosure = trace.disclosureText({
    state: "redacted",
    type: "string",
    length: 24,
    digest: "sha256:abc",
    value: "CorrectHorseBatteryStaple",
  });
  assert.match(disclosure, /redacted.*length 24.*sha256:abc/);
  assert.doesNotMatch(disclosure, /CorrectHorseBatteryStaple/);

  assert.deepEqual(trace.passPresentation({
    businessOracles: [{ independent: true, succeeded: true }],
    replayEligibility: { runVerificationAllowed: true },
    verification: { verified: true },
  }), {
    title: "Test passed (independently verified)",
    classification: "Independently verified pass",
  });
  assert.deepEqual(trace.passPresentation({
    businessOracles: [{ independent: false, succeeded: true }],
    replayEligibility: { runVerificationAllowed: true },
    verification: { verified: true },
  }), {
    title: "Test passed (replay only)",
    classification: "Replay pass",
  });

  const untrusted = trace.importedTrustPresentation({ verification: { state: "untrusted" } });
  const attested = trace.importedTrustPresentation({ verification: { state: "attested" } });
  assert.match(untrusted.explanation, /cannot execute/i);
  assert.match(attested.explanation, /diagnostic-only/i);
});

test("terminal locator ambiguity routes to guided resolution instead of repair or a guess", async () => {
  const trace = await loadPanelModule("inspector-trace.js");
  const report = {
    outcome: { status: "failed", terminal: true },
    failure: { class: "locator-ambiguous", stepId: "step-3" },
    steps: [{
      stepId: "step-3",
      sequence: 3,
      failureClass: "locator-ambiguous",
      candidateCount: 4,
      targetResolution: { matchCount: 4 },
    }],
  };

  assert.equal(trace.isAmbiguousLocatorFailure(report), true);
  assert.equal(trace.ambiguityMatchCount(report), 4);
  assert.equal(trace.isAmbiguousLocatorFailure({
    failure: { legacyKind: "ambiguous", stepId: "step-1" },
    steps: [{ stepId: "step-1", candidateCount: 2 }],
  }), true);
  assert.equal(trace.isAmbiguousLocatorFailure({
    failure: { class: "locator-not-found" },
  }), false);
});

test("run and trace use broker adapters and preserve imported read-only boundaries", () => {
  const devflow = read("devflow.js");
  const run = read("inspector-run.js");
  const trace = read("inspector-trace.js");

  for (const route of [
    "/api/workbench/run/preflight",
    "/api/workbench/run/start",
    "/api/workbench/run/journal",
    "/api/workbench/artifacts/import",
  ]) {
    assert.match(devflow, new RegExp(route.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")));
  }
  assert.match(devflow, /setCapturedTraceMode/);
  assert.match(devflow, /state\.mode = 'reproduction'/);
  assert.match(devflow, /Captured result mode is read-only/);
  assert.match(read("inspector-workbench.js"), /event\.key === '\['/);
  assert.match(read("inspector-workbench.js"), /event\.key === '\]'/);
  assert.doesNotMatch(run, /\/api\/flows\/replay/);
  assert.doesNotMatch(trace, /\/api\/flows\/replay/);
  assert.match(trace, /Reproduce locally/);
  assert.match(trace, /Download linked \.mauitrace v1/);
});

test("human plan draft includes required test-plan-v1 authoring fields", async () => {
  const plan = await loadPanelModule("inspector-plan.js");
  const draft = plan.createPlanDraft("login.md", "a".repeat(64));

  for (const field of [
    "schema", "planId", "revision", "flow", "goal", "scenarios", "preconditions",
    "reset", "acceptanceCriteria", "sideEffectPolicy", "provenance",
  ]) {
    assert.ok(Object.hasOwn(draft, field), `missing ${field}`);
  }
  assert.equal(draft.flow.path, "login.md");
  assert.equal(draft.flow.digest, "a".repeat(64));
  assert.equal(draft.provenance.actorKind, "human");
  assert.equal(draft.provenance.channel, "inspector");
  assert.match(
    plan.agentPreparationPrompt("Adding a todo updates the count"),
    /restricted DevFlow test-agent tools.*Adding a todo updates the count.*commit review.*separate run request/i
  );
});

test("typed assertion and selector authoring keep hard and observation semantics distinct", async () => {
  const steps = await loadPanelModule("inspector-steps.js");

  assert.equal(steps.isObservationOnlyAssertion("pageChanged"), true);
  assert.equal(steps.isObservationOnlyAssertion("exists"), false);
  assert.equal(steps.isStrictAuthoringSelector({
    automationId: "save", matchCount: 1, quality: "durable",
  }), true);
  assert.equal(steps.isStrictAuthoringSelector({
    automationId: "TodoCheckBox",
    stableItemKey: "todo-42",
    collectionScope: "TodoList",
    matchCount: 1,
    quality: "stable-item-key",
  }), true);
  assert.equal(steps.isStrictAuthoringSelector({
    id: "runtime-42", matchCount: 1, quality: "fragile",
  }), false);
  assert.equal(steps.isStrictAuthoringSelector({
    text: "Save", matchCount: 2, quality: "ambiguous",
  }), false);

  const flow = {
    steps: [
      { stepId: "a", seq: 1, action: "tap" },
      { stepId: "b", seq: 2, action: "fill" },
      { stepId: "c", seq: 3, action: "tap" },
    ],
    deviceSteps: [
      { afterStep: 1, action: "tap", x: 10, y: 20 },
      { afterStep: 2, action: "tap", x: 30, y: 40 },
    ],
  };
  const moved = steps.moveFlowStep(flow, 1, -1);
  assert.deepEqual(moved.steps.map((step) => step.stepId), ["b", "a", "c"]);
  assert.deepEqual(moved.steps.map((step) => step.seq), [1, 2, 3]);
  assert.deepEqual(moved.deviceSteps.map((step) => step.afterStep), [2, 1]);
  const removed = steps.removeFlowStep(flow, 1);
  assert.deepEqual(removed.steps.map((step) => step.stepId), ["a", "c"]);
  assert.deepEqual(removed.steps.map((step) => step.seq), [1, 2]);
  assert.deepEqual(removed.deviceSteps.map((step) => step.afterStep), [1]);
  const merged = steps.mergeRecordedFlow(flow, {
    steps: [{ stepId: "d", seq: 1, action: "tap" }],
    deviceSteps: [
      { afterStep: 0, action: "tap", x: 50, y: 60 },
      { afterStep: 1, action: "tap", x: 70, y: 80 },
    ],
  });
  assert.deepEqual(merged.steps.map((step) => step.seq), [1, 2, 3, 4]);
  assert.deepEqual(merged.deviceSteps.map((step) => step.afterStep), [1, 2, 3, 4]);
  assert.deepEqual(steps.usableSelectorFromMatch({
    automationId: "TodoCheckBox",
    stableItemKey: "todo-42",
    collectionScope: "TodoList",
  }, [], false), {
    automationId: "TodoCheckBox",
    stableItemKey: "todo-42",
    collectionScope: "TodoList",
  });
  assert.equal(steps.usableSelectorFromMatch(
    { automationId: "TodoCheckBox" },
    [{ automationId: "TodoCheckBox" }, { automationId: "TodoCheckBox" }],
    false
  ), null);
  const issues = steps.authoringIssues({
    errors: ["step 1: selector must resolve exactly one element; it currently reports 9 matches."],
  });
  assert.equal(issues[0].stepSequence, 1);
  assert.equal(issues[0].remediation, "resolve-selector");
});

test("authoring panels retain explicit recording, validation, diff, and commit controls", () => {
  const plan = read("inspector-plan.js");
  const steps = read("inspector-steps.js");
  const devflow = read("devflow.js");
  const bridge = read("inspector-host-bridge.js");
  const requests = read("inspector-agent-requests.js");

  assert.match(plan, /Reload saved test/);
  assert.match(plan, /Download current draft/);
  assert.match(plan, /What should this test prove\? \(required\)/);
  assert.match(plan, /Create your first test/);
  assert.match(plan, /Prepare a draft with your agent/);
  assert.match(plan, /trusted approval is not available yet/);
  assert.match(plan, /required: true/);
  assert.match(plan, /ariaInvalid/);
  assert.match(plan, /ariaDescribedBy/);
  for (const expander of [
    "Name and file (optional)",
    "Scenarios and outcomes (optional)",
    "Setup, safety, and platforms (optional)",
    "Review metadata (optional)",
  ]) {
    assert.match(plan, new RegExp(expander.replace(/[()]/g, "\\$&")));
  }
  assert.match(plan, /Optional\. The Goal remains the required description\./);
  assert.ok(plan.indexOf("df-goal-input") < plan.indexOf("df-test-name-input"));
  assert.doesNotMatch(plan, /Advanced: Quick record without a managed plan|Quick record raw draft/);
  assert.doesNotMatch(plan, /Save plan draft only|Advanced test settings|className: 'df-plan-more'/);
  assert.doesNotMatch(plan, /helpers\.capability\(root, 'saveTestBundle'\)/);
  for (const text of [
    "Go to Goal", "Start recording", "Stop recording", "Discard recording", "Continue to Review",
    "Go to Steps", "Check test", "Review changes", "Save test", "Continue to Run",
    "Record more steps", "Select a step", "Save step", "Move up", "Move down", "Remove step",
    "Step details \\(optional\\)",
    "Add expected result", "Download recording draft", "Check current app now", "exactly one element", "never prefilled",
    "The target no longer exists", "Target no longer exists",
    "Target AutomationId", "AutomationId of the target that should disappear",
    "Resolve step", "Check matching controls", "Use this control", "stable item key",
  ]) {
    assert.match(steps, new RegExp(text));
  }
  assert.match(steps, /addResult\.open = !assertions\.some/);
  assert.match(steps, /draft\.checkPassed !== true/);
  assert.match(steps, /draft\.diffReviewed !== true/);
  assert.match(steps, /function selectorCheckKey/);
  assert.match(steps, /selectorChecks\.clear\(\)/);
  assert.match(steps, /assertion\.kind === 'exists' \|\| assertion\.kind === 'propEquals'/);
  assert.match(steps, /assertion\.kind === 'notExists'/);
  assert.match(steps, /stepId:\$\{step\.stepId\}/);
  assert.doesNotMatch(steps, /Recording status|className: 'df-steps-more'/);
  assert.doesNotMatch(steps, /At least one hard outcome check|Typed assertion composer/);
  assert.match(devflow, /const retainedPlan = source === 'recording'/);
  assert.match(devflow, /startAppendingRecording/);
  assert.match(devflow, /appendRecordingBase/);
  assert.match(devflow, /lastWorkbenchCanDrive/);
  assert.match(devflow, /authoringChanged \|\| driveAuthorityChanged/);
  assert.match(devflow, /preservePanel: !rerender/);
  assert.match(read("inspector-workbench.js"), /options\.preservePanel === true/);
  assert.match(devflow, /mergeRecordedFlow/);
  assert.match(devflow, /reviewedDeviceEffectsDigest/);
  assert.match(devflow, /Exact device mutations/);
  assert.match(read("inspector-run.js"), /Exact device mutations/);
  assert.match(read("inspector-run.js"), /Record the surrounding device screen for this run/);
  assert.match(devflow, /setEvidenceConsent\(patch\)[\s\S]*state\.preflight = null;[\s\S]*state\.approved = false;/);
  assert.match(read("inspector-trace.js"), /Download device recording/);
  assert.match(requests, /Exact device changes/);
  assert.match(devflow, /bindingStale/);
  assert.match(devflow, /older flow digest/);
  assert.match(devflow, /openGoalForRecovery\('Add a Goal before recording actions\.'/);
  assert.match(devflow, /A Goal is required to save this test\. Your recorded draft is still here\./);
  assert.match(devflow, /Recording draft saved —/);
  assert.match(devflow, /downloadRecordingDraft/);
  assert.match(devflow, /Save the test before running it\. Your recorded draft is still here\./);
  assert.match(bridge, /loadTestBundle/);
  // Embedded surfaces sandbox the iframe without `allow-downloads`, so the bridge must never
  // promise a download it cannot deliver.
  assert.match(bridge, /Downloads are blocked in this embedded surface/);
  assert.doesNotMatch(bridge, /The browser will offer a download fallback/);
});

test("modal focus and Goal recovery include editable controls and focus restoration", () => {
  const workbench = read("inspector-workbench.js");
  const dialog = read("inspector-dialog.js");
  for (const source of [workbench, dialog]) {
    assert.match(source, /input:not\(\[disabled\]\):not\(\[type="hidden"\]\)/);
    assert.match(source, /textarea:not\(\[disabled\]\)/);
    assert.match(source, /select:not\(\[disabled\]\)/);
    assert.match(source, /\[contenteditable\]:not\(\[contenteditable="false"\]\)/);
  }
  assert.match(workbench, /returnFocus/);
  assert.match(workbench, /focusGoal/);
  assert.match(workbench, /pendingGoalFocus/);
  assert.match(workbench, /win\.clearTimeout\(pendingGoalFocus\)/);
  assert.match(workbench, /if \(!opened \|\| state\.selectedStage !== 'goal'\) return;/);
  assert.match(workbench, /const shortcutDigit = \/\^Digit\(\[1-9\]\)\$\/\.test\(event\.code\) \|\| \/\^Numpad\(\[1-9\]\)\$\/\.test\(event\.code\)/);
  const workflowShortcut = workbench.indexOf("if ((event.ctrlKey || event.metaKey) && event.altKey) {");
  const editableGuard = workbench.indexOf("if (isEditableTarget(event.target)) return;");
  assert.ok(workflowShortcut >= 0 && editableGuard > workflowShortcut, "workflow shortcuts must run before editable-target protection");
  assert.match(workbench, /if \(isEditableTarget\(event\.target\)\) return;/);
  assert.doesNotMatch(read("inspector.html"), /id="df-toggle-record"/);
  assert.match(read("inspector-plan.js"), /df-goal-help/);
});

test("toolbar keeps toggle semantics when actions move into More", () => {
  const devflow = read("devflow.js");
  const css = read("devflow.css");

  assert.match(devflow, /menuitemcheckbox/);
  assert.match(devflow, /aria-checked/);
  assert.match(devflow, /function setToolbarToggleState/);
  assert.match(devflow, /setToolbarToggleState\(tb\.bounds, on\)/);
  assert.match(devflow, /setToolbarToggleState\(toggleDockBtn, true\)/);
  assert.match(css, /\.df-toolbar-overflow \.df-tool-btn\.df-active[\s\S]*var\(--df-accent\)/);
});

test("VS Code host advertises implemented capabilities and refreshes after broker restarts", () => {
  const extension = readVscodeHost();
  const identity = readFileSync(
    new URL("../vscode-inspector/src/agent-identity.ts", import.meta.url),
    "utf8",
  );
  const capabilities = extension.match(/const VSCODE_HOST_CAPABILITIES = \[([^\]]+)\]/)?.[1] || "";

  assert.notEqual(capabilities, "");
  assert.doesNotMatch(capabilities, /attachTestContext/);
  assert.match(capabilities, /requestTestProposal/);
  assert.doesNotMatch(extension, /devflow:attachTestContext/);
  assert.match(extension, /devflow:requestTestProposal/);
  assert.match(extension, /isPartialQuery: false/);
  assert.match(extension, /const restartWatcher = setInterval/);
  assert.match(extension, /inspectorConnectionSignature/);
  assert.match(identity, /function normalizeAgentIdentityPart/);
  assert.match(identity, /function sameAgentIdentity/);
  assert.match(identity, /sameAgentIdentity\(current, candidate\)/);
  assert.match(identity, /normalizeAgentIdentityPart\(current\.project\)/);
  assert.match(identity, /normalizeAgentIdentityPart\(current\.tfm\)/);
  assert.match(identity, /normalizeAgentIdentityPart\(current\.platform\)/);
  assert.match(identity, /normalizeAgentIdentityPart\(current\.appName\)/);
  assert.match(identity, /normalizeAgentIdentityPart\(candidate\.id\) === currentId/);
  assert.doesNotMatch(identity, /sameAppMatches|candidate\.appName === current\.appName/);
  assert.match(extension, /panel\.webview\.options =/);
  assert.match(extension, /function withInspectorStartupHints/);
  assert.match(extension, /\["test", hints\?\.test\]/);
  assert.match(extension, /\["trace", hints\?\.trace\]/);
  assert.match(extension, /\["agentRequest", hints\?\.agentRequest\]/);
});

test("run uses a concise confirmed run check with visual progress and advanced legacy replay", () => {
  const devflow = read("devflow.js");
  const run = read("inspector-run.js");

  assert.match(devflow, /async openPreflight\(\)/);
  assert.match(devflow, /async function legacyQuickReplay/);
  assert.match(run, /Review, confirm, and start/);
  assert.match(run, /starts only after the confirmation dialog/);
  assert.match(read("inspector-repair.js"), /candidate\.unique === true \|\| candidate\.validation\?\.unique === true/);
  assert.match(read("inspector-trace.js"), /report\.replayEligibility\?\.repairEligibility === true/);
  assert.match(run, /Go to Review/);
  assert.match(run, /readiness\?\.hardOutcomeCheck !== true/);
  assert.match(run, /Run details \(optional\)/);
  assert.match(run, /Legacy quick replay \(advanced\)/);
  assert.doesNotMatch(run, /df-run-advanced-actions/);
  assert.match(run, /df-run-progress-visual/);
  assert.match(run, /df-run-step-/);
  assert.match(devflow, /scheduleRefresh\(125\)/);
  assert.match(devflow, /openStage\?\.\('results'/);
  assert.match(devflow, /focusResults/);
});

test("results show persistent summaries and contextual next actions", () => {
  const trace = read("inspector-trace.js");

  for (const text of [
    "Test needs attention", "Run again", "Improve test",
    "View failed step", "Review repair", "Improve selector", "Resolve", "Technical trace details",
  ]) {
    assert.match(trace, new RegExp(text));
  }
  assert.match(trace, /Test passed \(replay only\)/);
  assert.match(trace, /Test passed \(independently verified\)/);
  assert.match(trace, /Replay pass/);
  assert.match(trace, /passPresentation\(report\)/);
  assert.match(trace, /Diagnose this failure with your agent/);
  assert.match(trace, /report\.failure\?\.category === 'selector'/);
  assert.match(trace, /df-results-summary/);
  assert.match(trace, /df-results-banner/);
  for (const next of ["Go to Goal", "Go to Steps", "Go to Review", "Go to Run"]) {
    assert.match(trace, new RegExp(next));
  }
  assert.match(trace, /resultsNextStep/);
  assert.match(trace, /Open a result from another run/);
  assert.match(trace, /without running or changing the connected app/);
});

test("empty Results routes to the first unmet workflow prerequisite", async () => {
  const trace = await loadPanelModule("inspector-trace.js");

  assert.equal(trace.resultsNextStep({}).stage, "goal");
  assert.equal(trace.resultsNextStep({ goal: true }).stage, "record");
  assert.equal(trace.resultsNextStep({ goal: true, recordedSteps: true }).stage, "review");
  assert.equal(trace.resultsNextStep({
    goal: true,
    recordedSteps: true,
    hardOutcomeCheck: true,
  }).stage, "review");
  assert.equal(trace.resultsNextStep({
    goal: true,
    recordedSteps: true,
    hardOutcomeCheck: true,
    savedBundle: true,
  }).stage, "run");
});

test("Tests landing, recovery, and fast-run handoff stay visible", () => {
  const plan = read("inspector-plan.js");
  const workbenchCss = read("inspector-workbench.css");
  const devflow = read("devflow.js");
  const workbench = read("inspector-workbench.js");

  assert.match(plan, /This is the only required field before adding steps/);
  assert.match(plan, /Record steps/);
  assert.match(plan, /Open saved test/);
  assert.match(plan, /df-saved-test-picker/);
  assert.match(plan, /Choose Markdown file/);
  assert.match(plan, /Reload saved test/);
  assert.match(plan, /Download current draft/);
  assert.match(workbenchCss, /df-workbench-recovery/);
  assert.match(workbenchCss, /df-workbench-tool-list/);
  assert.match(devflow, /900 - visibleFor/);
  assert.match(workbench, /Tests opened\. Enter a Goal or open a saved test\./);
});

test("ambiguity controller re-verifies a human choice and changes only a draft selector", () => {
  const devflow = read("devflow.js");
  const improve = read("inspector-improve.js");
  const repair = read("inspector-repair.js");
  const trace = read("inspector-trace.js");
  const helperStart = devflow.indexOf("async applyHumanSelectedSelector");
  const helperEnd = devflow.indexOf("async verifyAssertion", helperStart);
  const helper = devflow.slice(helperStart, helperEnd);
  const improveStart = devflow.indexOf("async resolveAmbiguity");
  const improveEnd = devflow.indexOf("async openSource", improveStart);
  const ambiguityController = devflow.slice(improveStart, improveEnd);

  assert.ok(helperStart >= 0 && helperEnd > helperStart);
  assert.ok(improveStart >= 0 && improveEnd > improveStart);
  assert.match(trace, /Resolve \$\{count\} matches/);
  assert.match(trace, /will not choose automatically/i);
  assert.match(improve, /Highlight in app/);
  assert.match(improve, /Improve app testability/);
  assert.match(ambiguityController, /authoringController\.verifySelector/);
  assert.match(ambiguityController, /testWorkbench\?\.open\?\.\('improve'/);
  assert.match(ambiguityController, /testWorkbench\?\.open\?\.\('source'/);
  assert.match(ambiguityController, /applyHumanSelectedSelector/);
  assert.match(ambiguityController, /state\.filters\.step/);
  assert.match(ambiguityController, /await analyze\(true\)/);
  assert.doesNotMatch(ambiguityController, /repairController|\/repair\/propose/);
  assert.match(helper, /authoringStepIndexForFailure/);
  assert.match(helper, /verification\.matchCount !== 1/);
  assert.match(helper, /authoringDraft\.flowDirty = true/);
  assert.match(helper, /action, assertion, expected value/i);
  assert.match(helper, /Save test, then rerun it/);
  assert.doesNotMatch(helper, /commitBundle|runController|startRun|legacyQuickReplay/);
  assert.match(repair, /The selector matched more than one control/);
  assert.match(repair, /Resolve in Improve/);
});
