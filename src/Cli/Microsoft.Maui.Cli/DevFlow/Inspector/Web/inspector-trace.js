function values(value) {
  return Array.isArray(value) ? value : [];
}

function safe(value, fallback = 'Not available', maximum = 360) {
  if (value == null || value === '') return fallback;
  return String(value).replace(/[\u0000-\u001f\u007f]/g, ' ').slice(0, maximum);
}

function node(tag, className, text) {
  const element = document.createElement(tag);
  if (className) element.className = className;
  if (text != null) element.textContent = text;
  return element;
}

function section(root, title, className = '') {
  const element = node('section', `df-trace-section ${className}`.trim());
  element.append(node('h4', null, title));
  root.append(element);
  return element;
}

function line(parent, label, value, className = '') {
  const element = node('p', `df-trace-line ${className}`.trim());
  element.append(node('strong', null, `${label}: `), document.createTextNode(safe(value)));
  parent.append(element);
}

function list(parent, entries, empty = 'No details were retained.') {
  if (!entries.length) {
    parent.append(node('p', 'df-workbench-note df-workbench-note-muted', empty));
    return;
  }
  const listElement = node('ul', 'df-workbench-list');
  for (const entry of entries) listElement.append(node('li', null, safe(entry)));
  parent.append(listElement);
}

function action(parent, text, callback, disabled = false, className = '') {
  const button = node('button', `df-workbench-action ${className}`.trim(), text);
  button.type = 'button';
  button.disabled = disabled;
  if (!disabled && typeof callback === 'function') button.addEventListener('click', callback);
  parent.append(button);
  return button;
}

export function shouldShowTraceImport(state) {
  return state?.importEnabled === true &&
    !state.run &&
    !state.report &&
    state.mode !== 'imported';
}

function time(value) {
  const parsed = Date.parse(value || '');
  return Number.isFinite(parsed) ? new Date(parsed).toLocaleTimeString() : 'time unavailable';
}

function duration(step) {
  if (Number.isFinite(Number(step?.durationMs))) return `${Math.max(0, Number(step.durationMs))} ms`;
  const start = Date.parse(step?.startedAt || '');
  const end = Date.parse(step?.endedAt || '');
  return Number.isFinite(start) && Number.isFinite(end) && end >= start ? `${end - start} ms` : 'Not measured';
}

function runDuration(snapshot, report) {
  const started = snapshot?.startedAt || snapshot?.createdAt || report?.startedAt;
  const ended = snapshot?.endedAt || report?.endedAt;
  const start = Date.parse(started || '');
  const end = Date.parse(ended || '');
  if (!Number.isFinite(start) || !Number.isFinite(end) || end < start) return 'Not measured';
  const milliseconds = end - start;
  return milliseconds >= 1000 ? `${(milliseconds / 1000).toFixed(milliseconds % 1000 ? 1 : 0)} s` : `${milliseconds} ms`;
}

function completedSteps(report) {
  const steps = values(report?.steps);
  const passed = steps.filter((step) => !step?.failureClass &&
    !(report?.failure?.stepId && report.failure.stepId === step?.stepId)).length;
  return { passed, total: steps.length };
}

function outcomeFor(step, report) {
  if (!step) return 'unknown';
  if (step.failureClass) return step.failureClass;
  if (report?.failure?.stepId && report.failure.stepId === step.stepId) return report.failure.class || report.failure.code || 'failed';
  return 'completed';
}

/**
 * Finds the first reliable divergence without treating arbitrary imported IDs as local identity.
 */
export function firstDivergenceStepId(report) {
  if (!report || typeof report !== 'object') return null;
  if (typeof report.divergenceStepId === 'string' && report.divergenceStepId) return report.divergenceStepId;
  if (typeof report.failure?.stepId === 'string' && report.failure.stepId) return report.failure.stepId;
  const failed = values(report.steps).find((step) => step?.failureClass);
  return typeof failed?.stepId === 'string' ? failed.stepId : null;
}

/**
 * An ambiguous locator is deliberately distinct from a missing locator: choosing a candidate
 * would hide a regression or send an action to the wrong control.
 */
export function isAmbiguousLocatorFailure(report) {
  if (!report || typeof report !== 'object') return false;
  const stepId = firstDivergenceStepId(report);
  const step = values(report.steps).find((candidate) => candidate?.stepId === stepId);
  return [
    report.failure?.class,
    report.failure?.code,
    report.failure?.legacyKind,
    report.failureKind,
    report.failureClass,
    step?.failureClass,
    step?.failureKind,
    step?.targetResolution?.status,
  ].some((value) => {
    const normalized = String(value || '').trim().toLowerCase();
    return normalized === 'locator-ambiguous' || normalized === 'ambiguous';
  });
}

export function ambiguityMatchCount(report) {
  if (!report || typeof report !== 'object') return null;
  const stepId = firstDivergenceStepId(report);
  const step = values(report.steps).find((candidate) => candidate?.stepId === stepId);
  const candidates = [
    report.failure?.matchCount,
    step?.matchCount,
    step?.candidateCount,
    step?.targetResolution?.matchCount,
    step?.candidateSummary?.count,
  ];
  const count = candidates.find((value) => Number.isInteger(Number(value)) && Number(value) > 1);
  return count == null ? null : Number(count);
}

/**
 * Formats only the disclosure envelope. It intentionally never reads or displays the raw value.
 */
export function disclosureText(disclosure) {
  if (!disclosure || typeof disclosure !== 'object') return 'not disclosed';
  const pieces = [safe(disclosure.state, 'redacted', 48)];
  if (disclosure.type) pieces.push(`type ${safe(disclosure.type, '', 48)}`);
  if (Number.isFinite(Number(disclosure.length))) pieces.push(`length ${Math.max(0, Number(disclosure.length))}`);
  if (disclosure.digest) pieces.push(`digest ${safe(disclosure.digest, '', 80)}`);
  return pieces.join(' · ');
}

export function importedTrustPresentation(status) {
  const verification = status?.verification || {};
  const state = verification.state === 'locally-reproduced'
    ? 'locally-reproduced'
    : verification.state === 'attested'
      ? 'attested'
      : 'untrusted';
  const explanation = state === 'locally-reproduced'
    ? 'A separate new local run matched the broker’s current matching facts. This still does not create or apply a proposal.'
    : state === 'attested'
      ? 'Producer provenance was independently attested, but this artifact remains diagnostic-only and is not repair-authoritative.'
      : 'This artifact is untrusted diagnostic input. It cannot execute, mutate the app, or create a proposal.';
  return { state, explanation, reasons: values(verification.reasons) };
}

export function passPresentation(report) {
  const independentOracleSucceeded = values(report?.businessOracles).some((oracle) =>
    oracle?.independent === true && oracle?.succeeded === true);
  const independentlyVerified = report?.verification?.verified === true &&
    report?.replayEligibility?.runVerificationAllowed === true &&
    independentOracleSucceeded;
  return independentlyVerified
    ? {
      title: 'Test passed (independently verified)',
      classification: 'Independently verified pass',
    }
    : {
      title: 'Test passed (replay only)',
      classification: 'Replay pass',
    };
}

function renderStepRail(root, report, state, controller) {
  const rail = section(root, 'Ordered steps', 'df-trace-rail-section');
  const steps = values(report?.steps);
  if (!steps.length) {
    rail.append(node('p', 'df-workbench-note', 'No ordered step attempts were retained in this report.'));
    return;
  }

  const selected = state.selectedStepId || firstDivergenceStepId(report) || steps[0]?.stepId;
  const ordered = node('ol', 'df-trace-rail');
  for (const step of steps) {
    const item = node('li', `df-trace-rail-item${step?.stepId === selected ? ' df-trace-selected' : ''}`);
    const outcome = outcomeFor(step, report);
    const button = node(
      'button',
      'df-trace-step',
      `${step?.sequence ?? '?'} · ${safe(step?.action, 'step', 72)} · ${outcome}`
    );
    button.type = 'button';
    button.dataset.traceStep = step?.stepId || '';
    button.setAttribute('aria-current', String(step?.stepId === selected));
    button.setAttribute(
      'aria-label',
      `Step ${step?.sequence ?? '?'} ${safe(step?.action, 'step', 72)}, ${outcome}, ${duration(step)}`
    );
    button.addEventListener('click', () => controller?.selectStep?.(step?.stepId));
    item.append(button);
    ordered.append(item);
  }
  rail.append(ordered);
  const controls = node('div', 'df-trace-step-controls');
  const previous = action(controls, 'Previous step [', () => controller?.previousStep?.(), !controller?.canMove?.(-1));
  previous.setAttribute('aria-keyshortcuts', '[');
  const next = action(controls, 'Next step ]', () => controller?.nextStep?.(), !controller?.canMove?.(1));
  next.setAttribute('aria-keyshortcuts', ']');
  rail.append(controls);
}

function renderSelector(sectionElement, step) {
  const selector = step?.selectorRequest || {};
  const resolution = step?.targetResolution || {};
  line(sectionElement, 'Request kind', selector.kind || step?.selector?.automationId ? 'selector' : 'Not retained');
  line(sectionElement, 'Request scope', selector.scope);
  line(sectionElement, 'Request disclosure', disclosureText(selector.value));
  line(sectionElement, 'Match count', step?.candidateCount ?? resolution.matchCount ?? step?.candidateSummary?.count);
  line(sectionElement, 'Resolution', resolution.finalResolution || resolution.status);
  line(sectionElement, 'Fingerprint summary', step?.candidateSummary?.final || values(step?.candidateSummary?.types).join(', ') || 'Not retained');
}

function renderSelectorEvidence(sectionElement, step) {
  const fingerprint = step?.fingerprint || null;
  const candidates = values(step?.selectorCandidates);
  const omissions = values(step?.selectorCandidateOmissions);
  if (!fingerprint && !candidates.length && !omissions.length) {
    sectionElement.append(node('p', 'df-workbench-note df-workbench-note-muted',
      'No value-free fingerprint or deterministic candidate evidence was retained.'));
    return;
  }
  if (fingerprint) {
    line(sectionElement, 'Fingerprint', fingerprint.fingerprintId);
    line(sectionElement, 'Source state', fingerprint.source?.state);
    line(sectionElement, 'Topology evidence', fingerprint.topology?.ancestorHash || fingerprint.topology?.siblingHash);
    line(sectionElement, 'Collection scope', fingerprint.collection?.scope);
  }
  for (const candidate of candidates) {
    const card = node('div', 'df-trace-artifact');
    line(card, 'Candidate', candidate?.candidateId);
    line(card, 'Priority / deterministic rank', `${candidate?.priority ?? '?'} / ${candidate?.rank ?? '?'}`);
    line(card, 'Selector kind', candidate?.selectorDescriptor?.kind ||
      (candidate?.selector?.automationId ? 'automation-id' : 'not retained'));
    line(card, 'Unique', candidate?.validation?.unique === true ? 'yes' : candidate?.validation?.unique === false ? 'no' : 'not validated');
    line(card, 'Calibration', candidate?.calibration?.state || candidate?.calibrationStatus || 'uncalibrated');
    line(card, 'Rule version', candidate?.scores?.ruleVersion || 'selector-ranker-v1');
    line(card, 'Deterministic rank score', candidate?.scores?.deterministicRankScore ?? candidate?.score);
    const components = candidate?.scores || candidate?.scoreComponents;
    if (components && typeof components === 'object') {
      const values = Object.entries(components)
        .filter(([key]) => key !== 'ruleVersion' && key !== 'deterministicRankScore')
        .map(([key, value]) => `${safe(key, 'component', 64)}=${safe(value, '', 32)}`);
      if (values.length) line(card, 'Score components', values.join(' · '));
    }
    if (values(candidate?.riskFlags).length) line(card, 'Risk flags', values(candidate.riskFlags).join(', '));
    if (values(candidate?.rationaleCodes).length) line(card, 'Rationale', values(candidate.rationaleCodes).join(', '));
    sectionElement.append(card);
  }
  if (omissions.length) {
    list(sectionElement, omissions.map((omission) =>
      `${safe(omission?.kind, 'omission')}: ${safe(omission?.reason, '')}`));
  }
}

function renderActionability(sectionElement, step) {
  const ladder = values(step?.actionability);
  if (!ladder.length) {
    sectionElement.append(node('p', 'df-workbench-note df-workbench-note-muted', 'No actionability ladder was retained.'));
    return;
  }
  const listElement = node('ol', 'df-trace-ladder');
  for (const attempt of ladder) {
    const facts = [
      `attempt ${attempt?.attempt ?? attempt?.sequence ?? '?'}`,
      attempt?.resolved === false ? 'unresolved' : attempt?.resolved === true ? 'resolved' : null,
      attempt?.visible === false ? 'not visible' : attempt?.visible === true ? 'visible' : null,
      attempt?.enabled === false ? 'disabled' : attempt?.enabled === true ? 'enabled' : null,
      attempt?.boundsStable === false ? 'unstable bounds' : attempt?.boundsStable === true ? 'stable bounds' : null,
      attempt?.outcome,
    ].filter(Boolean);
    listElement.append(node('li', null, facts.join(' · ')));
  }
  sectionElement.append(listElement);
}

function renderAssertions(sectionElement, step) {
  const assertions = values(step?.assertions);
  if (!assertions.length) {
    sectionElement.append(node('p', 'df-workbench-note df-workbench-note-muted', 'No assertion result was retained for this step.'));
    return;
  }
  for (const assertion of assertions) {
    const card = node('div', `df-trace-assertion${assertion?.passed === false ? ' df-trace-failure' : ''}`);
    line(card, 'Kind', assertion?.kind);
    line(card, 'Outcome', assertion?.skipped ? 'skipped' : assertion?.passed === true ? 'passed' : assertion?.passed === false ? 'failed' : 'unknown');
    line(card, 'Expected disclosure', disclosureText(assertion?.expectedDisclosure));
    line(card, 'Actual disclosure', disclosureText(assertion?.actualDisclosure));
    if (assertion?.message) line(card, 'Safe comparison note', assertion.message);
    sectionElement.append(card);
  }
}

function renderResultsSummary(root, helpers, state, controller, snapshot, report) {
  const outcome = report.outcome?.status || snapshot.state || 'unknown';
  const terminal = snapshot.terminal === true || ['passed', 'failed', 'cancelled', 'timed-out', 'lease-lost', 'infrastructure-error', 'unknown-completion', 'orphaned'].includes(outcome);
  const failed = terminal && outcome !== 'passed';
  const steps = completedSteps(report);
  const pass = outcome === 'passed' ? passPresentation(report) : null;
  const classification = pass?.classification || report.failure?.class || report.failure?.code || outcome;
  const banner = section(root, pass?.title || (terminal ? 'Test needs attention' : 'Run in progress'),
    `df-results-banner ${outcome === 'passed' ? 'df-results-banner-pass' : terminal ? 'df-results-banner-fail' : ''}`);
  banner.id = 'df-results-summary';
  banner.tabIndex = -1;
  banner.setAttribute('role', 'status');
  line(banner, 'Outcome', outcome);
  line(banner, 'Steps', `${steps.passed}/${steps.total} passed`);
  line(banner, 'Duration', runDuration(snapshot, report));
  line(banner, 'Classification', classification);

  const actions = node('div', 'df-results-actions');
  if (!terminal) {
    actions.append(node('p', 'df-authoring-field-hint', 'Wait for the run to finish, or return to Run to cancel it.'));
  } else if (!failed) {
    action(actions, 'Run again', () => controller?.runAgain?.(), false, 'df-authoring-primary');
    action(actions, 'Improve test', () => helpers.selectTab?.('improve'));
  } else {
    if (isAmbiguousLocatorFailure(report)) {
      const count = ambiguityMatchCount(report);
      action(
        actions,
        count ? `Resolve ${count} matches` : 'Resolve matches',
        () => helpers.improve?.resolveAmbiguity?.(),
        !helpers.improve?.resolveAmbiguity,
        'df-authoring-primary'
      );
      action(actions, 'View failed step', () => controller?.focusResults?.(), !firstDivergenceStepId(report));
      banner.append(node(
        'p',
        'df-workbench-safety',
        'DevFlow will not choose automatically because that could hide an app regression or tap the wrong control. Resolve the matches in Improve.'
      ));
    } else {
      action(actions, 'View failed step', () => controller?.focusResults?.(), !firstDivergenceStepId(report), 'df-authoring-primary');
      const repairEligible = helpers.repair?.state?.().eligibility?.eligible === true ||
        (report.failure?.repairEligible === true &&
          report.replayEligibility?.repairEligibility === true);
      if (repairEligible && helpers.featureEnabled?.('repair')) {
        action(actions, 'Review repair', () => helpers.selectTab?.('repair'));
      }
      else if (report.failure?.category === 'selector' ||
        ['locator-not-found', 'locator-ambiguous'].includes(report.failure?.class || report.failure?.code)) {
        action(actions, 'Improve selector', () => helpers.selectTab?.('improve'));
      }
    }
  }
  banner.append(actions);
  if (failed) {
    helpers.agentAction?.(banner, {
      title: 'Diagnose this failure with your agent',
      description: 'Send an exact, time-limited handoff so your agent can explain this run without searching for files, sessions, or tokens.',
      prompt: () => helpers.run?.prepareFailureAgentPrompt?.(),
    });
  }
}

function renderLocalTrace(root, helpers, state, controller) {
  const snapshot = state.run || {};
  const report = snapshot.report || state.report || {};
  helpers.intro(root, 'Review the outcome, then choose the next action. Technical details stay available below.');
  renderResultsSummary(root, helpers, state, controller, snapshot, report);
  if (state.imported) {
    const reproduction = section(root, 'Separate local reproduction');
    reproduction.append(node('p', 'df-workbench-note',
      'This live run remains separate from the imported diagnostic artifact.'));
    action(reproduction, 'Return to imported trace', () => controller?.showImported?.());
    if (state.reproduction?.candidateRunId) {
      action(
        reproduction,
        'Verify matching local reproduction',
        () => controller?.verifyReproduction?.(),
        state.reproduction.canBind !== true
      );
      if (state.reproduction.canBind !== true) {
        reproduction.append(node('p', 'df-authoring-field-hint',
          safe(state.reproduction.unavailableReason, 'Current source matching facts are unavailable in this host.')));
      }
    }
  }

  const technical = node('details', 'df-trace-details');
  technical.append(node('summary', null, 'Technical trace details'));
  root.append(technical);
  renderStepRail(technical, report, state, controller);
  const selectedId = state.selectedStepId || firstDivergenceStepId(report);
  const selected = values(report.steps).find((step) => step?.stepId === selectedId) || values(report.steps)[0];
  if (selected) {
    const detail = section(technical, `Step ${selected.sequence ?? '?'} detail`);
    line(detail, 'Action', selected.action);
    line(detail, 'Intent', selected.intent);
    line(detail, 'Timing', duration(selected));
    line(detail, 'Outcome', outcomeFor(selected, report));
    line(detail, 'Failure class', selected.failureClass || 'None');

    const selector = section(detail, 'Selector and target resolution');
    renderSelector(selector, selected);
    const selectorEvidence = section(detail, 'Fingerprint and deterministic candidate evidence');
    renderSelectorEvidence(selectorEvidence, selected);
    const actionability = section(detail, 'Actionability attempt ladder');
    renderActionability(actionability, selected);
    const receipt = section(detail, 'Command receipt and completion certainty');
    const dispatch = selected.dispatch || {};
    line(receipt, 'Command ID', dispatch.commandId || selected.commandId);
    line(receipt, 'Sequence', dispatch.sequence ?? selected.commandSequence);
    line(receipt, 'Acknowledgement', dispatch.acknowledgementState || selected.acknowledgementState);
    line(receipt, 'Completion certainty', dispatch.completionCertainty || selected.completionCertainty);
    line(receipt, 'Authority epoch', dispatch.authorityEpoch ?? selected.authorityEpoch);
    const assertions = section(detail, 'Assertions');
    renderAssertions(assertions, selected);
  }

  const safety = section(technical, 'Reset, preconditions, and oracle facts');
  line(safety, 'Reset', report.reset?.succeeded === true ? 'reported successful' : report.reset?.requested === true ? 'not proven successful' : 'not requested or not retained');
  line(safety, 'Expected checkpoint', report.preconditions?.expected?.route || report.preconditions?.expected?.appBuildFingerprint || 'Not retained');
  line(safety, 'Observed checkpoint', report.preconditions?.observed?.route || report.preconditions?.observed?.appBuildFingerprint || 'Not retained');
  list(safety, values(report.businessOracles).map((oracle) =>
    `${safe(oracle?.oracleId, 'Unnamed oracle')}: ${oracle?.succeeded === true ? 'passed' : oracle?.succeeded === false ? 'failed' : 'not observed'}`),
  'No independent oracle result was retained.');

  const evidence = section(technical, 'Correlated artifacts and evidence');
  const artifacts = [
    ...values(report.artifacts),
    ...values(selected?.artifacts),
    ...values(report.failure?.artifacts),
  ];
  if (!artifacts.length) {
    evidence.append(node('p', 'df-workbench-note df-workbench-note-muted', 'No linked artifact was retained.'));
  } else {
    for (const artifact of artifacts) {
      const card = node('div', 'df-trace-artifact');
      line(card, 'Kind', artifact?.kind);
      line(card, 'Digest', artifact?.digest);
      line(card, 'Redacted', artifact?.redacted === true ? 'yes' : 'not stated');
      if (artifact?.kind === 'mauitrace') {
        line(card, 'Manifest run-report link', report.reportDigest || snapshot.reportDigest || 'retained in the linked report');
        line(card, 'Capture completeness', 'failure-only redacted evidence');
        action(card, 'Download linked .mauitrace v1', () => controller?.downloadEvidence?.(), !controller?.hasDownloadableEvidence?.());
        card.append(node('p', 'df-authoring-field-hint',
          'Failure evidence is redacted by default. Screenshot and flow text are attached only when explicitly approved in the run check.'));
      }
      if (artifact?.kind === 'device-recording') {
        line(card, 'Capture completeness', 'full-run device recording');
        action(
          card,
          'Download device recording',
          () => controller?.downloadDeviceRecording?.(),
          !controller?.hasDownloadableDeviceRecording?.()
        );
        card.append(node('p', 'df-authoring-field-hint',
          'This unredacted MP4 was captured only after explicit run-check approval.'));
      }
      evidence.append(card);
    }
  }
  const omissions = [
    ...values(report.omissions).map((omission) => `${safe(omission?.kind, 'omission')}: ${safe(omission?.reason, '')}`),
    ...(report.truncated ? [`Report truncation: ${safe(report.truncationReason)}`] : []),
  ];
  if (omissions.length) {
    const omissionSection = section(technical, 'Omissions and truncation notices', 'df-trace-omissions');
    list(omissionSection, omissions);
  }
}

function renderImportedTrace(root, helpers, state, controller) {
  const artifact = state.imported || {};
  const status = artifact.status || {};
  const projection = artifact.projection || {};
  const trust = importedTrustPresentation(status);
  helpers.intro(root, 'Captured imported artifacts are rendered as bounded diagnostic projections. Raw bytes, embedded run IDs, screenshots, and source are not retained.');

  const banner = section(root, `Imported artifact: ${trust.state}`, `df-trace-trust df-trace-trust-${trust.state}`);
  banner.setAttribute('role', 'status');
  banner.append(node('p', null, trust.explanation));
  line(banner, 'Isolated identity', status.identity?.namespace === 'imported-artifact' ? status.identity?.id : 'Invalid imported-artifact identity');
  line(banner, 'Artifact kind', status.artifactKind);
  line(banner, 'Raw content retained', status.rawContentRetained === true ? 'yes' : 'no');
  list(banner, trust.reasons.map((reason) => reason?.message || reason?.code), 'No provenance reason was retained.');

  const facts = section(root, 'Safe diagnostic projection');
  line(facts, 'Source schema', projection.sourceSchema);
  line(facts, 'Outcome', projection.outcome);
  line(facts, 'Failure class', projection.failure?.class || projection.failure?.code);
  line(facts, 'Flow fingerprint', projection.flowFingerprint);
  line(facts, 'Build fingerprint', projection.appBuildFingerprint);
  line(facts, 'Platform fingerprint', projection.platformFingerprint);
  line(facts, 'Completeness', projection.truncated ? 'truncated projection' : 'bounded projection');
  const omissions = values(projection.omissions).map((omission) =>
    `${safe(omission?.field, 'omission')}: ${safe(omission?.reason, '')}`);
  if (omissions.length) list(facts, omissions);

  const controls = section(root, 'Read-only imported mode');
  controls.append(node('p', 'df-workbench-safety',
    'Interact, Record, Run, mutations, property/source application, takeover, CDP evaluation, and effectful Data actions are disabled while this imported trace is selected.'));
  action(
    controls,
    'Reproduce locally',
    () => controller?.reproduceLocally?.(),
    state.reproductionOpening === true,
    'df-authoring-primary'
  );
  controls.append(node('p', 'df-authoring-field-hint',
    'This opens a separate live run check using the current flow and target. It does not replay or trust the imported artifact automatically.'));
  if (state.reproduction) {
    line(controls, 'Local reproduction binding', state.reproduction.binding?.matched === true ? 'matched' : 'not established');
    list(controls, values(state.reproduction.verification?.reasons).map((reason) => reason?.message || reason?.code));
    if (state.reproduction.candidateRunId) {
      action(
        controls,
        'Verify matching local reproduction',
        () => controller?.verifyReproduction?.(),
        state.reproduction.canBind !== true
      );
    }
  }
}

export function resultsNextStep(readiness = {}) {
  if (readiness.goal !== true) {
    return {
      stage: 'goal',
      label: 'Go to Goal',
      message: 'Add the Goal before continuing to Run.',
    };
  }
  if (readiness.recordedSteps !== true) {
    return {
      stage: 'record',
      label: 'Go to Steps',
      message: 'Record the steps that prove the Goal before continuing to Run.',
    };
  }
  if (readiness.hardOutcomeCheck !== true || readiness.savedBundle !== true) {
    return {
      stage: 'review',
      label: 'Go to Review',
      message: 'Add an expected result and save the reviewed test before continuing to Run.',
    };
  }
  return {
    stage: 'run',
    label: 'Go to Run',
    message: 'The reviewed test is ready to run.',
  };
}

export function renderTracePanel(helpers) {
  const root = helpers.root('Results');
  const controller = helpers.trace;
  const state = controller?.state?.() || {};
  if (state.mode === 'imported') renderImportedTrace(root, helpers, state, controller);
  else if (state.run || state.report) renderLocalTrace(root, helpers, state, controller);
  else if (state.mode === 'reproduction' && state.imported) {
    helpers.intro(root, 'A local run check is ready. Review Run and choose Review and start when you are ready.');
    action(root, 'Return to imported trace', () => controller?.showImported?.());
  }
  else {
    const next = resultsNextStep(helpers.authoring?.state?.().readiness);
    helpers.intro(root, next.message);
    action(
      root,
      next.label,
      () => next.stage === 'goal' ? helpers.focusGoal?.() : helpers.selectStage?.(next.stage),
      false,
      'df-authoring-primary'
    );
  }
  if (shouldShowTraceImport(state)) {
    const importControls = node('details', 'df-trace-import');
    importControls.append(node('summary', null, 'Open a result from another run'));
    helpers.capability(importControls, 'pickTrace');
    action(importControls, state.importing ? 'Opening…' : 'Choose result file', () => controller?.pickTrace?.(), state.importing);
    importControls.append(node('p', 'df-authoring-field-hint',
      'Use this to inspect a result produced elsewhere without running or changing the connected app.'));
    root.append(importControls);
  }
  helpers.studyEvidenceCard?.(root);
  helpers.safety(root);
  return root;
}
