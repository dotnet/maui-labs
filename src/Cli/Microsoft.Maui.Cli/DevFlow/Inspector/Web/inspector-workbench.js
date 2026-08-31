import { describeHostCapability } from './inspector-host-bridge.js';
import { renderPlanPanel } from './inspector-plan.js';
import { renderStepsPanel } from './inspector-steps.js';
import { renderRunPanel } from './inspector-run.js';
import { renderTracePanel } from './inspector-trace.js';
import { renderRepairPanel } from './inspector-repair.js';
import { renderImprovePanel } from './inspector-improve.js';
import { renderSourceProposalPanel } from './inspector-source.js';
import { renderPrototypeStudyEvidenceCard } from './inspector-study.js';

export const WORKBENCH_TABS = Object.freeze(['plan', 'steps', 'review', 'run', 'trace', 'requests', 'repair', 'improve', 'source']);
export const WORKBENCH_STAGES = Object.freeze(['goal', 'record', 'review', 'run', 'results']);
const STAGE_TABS = Object.freeze({
  goal: 'plan',
  record: 'steps',
  review: 'review',
  run: 'run',
  results: 'trace',
});

/**
 * Tabs that belong to the guided authoring journey rather than to a standalone review surface.
 * `improve` is here too: it reads a completed guided run, so it cannot stand on its own.
 */
const GUIDED_TABS = Object.freeze(new Set([...Object.values(STAGE_TABS), 'improve']));

export const AUTHORITY_STATES = Object.freeze(['writer', 'read-only', 'takeover-pending', 'lease-lost']);
export const AUTHORING_STATES = Object.freeze(['none', 'draft', 'recording', 'validating', 'saved', 'stale']);
export const RUN_STATES = Object.freeze([
  'idle', 'preflight', 'queued', 'acquiring-lease', 'preparing', 'running',
  'passed', 'failed', 'cancelled', 'timed-out', 'lease-lost', 'infrastructure-error',
  'unknown-completion', 'orphaned',
]);
export const TRACE_STATES = Object.freeze(['none', 'loading', 'ready', 'malformed', 'provenance-warning']);
export const REPAIR_STATES = Object.freeze([
  'unavailable', 'classifying', 'proposed', 'previewed', 'approved', 'applying',
  'applied', 'verified', 'stale', 'rejected', 'reverted', 'approval-expired',
  'awaiting-host-apply', 'apply-failed', 'verification-failed', 'rollback-required',
  'rollback-failed',
]);
export const SOURCE_PROPOSAL_STATES = Object.freeze([
  'unavailable', 'analyzing', 'proposed', 'previewed', 'approved', 'awaiting-host-apply',
  'applying', 'applied', 'verification-failed', 'rollback-required', 'verified', 'stale',
  'rejected', 'reverted', 'rollback-failed',
]);

const PANEL_RENDERERS = Object.freeze({
  plan: renderPlanPanel,
  steps: renderStepsPanel,
  review: renderStepsPanel,
  run: renderRunPanel,
  trace: renderTracePanel,
  repair: renderRepairPanel,
  improve: renderImprovePanel,
  source: renderSourceProposalPanel,
});

function validState(value, values, fallback) {
  return typeof value === 'string' && values.includes(value) ? value : fallback;
}

function boundedHint(value) {
  if (typeof value !== 'string') return null;
  const hint = value.trim();
  return hint && hint.length <= 4096 ? hint : null;
}

export async function deliverAgentRequest({
  bridge,
  prompt,
  label = 'Work with your agent',
  intent = 'devflow-test-assistance',
  copyText,
  agent,
} = {}) {
  const value = typeof prompt === 'function' ? await prompt() : prompt;
  const request = bindAgentPrompt(value, agent);
  if (!request) {
    return {
      ok: false,
      delivery: 'none',
      buttonLabel: 'Agent request unavailable',
      status: 'No agent request is available yet.',
    };
  }

  let hostError = '';
  if (bridge?.has?.('requestTestProposal')) {
    try {
      const result = await bridge.request('requestTestProposal', {
        prompt: request,
        title: label,
        intent,
      }, 30000);
      if (result?.ok) {
        const copied = result.value?.delivery === 'clipboard';
        return {
          ok: true,
          delivery: copied ? 'clipboard' : 'agent',
          buttonLabel: copied ? 'Prompt copied' : 'Sent to agent',
          status: result.status || result.message ||
            (copied ? 'Copied the agent request.' : `Sent “${label}” to the host agent.`),
        };
      }
      hostError = String(result?.error || 'The host agent did not accept the request.');
    } catch (error) {
      hostError = String(error?.message || 'The host agent did not accept the request.');
    }
  }

  const copied = typeof copyText === 'function' && await copyText(request);
  return {
    ok: !!copied,
    delivery: copied ? 'clipboard' : 'none',
    buttonLabel: copied ? 'Prompt copied' : 'Copy failed',
    status: copied
      ? hostError
        ? `The host agent was unavailable. Copied the request instead: ${hostError}`
        : 'Copied the agent request. Paste it into your coding agent chat.'
      : hostError || 'The host agent was unavailable and the prompt could not be copied.',
  };
}

export function bindAgentPrompt(prompt, agent = null) {
  const request = String(prompt || '').trim();
  if (!request) return '';
  const agentId = String(agent?.id || '').replace(/[\u0000-\u001f\u007f]/g, ' ').trim().slice(0, 256);
  const agentInstanceId = String(agent?.instanceId || '').replace(/[\u0000-\u001f\u007f]/g, ' ').trim().slice(0, 256);
  const binding = agentId && agentInstanceId
    ? [
      `Target exactly agentId "${agentId}" and agentInstanceId "${agentInstanceId}".`,
      'Chat approval or affirmative text expresses intent only; it does not authorize commit or run.',
      'A current Test Workbench broker grant is required separately for each commit or run.',
    ].join(' ')
    : '';
  return [binding, request].filter(Boolean).join(' ').slice(0, 8192);
}

export function parseWorkbenchStartupHints(search = '') {
  const query = new URLSearchParams(search);
  return {
    test: boundedHint(query.get('test')),
    trace: boundedHint(query.get('trace')),
    agentRequest: boundedHint(query.get('agentRequest')),
  };
}

export function createInitialWorkbenchState(search = '') {
  return {
    authority: 'writer',
    authoring: 'none',
    run: 'idle',
    trace: 'none',
    repair: 'unavailable',
    source: 'unavailable',
    selectedTab: 'plan',
    selectedStage: 'goal',
    selectedTraceStep: null,
    selectedEvidence: null,
    draft: { dirty: false },
    startupHints: parseWorkbenchStartupHints(search),
  };
}

export function normalizeWorkbenchState(current, patch = {}) {
  const state = current || createInitialWorkbenchState();
  const selectedTab = validState(patch.selectedTab, WORKBENCH_TABS, state.selectedTab);
  const inferredStage = patch.selectedTab === undefined
    ? state.selectedStage
    : Object.entries(STAGE_TABS).find(([, tab]) => tab === selectedTab)?.[0] || state.selectedStage;
  const hints = patch.startupHints && typeof patch.startupHints === 'object'
    ? {
      test: boundedHint(patch.startupHints.test) ?? state.startupHints.test,
      trace: boundedHint(patch.startupHints.trace) ?? state.startupHints.trace,
      agentRequest: boundedHint(patch.startupHints.agentRequest) ?? state.startupHints.agentRequest,
    }
    : state.startupHints;
  return {
    ...state,
    authority: validState(patch.authority, AUTHORITY_STATES, state.authority),
    authoring: validState(patch.authoring, AUTHORING_STATES, state.authoring),
    run: validState(patch.run, RUN_STATES, state.run),
    trace: validState(patch.trace, TRACE_STATES, state.trace),
    repair: validState(patch.repair, REPAIR_STATES, state.repair),
    source: validState(patch.source, SOURCE_PROPOSAL_STATES, state.source),
    selectedTab,
    selectedStage: validState(patch.selectedStage, WORKBENCH_STAGES, inferredStage),
    selectedTraceStep: patch.selectedTraceStep === undefined ? state.selectedTraceStep : patch.selectedTraceStep,
    selectedEvidence: patch.selectedEvidence === undefined ? state.selectedEvidence : patch.selectedEvidence,
    draft: patch.draft && typeof patch.draft === 'object' ? { ...state.draft, ...patch.draft } : state.draft,
    startupHints: hints,
  };
}

export function workbenchSafetyMessage(state) {
  if (state.run === 'unknown-completion' || state.run === 'orphaned') {
    return 'Unknown or orphaned completion disables retry, repair, and apply until a human resolves the run.';
  }
  if (state.repair === 'approval-expired') {
    return 'Approval expired. Review the current plan, flow, and patch digest before any future apply request.';
  }
  if (state.repair === 'awaiting-host-apply') {
    return 'This repair is awaiting a host acknowledgement. The shared shell never applies a source patch.';
  }
  if (state.repair === 'verification-failed') {
    return 'Verification failed. The next safe state is rollback-required, never a retry shortcut.';
  }
  if (state.repair === 'rollback-required' || state.repair === 'rollback-failed') {
    return 'Rollback requires explicit human handling; the Test Workbench shell cannot change source.';
  }
  if (state.source === 'verification-failed' || state.source === 'rollback-required' || state.source === 'rollback-failed') {
    return 'Source verification failed. An explicit atomic rollback is required; no flow selector is changed automatically.';
  }
  return null;
}

function isEditableTarget(target) {
  return target instanceof HTMLElement &&
    (target.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/i.test(target.tagName));
}

export function visibleFocusables(root) {
  const selector = [
    'button:not([disabled])',
    '[href]',
    'input:not([disabled]):not([type="hidden"])',
    'textarea:not([disabled])',
    'select:not([disabled])',
    '[contenteditable]:not([contenteditable="false"])',
    '[tabindex]:not([tabindex="-1"])',
  ].join(', ');
  return [...root.querySelectorAll(selector)]
    .filter((element) => element instanceof HTMLElement &&
      !element.closest('[hidden], [aria-hidden="true"]') &&
      element.getClientRects().length > 0);
}

export function retainWorkbenchFocus(element, schedule = (callback) => callback()) {
  if (!element ||
      typeof element.focus !== 'function' ||
      typeof element.getClientRects !== 'function' ||
      element.isConnected === false ||
      element.getClientRects().length === 0) {
    return false;
  }
  schedule(() => {
    if (element.isConnected !== false && element.getClientRects().length > 0)
      element.focus({ preventScroll: true });
  });
  return true;
}

export function enabledWorkbenchTabs(tabs, tabList) {
  return [...(tabs || [])].filter((button) =>
    !button.disabled &&
    !button.hidden &&
    button.closest?.('[role="tablist"]') === tabList);
}

function readableState(value) {
  return String(value || 'none').replace(/-/g, ' ');
}

export function createInspectorWorkbench(options = {}) {
  const doc = options.document || document;
  const win = options.window || window;
  const root = options.root || doc.getElementById('df-workbench');
  const toggleButton = options.toggleButton || doc.getElementById('df-toggle-workbench');
  if (!root || !toggleButton) {
    return Object.freeze({
      open: () => {},
      close: () => {},
      toggle: () => {},
      isOpen: () => false,
      updateState: () => {},
      state: () => createInitialWorkbenchState(),
    });
  }

  const tabs = [...root.querySelectorAll('[data-workbench-tab]')];
  const stageButtons = tabs.filter((button) => button.dataset.workbenchStage);
  const toolsGroup = root.querySelector('#df-workbench-advanced-tools');
  const panels = new Map(WORKBENCH_TABS.map((tab) => [
    tab,
    tab === 'requests' ? root.querySelector('#df-agent-requests') : root.querySelector(`#df-workbench-panel-${tab}`),
  ]));
  const panelBodies = new Map(WORKBENCH_TABS.map((tab) => [
    tab,
    tab === 'requests'
      ? root.querySelector('#df-agent-requests-body')
      : root.querySelector(`#df-workbench-panel-${tab} .df-workbench-panel-body`),
  ]));
  const status = root.querySelector('#df-workbench-status');
  const alert = root.querySelector('#df-workbench-alert');
  const strip = root.querySelector('#df-workbench-strip');
  const timeline = doc.getElementById('df-timeline');
  const resizeHandle = root.querySelector('#df-workbench-resize');
  const closeButton = root.querySelector('#df-workbench-close');
  const getLayout = typeof options.getLayout === 'function'
    ? options.getLayout
    : () => ({ width: 'wide', height: 'tall', overlay: false });
  const setInspectorStatus = typeof options.setStatus === 'function' ? options.setStatus : () => {};
  const copyText = typeof options.copyText === 'function'
    ? options.copyText
    : async (text) => {
      if (typeof win.navigator?.clipboard?.writeText !== 'function') return false;
      try {
        await win.navigator.clipboard.writeText(text);
        return true;
      } catch {
        return false;
      }
    };
  const bridge = options.hostBridge || null;
  const authoring = options.authoring || null;
  const run = options.run || null;
  const trace = options.trace || null;
  const improve = options.improve || null;
  const repair = options.repair || null;
  const source = options.source || null;
  const study = options.study || null;
  const agent = options.agent || run?.state?.().agent || null;
  const featureCapabilities = Object.freeze({
    workbench: options.featureCapabilities?.workbench === true,
    repair: options.featureCapabilities?.repair === true,
    source: options.featureCapabilities?.source === true,
    traceImport: options.featureCapabilities?.traceImport === true,
  });
  let state = createInitialWorkbenchState(win.location?.search || '');
  if (!featureCapabilities.workbench && !state.startupHints.agentRequest) {
    // The default landing tab belongs to the guided journey. Without that capability the panel
    // would open on a stage whose own tab button is hidden, so land on the approval inbox instead.
    state = normalizeWorkbenchState(state, { selectedTab: 'requests' });
  }
  let opened = false;
  let returnFocus = null;
  let resizeStart = null;
  let pendingGoalFocus = null;
  let pendingReturnFocus = null;

  function capabilities() {
    return bridge && typeof bridge.capabilities === 'function' ? bridge.capabilities() : [];
  }

  function featureEnabled(feature) {
    return featureCapabilities[feature] === true;
  }

  /**
   * The tab the panel lands on when nothing else is chosen. Without the workbench capability the
   * guided journey does not exist here, so Agent requests — the one surface an approval-only
   * Inspector really has — is the landing tab rather than an authoring panel with no tabs.
   */
  function defaultTab() {
    return featureEnabled('workbench') ? 'plan' : 'requests';
  }

  function setStatus(message) {
    if (status) status.textContent = message || '';
    if (message) setInspectorStatus(message);
  }

  function isModal() {
    const layout = getLayout();
    return layout.width === 'narrow' || layout.height === 'short';
  }

  function updateSheetOffset() {
    const toolbar = doc.getElementById('df-toolbar');
    const inspectorStatus = doc.getElementById('df-status');
    const toolbarHeight = toolbar?.getBoundingClientRect().height || 0;
    const statusHeight = inspectorStatus?.getBoundingClientRect().height || 0;
    root.style.setProperty('--df-workbench-top', `${Math.ceil(toolbarHeight + statusHeight)}px`);
  }

  function syncTimelineStrip() {
    if (!strip) return;
    const draft = authoring?.state?.() || {};
    const draftName = draft.flowName || draft.flow?.name || null;
    const draftSteps = Array.isArray(draft.flow?.steps) ? draft.flow.steps.length : 0;
    if (draftName) {
      strip.textContent = [draftName, `${draftSteps} step${draftSteps === 1 ? '' : 's'}`].join(' · ');
      strip.title = strip.textContent;
      return;
    }
    const hidden = !timeline || timeline.classList.contains('df-hidden');
    const title = doc.getElementById('df-timeline-title-text')?.textContent?.trim();
    const meta = doc.getElementById('df-timeline-meta')?.textContent?.trim();
    const visibleSteps = timeline?.querySelectorAll('#df-timeline-steps .df-tl-step').length || 0;
    const steps = draft.recording
      ? Math.max(Number(draft.recordingSteps) || 0, visibleSteps)
      : visibleSteps;
    strip.textContent = hidden
      ? 'No active test'
      : [title || 'Workflow', meta, steps ? `${steps} step${steps === 1 ? '' : 's'}` : null].filter(Boolean).join(' · ');
    strip.title = strip.textContent;
  }

  function journeyStatus() {
    const draft = authoring?.state?.() || {};
    const readiness = draft.readiness || {};
    const flowSteps = Array.isArray(draft.flow?.steps) ? draft.flow.steps.length : 0;
    const goal = readiness.goal === true || !!String(draft.plan?.goal || '').trim();
    const recorded = readiness.recordedSteps === true || flowSteps > 0;
    const saved = readiness.savedBundle === true;
    const reviewed = saved && readiness.hardOutcomeCheck === true;
    const terminal = ['passed', 'failed', 'cancelled', 'timed-out', 'lease-lost', 'infrastructure-error', 'unknown-completion', 'orphaned']
      .includes(state.run);
    const resultsAvailable = terminal || state.trace !== 'none';
    const results = resultsAvailable && state.trace === 'ready';
    const complete = { goal, record: recorded, review: reviewed, run: terminal, results };
    const blocked = {
      goal: false,
      record: !goal,
      review: !goal || !recorded,
      run: !reviewed,
      results: false,
    };
    const next = !goal
      ? 'goal'
      : !recorded
        ? 'record'
        : !reviewed
          ? 'review'
          : !terminal
            ? 'run'
            : 'results';
    return { complete, blocked, next, facts: { goal, recorded, reviewed, terminal, resultsAvailable } };
  }

  function renderJourney(status = journeyStatus()) {
    const unlockMessages = {
      record: 'Add a Goal to unlock Steps.',
      review: 'Record at least one step to unlock Review.',
      run: 'Save a reviewed test with an expected result to unlock Run.',
      results: 'Run or import a result to unlock Results.',
    };
    for (const button of stageButtons) {
      const stage = button.dataset.workbenchStage;
      if (!WORKBENCH_STAGES.includes(stage)) continue;
      const stageState = status.complete[stage]
        ? 'complete'
        : stage === status.next
          ? 'current'
          : status.blocked[stage]
            ? 'blocked'
            : 'pending';
      button.dataset.state = stageState;
      button.setAttribute('aria-current', stageState === 'current' ? 'step' : 'false');
      const numberElement = button.querySelector('.df-stage-number');
      if (numberElement) {
        numberElement.dataset.stepNumber ||= numberElement.textContent?.trim() || '';
        numberElement.textContent = stageState === 'complete' ? '✓' : numberElement.dataset.stepNumber;
      }
      const number = numberElement?.dataset.stepNumber;
      const label = button.querySelector('.df-stage-label')?.textContent?.trim() || stage;
      const selected = button.dataset.workbenchTab === state.selectedTab;
      const disabled = stageState === 'blocked' && !selected;
      const unlock = disabled ? unlockMessages[stage] : null;
      button.disabled = disabled;
      button.setAttribute('aria-disabled', String(disabled));
      button.title = unlock || (stage === 'results' ? 'View the latest test result.' : '');
      button.setAttribute(
        'aria-label',
        `${number ? `${number}. ` : ''}${label}: ${stageState}.${unlock ? ` ${unlock}` : ''}`
      );
      const indicator = button.querySelector('.df-stage-state');
      if (indicator) indicator.textContent = stageState === 'complete' ? 'Complete' : stageState;
    }
  }

  function toolAvailability(tab) {
    const draft = authoring?.state?.() || {};
    const hasFlow = !!draft.flow || !!draft.markdown;
    const traceState = trace?.state?.() || {};
    const report = traceState.report || traceState.run?.report || null;
    const repairState = repair?.state?.() || {};
    const sourceState = source?.state?.() || {};
    // The guided journey is one capability. When it is off, every stage is off with it — otherwise
    // the panel can still be selected and rendered while its own tab button is hidden.
    if (!featureEnabled('workbench') && GUIDED_TABS.has(tab)) {
      return {
        enabled: false,
        reason: 'Guided test authoring is disabled in this Inspector.',
      };
    }
    switch (tab) {
      case 'requests':
        return {
          // Without the guided journey the approval inbox is the panel, so it stays selectable even
          // while empty: that is what makes an authoring-only Inspector show its "work with your
          // agent" state instead of opening onto nothing.
          enabled: tabs.find((button) => button.dataset.workbenchTab === tab)?.dataset.available === 'true'
            || !featureEnabled('workbench'),
          reason: 'Agent requests appear here after your agent prepares a test or asks to run it.',
        };
      case 'repair':
        if (!featureEnabled('repair')) {
          return {
            enabled: false,
            reason: 'Selector repair is disabled in this Inspector.',
          };
        }
        if (traceState.mode === 'imported') {
          return {
            enabled: false,
            reason: 'Imported results are read-only. Reproduce the failure locally before using Repair.',
          };
        }
        if (['unknown-completion', 'orphaned'].includes(state.run)) {
          return {
            enabled: false,
            reason: 'Resolve the uncertain run completion before using Repair.',
          };
        }
        return {
          enabled: ['failed', 'timed-out', 'infrastructure-error'].includes(state.run) ||
            (traceState.mode === 'local' && !!report?.failure) ||
            !!repairState.eligibility || !!repairState.proposal || !!repairState.error,
          reason: 'Open a failed local result to unlock Repair.',
        };
      case 'improve':
        return {
          enabled: hasFlow || !!improve?.state?.().analysis || !!improve?.state?.().ambiguity,
          reason: 'Record or open a test to unlock Improve.',
        };
      case 'source':
        if (!featureEnabled('source')) {
          return {
            enabled: false,
            reason: 'Source preview is disabled in this Inspector. Stay on the visible control or ask your coding agent for a read-only testability plan.',
          };
        }
        if (traceState.mode === 'imported') {
          return {
            enabled: false,
            reason: 'Imported results are read-only. Return to the live app before changing source.',
          };
        }
        if (['unknown-completion', 'orphaned'].includes(state.run)) {
          return {
            enabled: false,
            reason: 'Resolve the uncertain run completion before changing source.',
          };
        }
        return {
          enabled: sourceState.selectedElement?.hasSource === true ||
            !!sourceState.selectedElement?.sourceFile ||
            !!sourceState.eligibility || !!sourceState.proposal || !!sourceState.error,
          reason: 'Select a source-mapped control to unlock Source.',
        };
      default:
        return { enabled: true, reason: '' };
    }
  }

  function makeHelpers() {
    const currentCapabilities = capabilities();
    return {
      root(title) {
        const element = doc.createElement('div');
        element.className = 'df-workbench-placeholder';
        const heading = doc.createElement('h3');
        heading.textContent = title;
        element.append(heading);
        return element;
      },
      intro(parent, text) {
        const paragraph = doc.createElement('p');
        paragraph.className = 'df-workbench-intro';
        paragraph.textContent = text;
        parent.append(paragraph);
      },
      list(parent, items) {
        const list = doc.createElement('ul');
        list.className = 'df-workbench-list';
        for (const item of items) {
          const line = doc.createElement('li');
          line.textContent = item;
          list.append(line);
        }
        parent.append(list);
      },
      status(parent, label) {
        const values = {
          'Authority state': state.authority,
          'Authoring state': state.authoring,
          'Run state': state.run,
          'Trace state': state.trace,
          'Repair state': state.repair,
          'Source proposal state': state.source,
        };
        const line = doc.createElement('p');
        line.className = 'df-workbench-state';
        const name = doc.createElement('strong');
        name.textContent = `${label}: `;
        const value = doc.createElement('span');
        value.textContent = readableState(values[label]);
        line.append(name, value);
        parent.append(line);
      },
      hint(parent, name, message) {
        const hint = state.startupHints[name];
        if (!hint) return;
        const line = doc.createElement('p');
        line.className = 'df-workbench-note';
        line.textContent = `${message} Hint: ${hint}`;
        parent.append(line);
      },
      capability(parent, capability) {
        const line = doc.createElement('p');
        line.className = currentCapabilities.includes(capability)
          ? 'df-workbench-note'
          : 'df-workbench-note df-workbench-note-muted';
        line.textContent = describeHostCapability(currentCapabilities, capability);
        parent.append(line);
      },
      disabledAction(parent, label, title) {
        const button = doc.createElement('button');
        button.type = 'button';
        button.className = 'df-workbench-action';
        button.textContent = label;
        button.disabled = true;
        button.title = title;
        parent.append(button);
        return button;
      },
      action(parent, label, onClick) {
        const button = doc.createElement('button');
        button.type = 'button';
        button.className = 'df-workbench-action';
        button.textContent = label;
        button.addEventListener('click', onClick);
        parent.append(button);
        return button;
      },
      agentAction(parent, config = {}) {
        if (!config.prompt) return null;
        const label = config.buttonLabel || config.title || 'Work with your agent';
        const button = doc.createElement('button');
        button.type = 'button';
        button.className = 'df-workbench-action df-agent-action';
        button.textContent = label;
        if (config.description) button.title = config.description;
        button.addEventListener('click', async () => {
          button.disabled = true;
          button.setAttribute('aria-busy', 'true');
          button.textContent = 'Preparing agent request…';
          try {
            if (bridge?.has?.('requestTestProposal')) button.textContent = 'Sending to agent…';
            const outcome = await deliverAgentRequest({
              bridge,
              prompt: config.prompt,
              label,
              intent: config.intent,
              copyText,
              agent,
            });
            button.textContent = outcome.buttonLabel;
            setStatus(outcome.status);
          } catch (error) {
            button.textContent = 'Agent request unavailable';
            setStatus(error?.message || 'Could not prepare the agent request.');
          } finally {
            win.setTimeout(() => {
              button.disabled = false;
              button.setAttribute('aria-busy', 'false');
              button.textContent = label;
            }, 1600);
          }
        });
        parent.append(button);
        return button;
      },
      placeholder(message) {
        setStatus(message);
      },
      announceFailure(message) {
        if (alert) alert.textContent = message || '';
        if (message) setInspectorStatus(message);
      },
      authoring,
      run,
      trace,
      improve,
      repair,
      source,
      study,
      inspectApp() {
        doc.getElementById('df-mode-inspect')?.click();
      },
      studyEvidenceCard(parent) {
        const card = renderPrototypeStudyEvidenceCard(study, doc);
        if (card) parent.append(card);
      },
      selectTab,
      selectStage,
      featureEnabled,
      toolAvailability,
      focusGoal,
      workbenchState: () => state,
      updateDraft(patch, rerender = true) {
        if (authoring && typeof authoring.update === 'function') {
          authoring.update(patch, rerender);
          return;
        }
        state = normalizeWorkbenchState(state, { draft: patch });
        if (rerender) renderPanel();
      },
      rerender: renderPanel,
      safety(parent) {
        const message = workbenchSafetyMessage(state);
        if (!message) return;
        const line = doc.createElement('p');
        line.className = 'df-workbench-safety';
        line.textContent = message;
        parent.append(line);
      },
    };
  }

  function renderChrome() {
    const journey = journeyStatus();
    let visibleTools = 0;
    for (const tab of WORKBENCH_TABS) {
      const selected = tab === state.selectedTab;
      const button = tabs.find((candidate) => candidate.dataset.workbenchTab === tab);
      const panel = panels.get(tab);
      if (button) {
        button.classList.toggle('df-active', selected);
        button.setAttribute('aria-selected', String(selected));
        button.tabIndex = selected ? 0 : -1;
        if (!button.dataset.workbenchStage) {
          const availability = toolAvailability(tab);
          const disabled = !availability.enabled;
          button.hidden = disabled;
          button.disabled = disabled;
          button.setAttribute('aria-disabled', String(disabled));
          button.title = disabled ? availability.reason : '';
          if (!disabled) visibleTools++;
        }
      }
      if (panel) panel.hidden = !selected;
    }
    if (toolsGroup) toolsGroup.hidden = visibleTools === 0;
    renderJourney(journey);
  }

  function renderPanel() {
    const selectedButton = tabs.find((button) => button.dataset.workbenchTab === state.selectedTab);
    const selectedAvailability = toolAvailability(state.selectedTab);
    if (selectedButton && !selectedAvailability.enabled) {
      const nextStage = journeyStatus().next;
      const stageFallback = STAGE_TABS[nextStage] || 'plan';
      // Falling back onto the guided journey only works when the journey exists. Without it the
      // one always-reachable surface is Agent requests.
      const fallbackTab = toolAvailability(stageFallback).enabled ? stageFallback : defaultTab();
      const fallbackStage = fallbackTab === stageFallback ? nextStage : state.selectedStage;
      state = normalizeWorkbenchState(state, { selectedTab: fallbackTab, selectedStage: fallbackStage });
      setStatus(fallbackTab === stageFallback
        ? `${selectedAvailability.reason} Returned to ${nextStage === 'record' ? 'Steps' : nextStage.charAt(0).toUpperCase() + nextStage.slice(1)}.`
        : `${selectedAvailability.reason} Showing Agent requests.`);
      win.setTimeout(() => {
        tabs.find((button) => button.dataset.workbenchTab === fallbackTab)?.focus?.({ preventScroll: true });
      }, 0);
    }
    const active = doc.activeElement;
    const retainedTraceStep = state.selectedTab === 'trace' && active instanceof HTMLElement
      ? active.dataset.traceStep
      : null;
    const renderer = PANEL_RENDERERS[state.selectedTab];
    const body = panelBodies.get(state.selectedTab);
    if (body && renderer) body.replaceChildren(renderer(makeHelpers()));
    renderChrome();
    if (retainedTraceStep) {
      const retained = body?.querySelectorAll?.('[data-trace-step]');
      const target = [...(retained || [])].find((element) => element.dataset.traceStep === retainedTraceStep);
      if (target instanceof HTMLElement) {
        win.setTimeout(() => target.focus({ preventScroll: true }), 0);
      }
    }
  }

  function selectTab(tab, focus = false, stage = null) {
    if (!WORKBENCH_TABS.includes(tab)) return false;
    const availability = toolAvailability(tab);
    if (!availability.enabled) {
      const retainedFocus = doc.activeElement;
      setStatus(availability.reason);
      retainWorkbenchFocus(retainedFocus, (callback) => win.setTimeout(callback, 0));
      return false;
    }
    const mappedStage = stage || Object.entries(STAGE_TABS).find(([, candidate]) => candidate === tab)?.[0];
    state = normalizeWorkbenchState(state, { selectedTab: tab, selectedStage: mappedStage || state.selectedStage });
    if (tab === 'trace') study?.resultsOpened?.(trace?.state?.().run);
    renderPanel();
    if (focus) {
      const target = tabs.find((button) => button.dataset.workbenchTab === tab);
      target?.focus();
      target?.scrollIntoView?.({ block: 'nearest', inline: 'nearest' });
    }
    return true;
  }

  function selectStage(stage, focus = false) {
    const tab = STAGE_TABS[stage];
    if (!tab) return;
    selectTab(tab, focus, stage);
  }

  function updateOpenChrome() {
    root.classList.toggle('df-hidden', !opened);
    root.classList.toggle('df-workbench-modal', opened && isModal());
    root.setAttribute('aria-hidden', String(!opened));
    root.setAttribute('aria-modal', String(opened && isModal()));
    root.setAttribute('role', opened && isModal() ? 'dialog' : 'region');
    doc.body.classList.toggle('df-workbench-open', opened);
    toggleButton.classList.toggle('df-active', opened);
    toggleButton.setAttribute('aria-pressed', String(opened));
    toggleButton.setAttribute('aria-expanded', String(opened));
  }

  function open(tab = state.selectedTab, focus = true) {
    if (!toolAvailability(tab).enabled && tab !== 'requests') tab = defaultTab();
    const firstOpen = !opened;
    const focusGoalOnOpen = firstOpen && tab === 'plan' && state.selectedStage === 'goal';
    if (!opened) {
      if (pendingReturnFocus !== null) {
        win.clearTimeout(pendingReturnFocus);
        pendingReturnFocus = null;
      }
      const active = doc.activeElement;
      returnFocus = active instanceof HTMLElement && active !== doc.body ? active : toggleButton;
      opened = true;
      updateSheetOffset();
      options.onOpen?.();
      updateOpenChrome();
      syncTimelineStrip();
    }
    const selected = selectTab(tab, false, tab === state.selectedTab ? state.selectedStage : null);
    if (firstOpen) {
      setStatus(tab === 'requests'
        ? 'Tests opened to Agent requests.'
        : 'Tests opened. Enter a Goal or open a saved test.');
    }
    if (focus && selected !== false) {
      win.setTimeout(() => {
        if (focusGoalOnOpen) {
          const goal = root.querySelector('#df-goal-input');
          if (goal instanceof HTMLElement && goal.getClientRects().length > 0) {
            goal.focus({ preventScroll: true });
            goal.scrollIntoView?.({ block: 'nearest', inline: 'nearest' });
            return;
          }
        }
        const target = tabs.find((button) => button.dataset.workbenchTab === state.selectedTab);
        target?.focus();
        target?.scrollIntoView?.({ block: 'nearest', inline: 'nearest' });
      }, 0);
    }
  }

  function openStage(stage, focus = true) {
    const tab = STAGE_TABS[stage];
    if (!tab) return;
    if (!opened) open(tab, false);
    selectStage(stage, focus);
  }

  function focusGoal() {
    if (pendingGoalFocus !== null) {
      win.clearTimeout(pendingGoalFocus);
      pendingGoalFocus = null;
    }
    openStage('goal', false);
    pendingGoalFocus = win.setTimeout(() => {
      pendingGoalFocus = null;
      if (!opened || state.selectedStage !== 'goal') return;
      const goal = root.querySelector('#df-goal-input');
      if (goal instanceof HTMLElement && goal.getClientRects().length > 0)
        goal.focus({ preventScroll: true });
    }, 0);
  }

  function close(restore = false) {
    if (pendingGoalFocus !== null) {
      win.clearTimeout(pendingGoalFocus);
      pendingGoalFocus = null;
    }
    if (!opened) return;
    opened = false;
    updateOpenChrome();
    options.onClose?.();
    if (restore) {
      const target = returnFocus?.isConnected ? returnFocus : toggleButton;
      target?.focus({ preventScroll: true });
      if (pendingReturnFocus !== null) win.clearTimeout(pendingReturnFocus);
      pendingReturnFocus = win.setTimeout(() => {
        pendingReturnFocus = null;
        if (!opened && target?.isConnected && target.getClientRects().length > 0)
          target.focus({ preventScroll: true });
      }, 0);
    }
  }

  function toggle() {
    if (opened) close(true);
    else open();
  }

  function trapFocus(event) {
    if (!opened || !isModal() || event.key !== 'Tab') return false;
    const focusables = visibleFocusables(root);
    if (!focusables.length) {
      event.preventDefault();
      root.focus({ preventScroll: true });
      return true;
    }
    const index = focusables.indexOf(doc.activeElement);
    if (event.shiftKey && (index <= 0 || !root.contains(doc.activeElement))) {
      event.preventDefault();
      focusables[focusables.length - 1].focus({ preventScroll: true });
      return true;
    }
    if (!event.shiftKey && index === focusables.length - 1) {
      event.preventDefault();
      focusables[0].focus({ preventScroll: true });
      return true;
    }
    return false;
  }

  function onKeyDown(event) {
    if (event.key === 'Escape' && opened) {
      event.preventDefault();
      event.stopImmediatePropagation();
      close(true);
      return;
    }
    if (trapFocus(event)) return;
    const key = event.key.toLowerCase();
    if ((event.ctrlKey || event.metaKey) && event.altKey) {
      if (key === 't') {
        event.preventDefault();
        event.stopImmediatePropagation();
        toggle();
        return;
      }
      const shortcutDigit = /^Digit([1-9])$/.test(event.code) || /^Numpad([1-9])$/.test(event.code)
        ? event.code.slice(-1)
        : key;
      const stageNumber = Number(shortcutDigit);
      if (stageNumber >= 1 && stageNumber <= WORKBENCH_STAGES.length) {
        event.preventDefault();
        event.stopImmediatePropagation();
        const stage = WORKBENCH_STAGES[stageNumber - 1];
        const button = stageButtons.find((candidate) => candidate.dataset.workbenchStage === stage);
        if (button?.disabled) {
          setStatus(button.title);
          return;
        }
        openStage(stage);
        return;
      }
      if (key === 'r') {
        event.preventDefault();
        event.stopImmediatePropagation();
        const runTab = tabs.find((button) => button.dataset.workbenchTab === 'run');
        if (runTab?.disabled) {
          setStatus(runTab.title);
          return;
        }
        openStage('run');
        run?.openPreflight?.();
        setStatus('Run check opened. Review and explicitly approve before starting.');
        return;
      }
      if (key === 'c') {
        event.preventDefault();
        event.stopImmediatePropagation();
        open('run');
        run?.requestCancel?.();
        setStatus('Cancellation confirmation opened. No run was changed yet.');
      }
      return;
    }
    if (isEditableTarget(event.target)) return;
    if (opened && state.selectedTab === 'trace' && event.key === '[') {
      event.preventDefault();
      trace?.previousStep?.();
      return;
    }
    if (opened && state.selectedTab === 'trace' && event.key === ']') {
      event.preventDefault();
      trace?.nextStep?.();
    }
  }

  function onTabKeyDown(event) {
    const tabList = event.currentTarget.closest('[role="tablist"]');
    const enabled = enabledWorkbenchTabs(tabs, tabList);
    const index = enabled.indexOf(event.currentTarget);
    let next = null;
    if (event.key === 'ArrowRight' || event.key === 'ArrowDown') next = enabled[(index + 1) % enabled.length];
    else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') next = enabled[(index - 1 + enabled.length) % enabled.length];
    else if (event.key === 'Home') next = enabled[0];
    else if (event.key === 'End') next = enabled[enabled.length - 1];
    else return;
    if (!next) return;
    event.preventDefault();
    selectTab(next.dataset.workbenchTab, true);
  }

  function updateResizeAria() {
    if (!resizeHandle) return;
    const height = Math.round(root.getBoundingClientRect().height);
    resizeHandle.setAttribute('aria-valuenow', String(height));
  }

  function resizeTo(clientY) {
    if (!resizeStart || getLayout().width !== 'wide') return;
    const max = Math.max(240, Math.floor(win.innerHeight * 0.8));
    const next = Math.max(240, Math.min(max, resizeStart.height + resizeStart.y - clientY));
    root.style.setProperty('--df-workbench-height', `${Math.round(next)}px`);
    updateResizeAria();
  }

  toggleButton.addEventListener('click', toggle);
  closeButton?.addEventListener('click', () => close(true));
  for (const tab of tabs) {
    tab.addEventListener('click', () => selectTab(tab.dataset.workbenchTab, true));
    tab.addEventListener('keydown', onTabKeyDown);
  }
  resizeHandle?.addEventListener('pointerdown', (event) => {
    if (getLayout().width !== 'wide') return;
    event.preventDefault();
    resizeStart = { y: event.clientY, height: root.getBoundingClientRect().height };
    resizeHandle.setPointerCapture?.(event.pointerId);
  });
  resizeHandle?.addEventListener('pointermove', (event) => resizeTo(event.clientY));
  resizeHandle?.addEventListener('pointerup', () => { resizeStart = null; });
  resizeHandle?.addEventListener('keydown', (event) => {
    if (getLayout().width !== 'wide') return;
    const current = root.getBoundingClientRect().height;
    let next = null;
    if (event.key === 'ArrowUp') next = current + 24;
    else if (event.key === 'ArrowDown') next = current - 24;
    else if (event.key === 'Home') next = 240;
    else if (event.key === 'End') next = win.innerHeight * 0.8;
    if (next == null) return;
    event.preventDefault();
    root.style.setProperty('--df-workbench-height', `${Math.round(Math.max(240, Math.min(win.innerHeight * 0.8, next)))}px`);
    updateResizeAria();
  });
  doc.addEventListener('keydown', onKeyDown, true);
  win.addEventListener('resize', updateSheetOffset);
  const chromeObserver = win.ResizeObserver
    ? new win.ResizeObserver(updateSheetOffset)
    : null;
  for (const chrome of [doc.getElementById('df-toolbar'), doc.getElementById('df-status')]) {
    if (chrome) chromeObserver?.observe(chrome);
  }
  const timelineObserver = timeline && win.MutationObserver
    ? new win.MutationObserver(syncTimelineStrip)
    : null;
  timelineObserver?.observe(timeline, { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: ['class'] });
  const unsubscribe = bridge?.onCapabilitiesChanged?.(() => {
    if (opened) renderPanel();
  });
  const onStateEvent = (event) => {
    state = normalizeWorkbenchState(state, event.detail || {});
    renderPanel();
  };
  win.addEventListener('devflow:workbench-state', onStateEvent);

  renderPanel();
  syncTimelineStrip();
  updateOpenChrome();
  updateResizeAria();

  return Object.freeze({
    open,
    openStage,
    focusGoal,
    featureEnabled,
    canSelectTab(tab) {
      return WORKBENCH_TABS.includes(tab) && (tab === 'requests' || toolAvailability(tab).enabled);
    },
    close,
    toggle,
    isOpen: () => opened,
    state: () => ({ ...state, startupHints: { ...state.startupHints }, draft: { ...state.draft } }),
    updateState(patch, options = {}) {
      state = normalizeWorkbenchState(state, patch);
      const activeToolBecameUnavailable =
        !stageButtons.some((button) => button.dataset.workbenchTab === state.selectedTab) &&
        !toolAvailability(state.selectedTab).enabled;
      if (options.preservePanel === true && !activeToolBecameUnavailable) renderChrome();
      else renderPanel();
    },
    destroy() {
      if (pendingGoalFocus !== null) win.clearTimeout(pendingGoalFocus);
      if (pendingReturnFocus !== null) win.clearTimeout(pendingReturnFocus);
      doc.removeEventListener('keydown', onKeyDown, true);
      win.removeEventListener('resize', updateSheetOffset);
      win.removeEventListener('devflow:workbench-state', onStateEvent);
      chromeObserver?.disconnect();
      timelineObserver?.disconnect();
      unsubscribe?.();
    },
  });
}
