const TERMINAL_STATES = new Set([
  'passed', 'failed', 'cancelled', 'timed-out', 'lease-lost', 'infrastructure-error',
  'unknown-completion', 'orphaned',
]);

function boundedText(value, fallback = 'Not available', maximum = 320) {
  if (value == null || value === '') return fallback;
  return String(value).replace(/[\u0000-\u001f\u007f]/g, ' ').slice(0, maximum);
}

function asArray(value) {
  return Array.isArray(value) ? value : [];
}

function element(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text != null) node.textContent = text;
  return node;
}

function section(root, title, className = '') {
  const node = element('section', `df-run-section ${className}`.trim());
  const heading = element('h4', null, title);
  node.append(heading);
  root.append(node);
  return node;
}

function field(parent, label, value, state) {
  const line = element('p', `df-run-field${state ? ` df-run-field-${state}` : ''}`);
  const name = element('strong', null, `${label}: `);
  line.append(name, document.createTextNode(boundedText(value)));
  parent.append(line);
}

function list(parent, values, empty = 'None declared.') {
  const items = asArray(values);
  if (!items.length) {
    parent.append(element('p', 'df-workbench-note df-workbench-note-muted', empty));
    return;
  }
  const node = element('ul', 'df-workbench-list');
  for (const value of items) node.append(element('li', null, boundedText(value)));
  parent.append(node);
}

function button(parent, label, handler, disabled = false, className = '') {
  const node = element('button', `df-workbench-action ${className}`.trim(), label);
  node.type = 'button';
  node.disabled = disabled;
  if (!disabled && typeof handler === 'function') node.addEventListener('click', handler);
  parent.append(node);
  return node;
}

function check(parent, id, label, checked, onChange, description) {
  const row = element('label', 'df-run-check');
  const input = document.createElement('input');
  input.type = 'checkbox';
  input.id = id;
  input.checked = checked === true;
  input.addEventListener('change', () => onChange?.(input.checked));
  row.append(input, document.createTextNode(` ${label}`));
  parent.append(row);
  if (description) parent.append(element('p', 'df-authoring-field-hint', description));
}

function selectorSummary(step) {
  const selector = step?.args?.selector || step?.selector || {};
  const key = ['automationId', 'text', 'type', 'id'].find((name) => selector[name]);
  return key ? `${key} selector` : 'target selector';
}

function secretReferences(value, result = []) {
  if (!value || typeof value !== 'object') return result;
  for (const [key, nested] of Object.entries(value)) {
    if (/secret/i.test(key) && typeof nested === 'string' && nested.trim()) {
      result.push(nested.trim().slice(0, 128));
      continue;
    }
    if (nested && typeof nested === 'object') secretReferences(nested, result);
  }
  return result;
}

/**
 * Converts a semantic flow to an intentionally value-free side-effect summary. This is a display
 * aid only; the broker remains the authority for admission and execution.
 */
export function summarizePlannedEffects(flow) {
  const summary = [];
  const refs = new Set();
  for (const step of asArray(flow?.steps)) {
    const action = String(step?.action || '').toLowerCase();
    if (['tap', 'fill', 'navigate', 'theme', 'setproperty'].includes(action)) {
      const label = action === 'setproperty' ? 'property change' : action === 'navigate' ? 'navigation' : action;
      summary.push(`${label} via ${selectorSummary(step)}`);
    }
    for (const reference of secretReferences(step)) refs.add(reference);
  }
  for (const reference of refs) summary.push(`secret reference: ${reference} (value withheld)`);
  for (const effect of asArray(flow?.deviceEffects)) {
    if (typeof effect === 'string' && effect.trim())
      summary.push(`device: ${boundedText(effect, '', 256)}`);
  }
  return summary;
}

export function formatElapsed(startedAt, endedAt, now = Date.now()) {
  const start = Date.parse(startedAt || '');
  const end = endedAt ? Date.parse(endedAt) : now;
  if (!Number.isFinite(start) || !Number.isFinite(end) || end < start) return 'Not started';
  const milliseconds = Math.max(0, end - start);
  const seconds = Math.floor(milliseconds / 1000);
  const minutes = Math.floor(seconds / 60);
  return minutes ? `${minutes}m ${String(seconds % 60).padStart(2, '0')}s` : `${seconds}s`;
}

export function runStateIsTerminal(state) {
  return TERMINAL_STATES.has(state);
}

function admissionMessage(reason) {
  if (reason?.code === 'independent-oracle-absent') {
    return 'This test can run, but the result will not be marked independently verified because no independent check is configured.';
  }
  return reason?.message || reason?.code;
}

export function runReadinessIssues({
  preflight,
  stalePlan = false,
  hasFlow = true,
  policy = 'unspecified',
  manualOneShot = false,
} = {}) {
  const reasons = asArray(preflight?.admission?.reasons);
  const blockers = [
    ...asArray(preflight?.errors),
    ...reasons.filter((reason) => reason?.blocking === true).map(admissionMessage),
  ].filter(Boolean);
  const notes = reasons
    .filter((reason) => reason?.blocking !== true)
    .map(admissionMessage)
    .filter(Boolean);
  if (stalePlan) blockers.unshift('The saved plan no longer matches the recorded steps.');
  if (!hasFlow) blockers.unshift('Save a test before opening Run.');
  if (policy === 'non-replayable' && manualOneShot !== true)
    blockers.push('Authorize this one human run before Review and start is available.');
  return {
    blockers: [...new Set(blockers)],
    notes: [...new Set(notes)],
  };
}

function appendLegacyReplay(parent, flow, state, controller) {
  const compatibility = section(parent, 'Compatibility');
  compatibility.append(element('p', 'df-authoring-field-hint',
    'Legacy quick replay bypasses the run check and is kept only for compatibility.'));
  button(
    compatibility,
    'Legacy quick replay (advanced)',
    () => controller?.legacyQuickReplay?.(),
    !flow || state.starting,
  );
}

function renderNovicePreflight(root, helpers, state, controller) {
  const plan = state.plan || null;
  const flow = state.flow || null;
  const preflight = state.preflight || null;
  const target = state.target || {};
  const policy = plan?.sideEffectPolicy || 'unspecified';
  const deviceEffects = [...asArray(preflight?.deviceEffects)];
  if (state.evidence?.includeDeviceRecording === true &&
      !deviceEffects.some((effect) => /record the surrounding device screen/i.test(String(effect)))) {
    deviceEffects.push('Record the surrounding device screen for the full run');
  }
  const oracles = [...asArray(plan?.businessOracles), ...asArray(plan?.independentBusinessOracles)];
  const { blockers, notes } = runReadinessIssues({
    preflight,
    stalePlan: state.stalePlan,
    hasFlow: !!flow,
    policy,
    manualOneShot: state.manualOneShot,
  });

  if (!state.reproduction &&
      (state.readiness?.savedBundle !== true || state.readiness?.hardOutcomeCheck !== true)) {
    helpers.intro(root, 'Save the reviewed test with at least one expected result before checking the run.');
    const next = section(root, 'Next step');
    button(next, 'Go to Review', () => helpers.selectStage?.('review'), false, 'df-authoring-primary');
    if (flow) {
      const compatibility = element('details', 'df-run-details');
      compatibility.append(element('summary', null, 'Compatibility (optional)'));
      root.append(compatibility);
      appendLegacyReplay(compatibility, flow, state, controller);
    }
    return;
  }

  helpers.intro(root, 'Review the target and safety summary, then choose Review, confirm, and start. The test starts only after the confirmation dialog.');

  const summary = section(root, 'Run check', 'df-run-summary');
  field(summary, 'Target', [
    target.appName || state.agent?.appName || 'No live app selected',
    target.platform || state.agent?.platform,
    target.device?.deviceType,
  ].filter(Boolean).join(' · '), target.agentId ? 'success' : 'warning');
  const ready = preflight?.ok === true && blockers.length === 0;
  field(summary, 'Safety readiness',
    ready
      ? notes.length ? 'Ready to run; verification limited' : 'Ready to run'
      : state.preflighting ? 'Checking' : blockers.length ? 'Needs attention' : 'Needs a run check',
    ready ? notes.length ? 'warning' : 'success' : blockers.length ? 'error' : 'warning');
  field(summary, 'Side effects',
    deviceEffects.length
      ? `${deviceEffects.length} reviewed device mutation${deviceEffects.length === 1 ? '' : 's'} · ${policy}`
      : policy === 'none' ? 'No declared side effects' : policy);
  if (deviceEffects.length) {
    const device = element('div', 'df-run-device-effects');
    device.append(element('strong', null, 'Exact device mutations'));
    list(device, deviceEffects);
    summary.append(device);
  }
  if (blockers.length) {
    const block = element('div', 'df-run-blockers');
    block.append(element('strong', null, 'What needs attention'));
    list(block, blockers.map((item) => boundedText(item)));
    summary.append(block);
  } else {
    summary.append(element('p', 'df-workbench-note', 'No current blocker is reported. Review, confirm, and start opens the final confirmation before execution.'));
  }
  if (notes.length) {
    const note = element('div', 'df-run-verification-notes');
    note.append(element('strong', null, 'Verification notes'));
    list(note, notes.map((item) => boundedText(item)));
    summary.append(note);
  }

  const actions = section(root, 'Next action');
  const canReviewAndRun = ready &&
    (policy !== 'non-replayable' || state.manualOneShot === true) &&
    !state.importedMode && !state.starting;

  const onlyManualAuthorizationMissing = preflight?.ok === true &&
    !state.stalePlan &&
    policy === 'non-replayable' &&
    state.manualOneShot !== true &&
    blockers.length === 1;
  if (onlyManualAuthorizationMissing) {
    check(
      actions,
      'df-run-manual-one-shot',
      'Authorize this one human run only.',
      state.manualOneShot,
      (value) => controller?.setManualOneShot?.(value),
      'This does not enable retries, repair, or source changes.'
    );
  }

  if (canReviewAndRun || state.starting) {
    button(
      actions,
      state.starting ? 'Starting test…' : 'Review, confirm, and start',
      () => controller?.reviewAndRun?.(),
      state.starting,
      'df-authoring-primary'
    );
  } else if (onlyManualAuthorizationMissing) {
    actions.append(element('p', 'df-authoring-field-hint',
      'Authorize the one human run above to continue.'));
  } else if (state.stalePlan) {
    button(actions, 'Go to Review', () => helpers.selectStage?.('review'), false, 'df-authoring-primary');
    actions.append(element('p', 'df-authoring-field-hint',
      'Review and save the updated test to bind the plan to the current recorded steps.'));
  } else if (preflight?.ok !== true || blockers.length > 0) {
    button(
      actions,
      state.preflighting ? 'Checking run…' : preflight ? 'Check again' : 'Check run',
      () => controller?.refreshPreflight?.(),
      state.preflighting,
      'df-authoring-primary'
    );
    actions.append(element('p', 'df-authoring-field-hint',
      'When the check succeeds, the final Review, confirm, and start action becomes available.'));
  }

  const details = element('details', 'df-run-details');
  details.append(element('summary', null, 'Run details (optional)'));
  root.append(details);
  const selection = section(details, 'Saved test');
  field(selection, 'Flow', state.flowName || flow?.name || 'No saved test');
  field(selection, 'Flow digest', state.flowDigest || 'Not calculated');
  field(selection, 'Plan', plan?.planId || 'No managed plan');
  field(selection, 'Plan revision', plan?.revision ?? 'Not declared');
  field(selection, 'Plan digest', state.planDigest || 'Not calculated');

  const targetDetails = section(details, 'Technical target');
  field(targetDetails, 'Agent', target.agentId || state.agent?.id);
  field(targetDetails, 'Instance', target.agentInstanceId || state.agent?.instanceId);
  field(targetDetails, 'Build', target.app?.build || 'Not reported');
  field(targetDetails, 'Platform', target.platform || state.agent?.platform);
  field(targetDetails, 'Device', [target.device?.deviceType, target.device?.idiom].filter(Boolean).join(' · '));

  const capabilities = section(details, 'Capabilities');
  const requirements = asArray(plan?.requirements?.requiredCapabilities);
  if (!requirements.length) {
    capabilities.append(element('p', 'df-workbench-note df-workbench-note-muted', 'No capabilities are declared for this test.'));
  } else {
    for (const requirement of requirements) {
      const name = boundedText(requirement?.name, 'Unnamed capability');
      const available = target.capabilities && Object.hasOwn(target.capabilities, name)
        ? target.capabilities[name] === true
        : null;
      field(capabilities, name,
        available === true ? 'available' : available === false ? 'unavailable' : 'not reported',
        available === false ? 'error' : available === true ? 'success' : 'warning');
    }
  }

  const safety = section(details, 'Reset, checks, and evidence');
  const reset = plan?.reset || {};
  field(safety, 'Reset strategy', reset.strategy || 'Not declared');
  field(safety, 'App-state seed', reset.appStateSeed?.fingerprint || reset.seedFingerprint || 'Not declared');
  field(safety, 'Backend/test-data seed', reset.backendTestDataSeed?.fingerprint || reset.backendStateFingerprint || 'Not declared');
  field(safety, 'Independent check', oracles.length ? 'Declared' : 'Not declared', oracles.length ? 'success' : 'warning');
  if (deviceEffects.length) {
    field(safety, 'Device mutation digest', preflight?.deviceEffectsDigest || 'Not calculated');
    list(safety, deviceEffects);
  }
  check(
    safety,
    'df-run-evidence-screenshot',
    'Include a screenshot in failure evidence.',
    state.evidence?.includeScreenshot,
    (value) => controller?.setEvidenceConsent?.({ includeScreenshot: value }),
    'Off by default because screenshots can contain on-screen data.'
  );
  check(
    safety,
    'df-run-evidence-workflow',
    'Include flow text in failure evidence.',
    state.evidence?.includeWorkflow,
    (value) => controller?.setEvidenceConsent?.({ includeWorkflow: value }),
    'Off by default because recorded values can be sensitive.'
  );
  check(
    safety,
    'df-run-evidence-device-recording',
    'Record the surrounding device screen for this run.',
    state.evidence?.includeDeviceRecording,
    (value) => controller?.setEvidenceConsent?.({ includeDeviceRecording: value }),
    'Off by default because device video can contain content outside the app.'
  );
  if (state.reproduction) {
    const reproduction = section(details, 'Separate local reproduction');
    field(reproduction, 'Imported artifact', state.reproduction.artifactId || 'Isolated imported identity');
    field(reproduction, 'Current source fingerprint', state.reproduction.expectation?.appSourceFingerprint || 'Unavailable in this host');
  }

  appendLegacyReplay(details, flow, state, controller);
}

function renderProgress(root, helpers, state, controller) {
  const run = state.run || {};
  const terminal = runStateIsTerminal(run.state);
  helpers.intro(root, terminal
    ? 'This test is finished. Results are ready to review.'
    : 'The live app preview refreshes as each action changes.');

  const progress = section(root, 'Run progress');
  const total = Math.max(0, Number(state.total) || 0);
  const completed = Math.max(0, Math.min(total || Number.MAX_SAFE_INTEGER, Number(state.completed) || 0));
  const percentage = total ? Math.round((completed / total) * 100) : 0;
  const visual = element('div', 'df-run-progress-visual');
  visual.setAttribute('role', 'progressbar');
  visual.setAttribute('aria-label', 'Test run progress');
  visual.setAttribute('aria-valuemin', '0');
  visual.setAttribute('aria-valuemax', String(total));
  visual.setAttribute('aria-valuenow', String(completed));
  visual.setAttribute('aria-valuetext', `Step ${Math.min(total || 0, completed + (terminal ? 0 : 1))} of ${total}`);
  const fill = element('span', 'df-run-progress-fill');
  fill.style.width = `${percentage}%`;
  visual.append(fill);
  progress.append(visual);
  field(progress, 'Progress', `Step ${Math.min(total || 0, completed + (terminal ? 0 : 1))} of ${total}`);
  field(progress, 'Run ID', run.runId);
  field(progress, 'State', run.state || 'queued', terminal && run.state !== 'passed' ? 'error' : '');
  field(progress, 'Current action', state.currentAction || state.currentStep || 'Waiting for the first action');
  field(progress, 'Elapsed', formatElapsed(run.startedAt || run.createdAt, run.endedAt));
  field(progress, 'Steps complete', `${completed} / ${total}`);
  field(progress, 'Latest safe event', state.latestEvent || run.message || 'Waiting for broker progress.');
  if (run.cancellationRequested || state.cancelPending) {
    progress.append(element('p', 'df-workbench-safety',
      'Cancellation is pending. No future step will start, but an in-flight command may already complete.'));
  }

  const steps = section(root, 'Steps');
  const semanticSteps = asArray(state.progressSteps);
  if (!semanticSteps.length) {
    steps.append(element('p', 'df-workbench-note df-workbench-note-muted', 'Steps appear here as the saved test runs.'));
  } else {
    const ordered = element('ol', 'df-run-step-list');
    for (const step of semanticSteps) {
      const item = element('li', `df-run-step df-run-step-${step.state || 'pending'}`);
      item.append(element('strong', null, `${step.sequence}. `), document.createTextNode(
        `${boundedText(step.action, 'step', 80)} — ${step.state || 'pending'}`
      ));
      if (step.state === 'current') item.setAttribute('aria-current', 'step');
      ordered.append(item);
    }
    steps.append(ordered);
  }

  const eventDetails = element('details', 'df-run-event-details');
  eventDetails.append(element('summary', null, 'Run event details'));
  root.append(eventDetails);
  const events = section(eventDetails, 'Lifecycle events');
  list(events, asArray(run.events).map((event) => {
    const time = event?.at ? new Date(event.at).toLocaleTimeString() : 'time unavailable';
    return `${time} · ${boundedText(event?.kind, 'event')} · ${boundedText(event?.message, '')}`;
  }), 'No run event is available yet.');

  const controls = section(root, 'Run controls');
  if (!terminal) {
    if (state.cancelConfirm) {
      controls.append(element('p', 'df-workbench-safety',
        'Cancel this run? DevFlow will not assume that an in-flight action was undone.'));
      button(controls, 'Confirm cancellation request', () => controller?.cancel?.(), state.cancelPending, 'df-authoring-primary');
      button(controls, 'Keep run active', () => controller?.dismissCancel?.(), state.cancelPending);
    } else {
      const cancel = button(controls, 'Cancel run', () => controller?.requestCancel?.(), state.cancelPending);
      cancel.setAttribute('aria-keyshortcuts', 'Control+Alt+C Meta+Alt+C');
    }
  } else {
    button(controls, 'Open Results', () => {
      helpers.selectStage?.('results');
      controller?.focusResults?.();
    }, !state.hasTrace);
    if (['unknown-completion', 'orphaned'].includes(run.state)) {
      controls.append(element('p', 'df-workbench-safety',
        'Retry, repair, and apply remain disabled until a human resolves this terminal state.'));
    }
  }
}

export function renderRunPanel(helpers) {
  const root = helpers.root('Run');
  const controller = helpers.run;
  const state = controller?.state?.() || {};

  if (state.importedMode) {
    helpers.intro(root, 'This imported result is read-only. Open Results and choose Reproduce locally when you are ready to test the live app.');
    helpers.safety(root);
    return root;
  }

  if (state.run?.runId) renderProgress(root, helpers, state, controller);
  else renderNovicePreflight(root, helpers, state, controller);
  helpers.safety(root);
  return root;
}
