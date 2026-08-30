function values(value) {
  return Array.isArray(value) ? value : [];
}

function safe(value, fallback = 'Not available', maximum = 360) {
  if (value == null || value === '') return fallback;
  return String(value).replace(/[\u0000-\u001f\u007f]/g, ' ').slice(0, maximum);
}

const MAX_AMBIGUITY_MATCHES = 20;

function safeOptional(value, maximum = 256) {
  if (value == null) return null;
  const result = String(value).replace(/[\u0000-\u001f\u007f]/g, ' ').trim().slice(0, maximum);
  return result || null;
}

function safeBoolean(value) {
  return value === true ? true : value === false ? false : null;
}

function safeBounds(value) {
  if (!value || typeof value !== 'object') return null;
  const coordinate = (name) => {
    const number = Number(value[name]);
    return Number.isFinite(number) ? number : null;
  };
  const bounds = {
    x: coordinate('x'),
    y: coordinate('y'),
    width: coordinate('width'),
    height: coordinate('height'),
  };
  return Object.values(bounds).some((coordinate) => coordinate !== null) ? bounds : null;
}

function safeSelectorKind(value) {
  return {
    automationId: 'AutomationId',
    text: 'Exact text',
    typeIndex: 'Type + index',
    runtimeId: 'Legacy runtime ID',
  }[String(value || '')] || 'Selector';
}

function safeAmbiguityMatch(value) {
  const match = value && typeof value === 'object' ? value : {};
  const hasSource = match.hasSource === true;
  const line = Number(match.sourceLine);
  return {
    id: safeOptional(match.id),
    type: safeOptional(match.type, 128),
    role: safeOptional(match.role, 128),
    automationId: safeOptional(match.automationId, 256),
    isVisible: safeBoolean(match.isVisible),
    isEnabled: safeBoolean(match.isEnabled),
    bounds: safeBounds(match.bounds),
    windowBounds: safeBounds(match.windowBounds),
    hasSource,
    sourceLine: hasSource && Number.isInteger(line) && line > 0 ? line : null,
  };
}

/**
 * Retains only the safe fields returned by selector verification. This is intentionally a second
 * boundary: rendering must not begin exposing a future endpoint field by accident.
 */
export function normalizeAmbiguityContext(value) {
  const context = value && typeof value === 'object' ? value : {};
  const allMatches = values(context.matches);
  const matches = allMatches.slice(0, MAX_AMBIGUITY_MATCHES).map(safeAmbiguityMatch);
  const requestedTotal = Number(context.totalCount);
  const totalCount = Number.isInteger(requestedTotal) && requestedTotal >= matches.length
    ? requestedTotal
    : matches.length;
  return {
    stepId: safeOptional(context.stepId, 128),
    stepSequence: Number.isInteger(Number(context.stepSequence)) && Number(context.stepSequence) > 0
      ? Number(context.stepSequence)
      : null,
    selectorKind: safeSelectorKind(context.selectorKind),
    totalCount,
    truncated: context.truncated === true || allMatches.length > MAX_AMBIGUITY_MATCHES || totalCount > matches.length,
    matches,
  };
}

/**
 * A returned AutomationId is only a candidate. A later canonical verification must still prove
 * that it resolves to exactly one live element before the human-selected draft update is allowed.
 */
export function isUniqueReturnedAutomationId(match, matches, truncated = false) {
  const automationId = safeOptional(match?.automationId, 256);
  if (!automationId || truncated === true) return false;
  return values(matches)
    .filter((candidate) => safeOptional(candidate?.automationId, 256) === automationId)
    .length === 1;
}

export function hasUniqueReturnedAutomationId(context) {
  const normalized = normalizeAmbiguityContext(context);
  return normalized.matches.some((match) =>
    isUniqueReturnedAutomationId(match, normalized.matches, normalized.truncated));
}

function severityOrder(value) {
  return value === 'error' ? 0 : value === 'warning' ? 1 : 2;
}

function create(tag, className, text) {
  const element = document.createElement(tag);
  if (className) element.className = className;
  if (text != null) element.textContent = text;
  return element;
}

function button(text, action, disabled = false) {
  const element = create('button', 'df-workbench-action', text);
  element.type = 'button';
  element.disabled = disabled;
  if (!disabled && typeof action === 'function') element.addEventListener('click', action);
  return element;
}

function normalizeFinding(value) {
  const finding = value && typeof value === 'object' ? value : {};
  return {
    diagnosticId: safe(finding.diagnosticId, 'DFSH'),
    findingId: safe(finding.findingId, 'finding'),
    severity: ['error', 'warning', 'info'].includes(finding.severity) ? finding.severity : 'info',
    category: safe(finding.category, 'general', 64),
    stepId: finding.stepId == null ? null : safe(finding.stepId, '', 80),
    source: finding.source == null ? null : safe(finding.source, '', 360),
    platforms: values(finding.platforms).map((platform) => safe(platform, '', 96)).filter(Boolean),
    message: safe(finding.message, 'No deterministic rationale was retained.'),
    rationaleCodes: values(finding.rationaleCodes).map((code) => safe(code, '', 96)).filter(Boolean),
    evidenceRefs: values(finding.evidenceRefs).map((reference) => safe(reference, '', 128)).filter(Boolean),
  };
}

export function groupFindings(findings) {
  const groups = new Map();
  for (const raw of values(findings)) {
    const finding = normalizeFinding(raw);
    const key = `${finding.severity}|${finding.category}`;
    if (!groups.has(key)) groups.set(key, { severity: finding.severity, category: finding.category, findings: [] });
    groups.get(key).findings.push(finding);
  }
  return [...groups.values()]
    .map((group) => ({
      ...group,
      findings: group.findings.sort((left, right) =>
        severityOrder(left.severity) - severityOrder(right.severity) ||
        left.diagnosticId.localeCompare(right.diagnosticId) ||
        String(left.stepId || '').localeCompare(String(right.stepId || '')) ||
        left.findingId.localeCompare(right.findingId)),
    }))
    .sort((left, right) =>
      severityOrder(left.severity) - severityOrder(right.severity) ||
      left.category.localeCompare(right.category));
}

export function filterFindings(findings, filters = {}) {
  return values(findings).map(normalizeFinding).filter((finding) => {
    if (filters.severity && finding.severity !== filters.severity) return false;
    if (filters.category && finding.category !== filters.category) return false;
    if (filters.step && finding.stepId !== String(filters.step)) return false;
    if (filters.platform && !finding.platforms.includes(filters.platform)) return false;
    return true;
  });
}

export function isImproveStale(state) {
  return state?.stale === true || (!!state?.analysis && state?.inputKey && state.inputKey !== state.currentKey);
}

function optionValues(findings, property) {
  const found = new Set();
  for (const finding of values(findings).map(normalizeFinding)) {
    if (property === 'platform') finding.platforms.forEach((platform) => found.add(platform));
    else if (finding[property]) found.add(String(finding[property]));
  }
  return [...found].sort((left, right) => left.localeCompare(right));
}

function filterControl(label, key, findings, value, update) {
  const row = create('label', 'df-authoring-field');
  row.append(create('span', 'df-authoring-field-label', label));
  const select = document.createElement('select');
  select.className = 'df-authoring-select';
  select.append(new Option(`All ${label.toLowerCase()}s`, ''));
  for (const entry of optionValues(findings, key)) select.append(new Option(entry, entry));
  select.value = value || '';
  select.addEventListener('change', () => update({ [key]: select.value || null }));
  row.append(select);
  return row;
}

function findingCard(finding, helpers, controller) {
  const card = create('article', `df-improve-finding df-improve-${finding.severity}`);
  const heading = create('h4', null, `${finding.diagnosticId} · ${finding.category}`);
  heading.id = `df-improve-${finding.findingId.replace(/[^a-zA-Z0-9_-]/g, '')}`;
  card.append(heading, create('p', 'df-workbench-intro', finding.message));

  const metadata = create('p', 'df-workbench-note');
  metadata.textContent = [
    `severity: ${finding.severity}`,
    finding.stepId ? `step: ${finding.stepId}` : null,
    finding.platforms.length ? `platform: ${finding.platforms.join(', ')}` : null,
  ].filter(Boolean).join(' · ');
  card.append(metadata);

  if (finding.rationaleCodes.length) {
    const rationale = create('p', 'df-authoring-field-hint');
    rationale.textContent = `Rules: ${finding.rationaleCodes.join(', ')}`;
    card.append(rationale);
  }
  if (finding.evidenceRefs.length) {
    const evidence = create('p', 'df-authoring-field-hint');
    evidence.textContent = `Evidence: ${finding.evidenceRefs.join(', ')}`;
    card.append(evidence);
  }

  const links = create('div', 'df-authoring-actions');
  if (finding.stepId) {
    links.append(button('Open Steps', () => helpers.selectTab?.('steps')));
    links.append(button('Open Trace', () => helpers.selectTab?.('trace')));
  }
  if (finding.source) {
    if (helpers.featureEnabled?.('source')) {
      links.append(button('Source anchor', () => controller?.openSource?.(finding.source)));
    } else {
      helpers.agentAction?.(links, {
        buttonLabel: 'Ask agent about source anchor',
        title: 'Ask your agent for a testability plan',
        description: 'Source preview is disabled, so keep this finding visible and request a read-only conversational handoff.',
        prompt: [
          'Use only the restricted DevFlow test-agent tools.',
          `Explain how to improve the testability finding anchored at ${finding.source}.`,
          'Prepare a read-only plan only. Do not edit source, commit, run, or claim that chat approval grants authority.',
        ].join(' '),
      });
    }
  }
  if (links.childElementCount) card.append(links);
  return card;
}

function coverageTable(coverage) {
  const section = create('section', 'df-improve-coverage');
  section.append(create('h4', null, 'Coverage by route and platform'));
  const rows = values(coverage);
  if (!rows.length) {
    section.append(create('p', 'df-workbench-note df-workbench-note-muted', 'No target selector coverage was available.'));
    return section;
  }
  const table = document.createElement('table');
  const header = document.createElement('tr');
  for (const title of ['Platform', 'Route', 'Durable', 'Fragile', 'Missing']) header.append(create('th', null, title));
  table.append(header);
  for (const row of rows) {
    const tr = document.createElement('tr');
    const total = Number(row?.totalTargets) || 0;
    const cells = [
      safe(row?.platform, 'unknown', 80),
      safe(row?.route, 'unknown', 160),
      `${Number(row?.durableTargets) || 0}/${total}`,
      String(Number(row?.fragileTargets) || 0),
      String(Number(row?.missingTargets) || 0),
    ];
    for (const cell of cells) tr.append(create('td', null, cell));
    table.append(tr);
  }
  section.append(table);
  return section;
}

function boundsText(bounds) {
  if (!bounds) return 'not reported';
  const number = (value) => value == null ? '?' : String(value);
  return `x ${number(bounds.x)}, y ${number(bounds.y)}, ${number(bounds.width)} × ${number(bounds.height)}`;
}

function ambiguityButton(text, action, label, disabled = false) {
  const element = button(text, action, disabled);
  element.setAttribute('aria-label', label);
  return element;
}

function ambiguityMatchCard(match, context, controller, helpers) {
  const card = create('article', 'df-ambiguity-match');
  card.append(create('h5', null, `${match.type || 'Element'} · ${match.role || 'role unavailable'}`));
  const facts = [
    `Ephemeral element ID: ${match.id || 'unavailable'}`,
    `Type: ${match.type || 'not reported'}`,
    `Role: ${match.role || 'not reported'}`,
    `AutomationId: ${match.automationId || 'not set'}`,
    `Visible: ${match.isVisible === null ? 'not reported' : match.isVisible ? 'yes' : 'no'}`,
    `Enabled: ${match.isEnabled === null ? 'not reported' : match.isEnabled ? 'yes' : 'no'}`,
    `Bounds: ${boundsText(match.bounds)}`,
    `Window bounds: ${boundsText(match.windowBounds)}`,
    `Source mapping: ${match.hasSource ? `yes${match.sourceLine ? ` · line ${match.sourceLine}` : ''}` : 'no'}`,
  ];
  const factList = create('ul', 'df-workbench-list');
  for (const fact of facts) factList.append(create('li', null, fact));
  card.append(factList);

  const actions = create('div', 'df-authoring-actions');
  actions.append(ambiguityButton(
    'Highlight in app',
    () => controller?.highlightMatch?.(match),
    `Highlight ${match.type || 'element'} in the app`
  ));
  if (isUniqueReturnedAutomationId(match, context.matches, context.truncated)) {
    actions.append(ambiguityButton(
      'Use this AutomationId',
      () => controller?.useAutomationId?.(match),
      `Use this AutomationId for the failed step after global verification`
    ));
  }
  if (!hasUniqueReturnedAutomationId(context) && match.hasSource) {
    if (helpers.featureEnabled?.('source')) {
      actions.append(ambiguityButton(
        'Improve app testability',
        () => controller?.improveTestability?.(match),
        `Select this mapped ${match.type || 'element'} and open a reviewed source proposal`
      ));
    } else {
      helpers.agentAction?.(actions, {
        buttonLabel: 'Ask agent about testability',
        title: 'Ask your agent for a safe testability plan',
        description: 'Source preview is disabled. The current control stays selected and visible while the prompt is prepared.',
        prompt: [
          'Use only the restricted DevFlow test-agent tools.',
          `Explain a read-only testability plan for the mapped ${match.type || 'element'} with no unique AutomationId.`,
          'Do not edit source, change the test, commit, or run. Chat approval is intent only.',
        ].join(' '),
      });
    }
  }
  card.append(actions);
  return card;
}

function ambiguityGuidance(context) {
  if (context.truncated) {
    return 'Only the first 20 matches are shown. Do not infer uniqueness from this list; highlight the intended control, then re-verify a human-selected AutomationId globally, or improve a mapped control’s testability.';
  }
  if (!hasUniqueReturnedAutomationId(context)) {
    return 'No safely unique AutomationId is available from these matches. Highlight the intended control; if it is source-mapped, open a reviewed testability proposal. Do not guess.';
  }
  return 'A displayed AutomationId is only a candidate. DevFlow re-verifies it against the full live tree before changing the draft.';
}

function renderAmbiguityCard(context, controller, helpers) {
  const card = create('section', 'df-ambiguity-card');
  const heading = create('h4', null, 'Ambiguous selector');
  heading.id = 'df-ambiguous-selector-heading';
  card.setAttribute('aria-labelledby', heading.id);
  card.setAttribute('aria-live', 'polite');
  card.append(heading);
  const step = context.stepSequence ? `step ${context.stepSequence}` : context.stepId || 'the failed step';
  card.append(create(
    'p',
    'df-workbench-safety',
    `DevFlow will not choose automatically because that could hide an app regression or tap the wrong control.`
  ));
  const facts = create('ul', 'df-workbench-list');
  for (const fact of [
    `Step: ${step}`,
    `Selector kind: ${context.selectorKind}`,
    `Total matches: ${context.totalCount}`,
    `Match list: ${context.truncated ? 'truncated to 20 safe summaries' : 'complete safe summary set'}`,
  ]) facts.append(create('li', null, fact));
  card.append(facts);
  const matches = create('div', 'df-ambiguity-matches');
  for (const match of context.matches) matches.append(ambiguityMatchCard(match, context, controller, helpers));
  card.append(matches);
  card.append(create('p', 'df-workbench-note', ambiguityGuidance(context)));
  return card;
}

export function renderImprovePanel(helpers) {
  const root = helpers.root('Improve');
  const controller = helpers.improve;
  const state = controller?.state?.() || {};
  const analysis = state.analysis && typeof state.analysis === 'object' ? state.analysis : null;
  const ambiguity = state.ambiguity ? normalizeAmbiguityContext(state.ambiguity) : null;
  const findings = values(analysis?.findings);
  const filters = state.filters || {};
  const visible = filterFindings(findings, filters);
  const stale = isImproveStale(state);

  if (!state.hasFlow && !ambiguity) {
    const empty = create('section', 'df-authoring-section df-tool-empty-state');
    empty.append(create('h4', null, 'No test to scan'));
    empty.append(create(
      'p',
      'df-workbench-intro',
      'Record or open a test first. You can then check it here or ask your agent to review its quality.'
    ));
    empty.append(button('Go to Goal', () => { if (helpers.focusGoal) helpers.focusGoal(); else helpers.selectStage?.('goal'); }));
    root.append(empty);
    return root;
  }

  if (ambiguity) root.append(renderAmbiguityCard(ambiguity, controller, helpers));
  if (state.error) root.append(create('p', 'df-workbench-safety', safe(state.error)));

  const controls = create('section', 'df-authoring-section df-tool-ready-state');
  controls.append(create('h4', null, analysis ? 'Scan summary' : 'Check this test'));
  controls.append(create(
    'p',
    'df-workbench-intro',
    analysis
      ? 'Review the findings below. Nothing in the test changes until you edit or approve it.'
      : 'Look for fragile controls, missing expected results, and incomplete route or platform coverage.'
  ));
  const actionRow = create('div', 'df-authoring-actions');
  const scan = button(
    state.scanning ? 'Scanning…' : analysis ? (stale ? 'Update scan' : 'Scan again') : 'Scan test',
    () => controller?.analyze?.(),
    state.scanning === true
  );
  scan.classList.add('df-authoring-primary');
  actionRow.append(scan);
  controls.append(actionRow);
  controls.append(create(
    'p',
    stale ? 'df-workbench-safety' : 'df-workbench-note',
    stale
      ? 'The test changed after this scan. Update it before relying on the findings.'
      : analysis
        ? `${findings.length} finding${findings.length === 1 ? '' : 's'} · read-only scan`
        : 'The scan is read-only and does not create a repair.'
  ));
  root.append(controls);
  helpers.agentAction?.(root, {
    title: 'Improve this test with your agent',
    description: 'Your agent can run the same read-only quality check and explain the findings in plain language.',
    prompt: [
      'Use only the restricted DevFlow test-agent tools.',
      'Review the currently loaded DevFlow test for fragile controls, missing expected results, and incomplete route or platform coverage.',
      'Summarize the most important findings in plain language and suggest the next safe action.',
      'Do not apply repairs or source changes.',
    ].join(' '),
  });

  const scanOptions = create('details', 'df-tool-details');
  scanOptions.append(create('summary', null, 'Scan options'));
  const live = document.createElement('label');
  live.className = 'df-run-check';
  const checkbox = document.createElement('input');
  checkbox.type = 'checkbox';
  checkbox.checked = state.includeLiveTree !== false;
  checkbox.addEventListener('change', () => controller?.setLiveTree?.(checkbox.checked));
  live.append(checkbox, document.createTextNode(' Include the current live visual tree'));
  scanOptions.append(live);
  if (state.liveTree) {
    scanOptions.append(create(
      'p',
      'df-authoring-field-hint',
      state.liveTree.available
        ? `Live tree: ${state.liveTree.elementCount || 0} structural elements${state.liveTree.truncated ? ' (capped)' : ''}.`
        : 'Live tree facts were unavailable; flow and plan diagnostics remain deterministic.'
    ));
  }
  root.append(scanOptions);
  if (!analysis) return root;

  const result = create('section', 'df-improve-results');
  result.setAttribute('aria-live', 'polite');
  result.append(create('h4', null, `${visible.length} deterministic finding${visible.length === 1 ? '' : 's'}`));
  if (!visible.length) {
    result.append(create('p', 'df-workbench-note df-workbench-note-muted', 'No finding matches the current filters.'));
  } else {
    for (const group of groupFindings(visible)) {
      const groupElement = create('section', `df-improve-group df-improve-${group.severity}`);
      groupElement.append(create('h5', null, `${group.severity} · ${group.category}`));
      for (const finding of group.findings) groupElement.append(findingCard(finding, helpers, controller));
      result.append(groupElement);
    }
  }
  root.append(result);

  if (findings.length > 1) {
    const filtersSection = create('details', 'df-tool-details');
    filtersSection.append(create('summary', null, 'Filter findings'));
    const filterRow = create('div', 'df-improve-filters');
    const update = (patch) => controller?.setFilters?.(patch);
    filterRow.append(
      filterControl('Severity', 'severity', findings, filters.severity, update),
      filterControl('Category', 'category', findings, filters.category, update),
      filterControl('Step', 'stepId', findings, filters.step, (patch) => update({ step: patch.stepId })),
      filterControl('Platform', 'platform', findings, filters.platform, update),
    );
    filtersSection.append(filterRow);
    root.append(filtersSection);
  }

  const coverage = create('details', 'df-tool-details');
  coverage.append(create('summary', null, 'Coverage details'));
  coverage.append(coverageTable(analysis.coverage));
  root.append(coverage);
  return root;
}
