// The original Layout dock's list/detail/coverage experience, adapted to the schema-1 report.
export const LAYOUT_FINDING_LIMIT = 200;
const severityRanks = { info: 0, minor: 1, moderate: 2, serious: 3, critical: 4 };
const confidenceRanks = { low: 0, medium: 1, high: 2, exact: 3 };
const labels = {
  'layout.element-clipped': 'Clipped element',
  'layout.element-outside-window': 'Outside window',
  'layout.content-overflow': 'Content overflow',
  'layout.text-not-fully-rendered': 'Text not fully rendered',
  'layout.interaction-occluded': 'Interaction blocked',
  'layout.visual-occluded': 'Visually covered',
  'layout.geometric-overlap': 'Element overlap',
  'layout.accessibility-visibility-mismatch': 'Accessibility visibility mismatch',
  'layout.visible-zero-area': 'Zero-size visible element',
  'layout.constraint-violation': 'Layout constraint violation',
  'layout.desired-size-constrained': 'Content under size pressure',
  'layout.child-outside-parent': 'Child outside parent',
};
const text = value => typeof value === 'string' ? value : '';
const count = value => Number.isInteger(value) && value >= 0 ? value : 0;
const title = value => text(value).replace(/^./, character => character.toUpperCase());

export function layoutRuleLabel(rule) {
  return labels[rule] || text(rule).replace(/^layout\./, '').replaceAll('-', ' ') || 'Layout finding';
}

export function createLayoutEventTracker() {
  let route = '', lifecycle = '';
  return {
    beginConnection() {
      let first = true, seedTimestamp = '';
      return message => {
        // The adjacent initial lifecycle/route snapshots share a timestamp. A later
        // same-route event (including one with from:null) is real navigation.
        const lifecycleSeed = first && message?.type === 'lifecycle';
        const navigationSeed = !!seedTimestamp && message?.type === 'navigation' &&
          message.data?.from == null && message.timestamp === seedTimestamp;
        first = false;
        seedTimestamp = lifecycleSeed ? text(message.timestamp) : '';
        if (message?.type === 'navigation') {
          const next = text(message.data?.route || message.data?.to);
          const changed = !navigationSeed || (!!route && !!next && route !== next);
          if (next) route = next;
          return changed;
        }
        if (message?.type === 'lifecycle') {
          const next = text(message.data?.state);
          const changed = !lifecycleSeed || next === 'stopped' || (!!lifecycle && !!next && lifecycle !== next);
          if (next) lifecycle = next;
          return changed;
        }
        return ['treeChange', 'themeChange'].includes(message?.type);
      };
    },
  };
}

export function filterLayoutFindings(findings, filters = {}) {
  return (Array.isArray(findings) ? findings : []).filter(finding => {
    if (!finding || typeof finding !== 'object') return false;
    if (!filters.includeSuppressed && finding.suppressed) return false;
    const outcome = filters.outcome || 'all';
    if (outcome === 'actionable' && !['violation', 'incomplete'].includes(finding.outcome)) return false;
    if (outcome !== 'all' && outcome !== 'actionable' && finding.outcome !== outcome) return false;
    if ((severityRanks[finding.severity] ?? 0) < (severityRanks[filters.severity] ?? 0)) return false;
    if ((confidenceRanks[finding.confidence] ?? 0) < (confidenceRanks[filters.confidence] ?? 0)) return false;
    return !filters.rule || text(finding.ruleId).toLowerCase().includes(filters.rule.toLowerCase());
  });
}

export function layoutReportView(report, filters = {}) {
  if (!report || !Array.isArray(report.findings)) return null;
  const summary = report.summary || {};
  const stable = report.snapshot?.stable === true;
  const complete = report.coverage?.overall === 'complete' && stable && count(report.snapshot?.nodeCount) > 0 && !count(summary.incomplete);
  const matching = filterLayoutFindings(report.findings, filters);
  return {
    stable,
    complete,
    label: !stable ? 'Unstable snapshot' : complete ? 'Complete coverage' : 'Partial coverage',
    summary: `${count(summary.violations)} violations, ${count(summary.observations)} observations, ` +
      `${count(summary.incomplete)} incomplete, ${count(summary.passes)} passes, ` +
      `${count(summary.notApplicable)} not applicable, ${count(summary.suppressed)} suppressed, ${count(summary.filtered)} filtered`,
    examined: count(report.snapshot?.nodeCount),
    findings: matching.slice(0, LAYOUT_FINDING_LIMIT),
    matching: matching.length,
    total: report.findings.length,
    truncated: matching.length > LAYOUT_FINDING_LIMIT,
    rules: Array.isArray(report.coverage?.rules) ? report.coverage.rules : [],
    limitations: [
      ...(Array.isArray(report.coverage?.limitations) ? report.coverage.limitations : []),
      ...(text(report.snapshot?.stabilityReason) ? [report.snapshot.stabilityReason] : []),
    ],
    empty: matching.length ? '' : report.findings.length ? 'No findings match these filters' :
      complete ? 'No findings in the evaluated checks' : 'No findings in evaluated checks; coverage is incomplete',
  };
}

export function layoutRecheckMessage(previous, report) {
  if (!report || !Array.isArray(report.findings)) return 'Recheck unavailable; the previous finding has not been resolved.';
  const matches = report.findings.filter(finding => finding.ruleId === previous.ruleId &&
    (finding.element?.id === previous.element?.id ||
     (previous.element?.automationId && finding.element?.automationId === previous.element.automationId &&
      finding.element?.type === previous.element?.type)));
  if (matches.length) return 'The finding is still present in the latest check.';
  return layoutReportView(report)?.complete
    ? 'The finding was not observed in the latest complete check.'
    : 'The finding was not observed, but incomplete coverage cannot confirm resolution.';
}

// Deliberately excludes text evidence, values, arbitrary dictionaries, and report extensions.
export function layoutContextPayload(report, selectedId = null) {
  const candidates = Array.isArray(report?.findings) ? report.findings : [];
  const selected = selectedId ? candidates.filter(finding => finding.id === selectedId) : candidates;
  const element = value => value ? {
    id: value.id, type: value.type, automationId: value.automationId,
    sourceFile: value.sourceFile, sourceLine: value.sourceLine, sourceColumn: value.sourceColumn,
  } : null;
  const sizing = value => value ? Object.fromEntries([
    'arrangedWidth', 'arrangedHeight', 'desiredWidth', 'desiredHeight',
    'minimumWidth', 'minimumHeight', 'maximumWidth', 'maximumHeight',
  ].filter(key => typeof value[key] === 'number' && Number.isFinite(value[key])).map(key => [key, value[key]])) : null;
  return {
    schemaVersion: report?.schemaVersion,
    ruleSetVersion: report?.ruleSetVersion,
    snapshot: report?.snapshot ? {
      id: report.snapshot.id, stable: report.snapshot.stable, nodeCount: report.snapshot.nodeCount,
      capturedAt: report.snapshot.capturedAt, platform: report.snapshot.platform,
    } : null,
    summary: report?.summary ? Object.fromEntries([
      'violations', 'observations', 'incomplete', 'passes', 'notApplicable', 'suppressed', 'filtered',
    ].map(key => [key, count(report.summary[key])])) : null,
    coverage: report?.coverage ? {
      overall: report.coverage.overall,
      rules: (report.coverage.rules || []).map(rule => ({
        ruleId: rule.ruleId, support: rule.support, confidence: rule.confidence, limitations: rule.limitations,
      })),
      limitations: report.coverage.limitations,
    } : null,
    selectedFindingId: selectedId,
    findings: selected.slice(0, 100).map(finding => ({
      id: finding.id, ruleId: finding.ruleId, subtype: finding.subtype,
      outcome: finding.outcome, severity: finding.severity, confidence: finding.confidence,
      message: finding.message, element: element(finding.element),
      relatedElements: (finding.relatedElements || []).map(item => ({ relation: item.relation, element: element(item.element) })),
      sizing: sizing(finding.evidence?.sizing),
      limitations: finding.evidence?.limitations || [],
      suppressed: finding.suppressed === true,
    })),
    truncated: selected.length > 100,
  };
}

export function createLayoutPanel({ document, scan, show, openSource, hasSource = finding => !!finding.element?.sourceFile,
  copy, suppress, selection, defaultRoot, status, changed }) {
  const node = (tag, attributes = {}, ...children) => {
    const result = document.createElement(tag);
    for (const [key, value] of Object.entries(attributes)) {
      if (key === 'text') result.textContent = String(value ?? '');
      else if (key === 'class') result.className = value;
      else if (key === 'click') result.addEventListener('click', value);
      else result.setAttribute(key, String(value));
    }
    for (const child of children) if (child) result.append(child);
    return result;
  };
  const button = (label, action, className = '') =>
    node('button', { type: 'button', class: `df-dock-btn ${className}`, text: label, click: action });
  const root = document.getElementById('df-diagnostics-pane') ||
    node('section', { id: 'df-diagnostics-pane', class: 'df-layout-panel', 'aria-label': 'Layout diagnostics' });
  const bar = node('div', { class: 'df-layout-action-strip', 'aria-label': 'Layout actions' });
  const content = node('div', { class: 'df-layout-content' });
  const filters = { outcome: 'all', severity: 'info', confidence: 'low', rule: '', includeSuppressed: false };
  let report = null, busy = false, error = '', stale = false, active = false, live = false;
  let timer = null, generation = 0, pending = false, subview = 'findings', selected = null;
  let selectedScope = false, profile = 'agent', revision = '', invalidation = 0, policyPath = null;
  const visible = () => active && !document.hidden;
  const rescan = button('Rescan', () => run(), 'df-layout-rescan');
  rescan.id = 'layout-rescan';
  const coverage = button('Not checked', () => { subview = 'coverage'; render(); }, 'df-layout-coverage-button');
  coverage.id = 'layout-coverage';
  const back = button('Findings', () => { subview = 'findings'; render(); }, 'df-layout-back');
  const filterMenu = node('details', { class: 'df-layout-filter-menu' });
  filterMenu.append(node('summary', { class: 'df-dock-btn', text: 'Filters' }));
  const filterBody = node('div', { class: 'df-layout-filter-popover' });
  function select(id, label, values, value, set) {
    const input = node('select', { id, 'aria-label': label, class: 'df-field' });
    for (const [key, text] of values) input.append(node('option', { value: key, text }));
    input.value = value;
    input.addEventListener('change', () => { set(input.value); render(); });
    filterBody.append(node('label', { class: 'df-layout-field', text: label }, input));
  }
  select('diagnostics-filter', 'Outcome filter',
    [['all', 'All findings'], ['actionable', 'Actionable'], ['violation', 'Violations'], ['observation', 'Observations'], ['incomplete', 'Incomplete'], ['pass', 'Passes']],
    filters.outcome, value => { filters.outcome = value; });
  select('diagnostics-severity', 'Minimum severity',
    Object.keys(severityRanks).map(value => [value, title(value)]), filters.severity, value => { filters.severity = value; });
  select('diagnostics-confidence', 'Minimum confidence',
    Object.keys(confidenceRanks).map(value => [value, title(value)]), filters.confidence, value => { filters.confidence = value; });
  const ruleFilter = node('input', { id: 'diagnostics-rule', class: 'df-field', type: 'search', placeholder: 'Filter rule', 'aria-label': 'Filter rule' });
  ruleFilter.addEventListener('input', () => { filters.rule = ruleFilter.value; render(); });
  filterBody.append(ruleFilter);
  const suppressed = node('input', { id: 'diagnostics-suppressed', type: 'checkbox' });
  suppressed.addEventListener('change', () => { filters.includeSuppressed = suppressed.checked; render(); });
  filterBody.append(node('label', { text: 'Include suppressed ' }, suppressed));
  select('layout-profile', 'Scan profile', [['agent', 'Agent'], ['strict', 'Strict'], ['exhaustive', 'Exhaustive']], profile,
    value => { profile = value; invalidate('Scan settings changed.'); });
  const scope = node('input', { id: 'layout-selected-scope', type: 'checkbox' });
  scope.addEventListener('change', () => { selectedScope = scope.checked; invalidate('Scan scope changed.'); render(); });
  filterBody.append(node('label', { text: 'Selected subtree only ' }, scope));
  filterMenu.append(filterBody);
  function sizeFilterPopover() {
    if (!filterMenu.open) return;
    const bounds = root.getBoundingClientRect();
    const top = bar.getBoundingClientRect().bottom - bounds.top + 4;
    filterBody.style.top = `${top}px`;
    filterBody.style.maxHeight = `${Math.max(0, bounds.height - top - 8)}px`;
  }
  filterMenu.addEventListener('toggle', sizeFilterPopover);
  if (document.defaultView?.ResizeObserver) {
    const observer = new document.defaultView.ResizeObserver(sizeFilterPopover);
    observer.observe(root);
    observer.observe(bar);
  }
  const liveInput = node('input', { id: 'layout-live', type: 'checkbox' });
  liveInput.addEventListener('change', () => {
    live = liveInput.checked;
    if (live && visible()) schedule();
    else { clearTimeout(timer); timer = null; }
  });
  const countLabel = node('span', { id: 'diagnostics-summary', class: 'df-layout-count', role: 'status' });
  const spacer = node('span', { class: 'df-layout-spacer' });
  bar.append(back, rescan, coverage, spacer, filterMenu,
    node('label', { class: 'df-layout-live', text: 'Live ' }, liveInput), countLabel);
  root.append(bar, content);

  function invalidate(reason) {
    invalidation++;
    if (report) {
      stale = true;
      show(null);
      changed(report, true);
    }
    if (live && visible()) schedule();
    if (active) { render(); status(reason); }
  }
  function schedule() {
    clearTimeout(timer);
    timer = setTimeout(() => { timer = null; if (visible() && live) run(); }, 350);
  }
  async function run(recheck = null) {
    if (!visible()) return null;
    if (busy) { pending = true; return null; }
    pending = false;
    clearTimeout(timer);
    timer = null;
    const rootId = selectedScope ? selection() : defaultRoot();
    if (selectedScope && !rootId) {
      error = 'Select an element before scanning its subtree.';
      render();
      status(error);
      return null;
    }
    busy = true;
    error = '';
    show(null);
    const epoch = ++generation;
    const startedRevision = revision;
    const startedInvalidation = invalidation;
    const requestedProfile = profile;
    const requestedSelectedScope = selectedScope;
    render();
    try {
      const result = await scan({ rootElementId: rootId, profile: requestedProfile });
      if (epoch !== generation) return null;
      if (!result?.ok || !result.report) throw new Error(result?.error || 'The connected app could not provide layout diagnostics.');
      report = result.report;
      policyPath = text(result.policyFilePath) || null;
      stale = requestedProfile !== profile || requestedSelectedScope !== selectedScope ||
        startedInvalidation !== invalidation ||
        (selectedScope && rootId !== selection()) ||
        (!!startedRevision && !!revision && startedRevision !== revision);
      if (selected) {
        selected = report.findings?.find(finding => finding.id === selected.id) || null;
        if (!selected) subview = 'findings';
      }
      changed(report, stale);
      status(recheck ? layoutRecheckMessage(recheck, report) :
        `Layout check complete. ${layoutReportView(report)?.summary}.`);
      return report;
    } catch (failure) {
      if (epoch === generation) {
        error = failure instanceof Error ? failure.message : 'Layout check failed.';
        stale = !!report;
        changed(report, stale);
        status(error);
      }
      return null;
    } finally {
      if (epoch === generation) {
        busy = false;
        render();
        const repeat = pending && visible();
        pending = false;
        if (repeat) run();
      }
    }
  }
  function pill(label, kind = '') { return node('span', { class: `df-layout-pill df-layout-${kind}`, text: label }); }
  function render() {
    const view = layoutReportView(report, filters);
    rescan.disabled = busy;
    rescan.textContent = busy ? 'Scanning...' : 'Rescan';
    back.hidden = subview === 'findings';
    filterMenu.hidden = subview !== 'findings';
    coverage.textContent = stale ? 'Stale snapshot' : view?.label || 'Not checked';
    coverage.disabled = !view;
    coverage.title = view ? `${view.summary}. ${view.examined} realized nodes. Schema ${report.schemaVersion}, rules ${report.ruleSetVersion}.` : '';
    countLabel.textContent = view ? `${view.matching} ${view.matching === 1 ? 'finding' : 'findings'}` : '';
    countLabel.title = view?.summary || '';
    content.replaceChildren();
    if (error || stale) content.append(node('p', { role: error ? 'alert' : 'status', class: 'df-layout-notice',
      text: error || 'The app changed after this snapshot. Rescan before using these findings.' }));
    if (!view) {
      content.append(node('div', { class: 'df-empty', text: busy ? 'Checking the current layout...' : error ? 'Layout check unavailable.' : 'Layout not checked. Choose Rescan to begin.' }));
      return;
    }
    if (subview === 'coverage') {
      content.append(node('p', { class: 'df-layout-summary', text: `${view.summary}. ${view.examined} realized nodes. Schema ${report.schemaVersion}, rules ${report.ruleSetVersion}.` }));
      const table = node('table', { class: 'df-layout-coverage-table' },
        node('thead', {}, node('tr', {}, ...['Check', 'Support', 'Confidence', 'Limitations'].map(label => node('th', { text: label })))));
      const body = node('tbody');
      for (const rule of view.rules) body.append(node('tr', {},
        node('td', {}, node('strong', { text: layoutRuleLabel(rule.ruleId) }), node('code', { text: rule.ruleId })),
        node('td', { text: rule.support || 'Unknown' }), node('td', { text: rule.confidence || 'Unknown' }),
        node('td', { text: (rule.limitations || []).join(' ') || 'No additional limitation reported.' })));
      table.append(body);
      content.append(table);
      const notes = node('ul', { class: 'df-layout-limitations' });
      for (const limitation of view.limitations) notes.append(node('li', { text: limitation }));
      if (view.limitations.length) content.append(notes);
      return;
    }
    if (subview === 'detail' && selected) {
      const finding = selected;
      const detail = node('div', { class: 'df-layout-detail', 'data-layout-detail-id': finding.id },
        node('h3', { text: layoutRuleLabel(finding.ruleId) }),
        pill(title(finding.outcome), finding.outcome),
        node('p', { text: finding.message }), node('p', { text: finding.explanation || '' }),
        node('p', { class: 'df-layout-context', text: `${finding.element?.type || 'Element'} #${finding.element?.automationId || finding.element?.id || '?'} / ${title(finding.severity)} / ${title(finding.confidence)} confidence` }));
      const actions = node('div', { class: 'df-layout-detail-actions' });
      const action = (label, fn, needsFresh = true) => {
        const item = button(label, () => {
          if (busy || (needsFresh && stale)) return;
          Promise.resolve().then(fn).catch(failure => status(failure.message || 'Layout action failed.'));
        });
        item.disabled = busy || (needsFresh && stale);
        actions.append(item);
        return item;
      };
      action('Show in app', () => show(finding));
      if (hasSource(finding)) action('Open source', () => openSource(finding));
      action('Add to Copilot', () => copy(report, finding.id, true, stale), false);
      action('Copy payload', () => copy(report, finding.id, false, stale), false);
      action('Recheck', () => run(finding), false);
      if (finding.outcome !== 'pass') {
        const suppression = action(finding.suppressed ? 'Unsuppress...' : 'Suppress...', async () => {
          if (await suppress(finding, policyPath)) { subview = 'findings'; await run(); }
        });
        if (!policyPath) {
          suppression.disabled = true;
          suppression.title = 'Rebuild with MauiDevFlowIncludeProjectPath=true so the Inspector can identify the project policy.';
        }
      }
      detail.append(actions);
      const evidence = finding.evidence || {};
      const rows = Object.entries(evidence.sizing || {}).filter(([, value]) => typeof value === 'number' && Number.isFinite(value));
      if (rows.length) {
        detail.append(node('h4', { text: 'Captured sizing (logical units, excluding margins)' }));
        const table = node('table', { class: 'df-layout-evidence-table' });
        for (const [name, value] of rows) table.append(node('tr', {}, node('td', { text: name }), node('td', { text: value })));
        detail.append(table);
      }
      for (const [name, insets] of [['Untransformed layout overflow at window density', evidence.layoutOverflowInsetsPhysicalPixels],
        ['Rendered overflow (physical pixels)', evidence.overflowInsetsPhysicalPixels]]) {
        if (insets) detail.append(node('p', { text: `${name}: left ${insets.left}, top ${insets.top}, right ${insets.right}, bottom ${insets.bottom}.` }));
      }
      for (const related of finding.relatedElements || []) {
        action(`${related.relation}: ${related.element?.automationId || related.element?.id || '?'}`,
          () => show({ element: related.element }));
      }
      const notes = [...(evidence.limitations || []), ...(finding.limitations || [])];
      if (notes.length) detail.append(node('ul', { class: 'df-layout-limitations' }, ...notes.map(text => node('li', { text }))));
      if (finding.suppressed) detail.append(node('p', { text: `Suppressed: ${finding.suppressionReason || 'No reason supplied.'}` }));
      detail.append(node('details', {}, node('summary', { text: 'Technical identifiers' }),
        node('pre', { text: JSON.stringify({ rule: finding.ruleId, finding: finding.id, snapshot: report.snapshot?.id }, null, 2) })));
      content.append(detail);
      return;
    }
    if (view.truncated) content.append(node('p', { class: 'df-layout-notice', text: `Showing ${LAYOUT_FINDING_LIMIT} of ${view.matching} matching findings. Narrow the filters to see the rest.` }));
    const list = node('div', { id: 'diagnostics-list', class: 'df-layout-finding-list' });
    for (const finding of view.findings) {
      const element = finding.element || {};
      const file = text(element.sourceFile).replaceAll('\\', '/').split('/').pop();
      const row = button('', () => {
        selected = finding;
        subview = 'detail';
        if (!stale && !busy) show(finding);
        render();
        back.focus();
      }, `df-layout-finding-row diagnostic-item df-layout-${finding.outcome}`);
      row.setAttribute('data-layout-finding-id', text(finding.id));
      row.setAttribute('aria-label', `${title(finding.outcome)}: ${layoutRuleLabel(finding.ruleId)}. ${finding.message || ''}`);
      row.append(pill(title(finding.outcome), finding.outcome),
        node('span', { class: 'df-layout-row-main' }, node('strong', { text: layoutRuleLabel(finding.ruleId) }), node('span', { text: finding.message })),
        node('span', { class: 'df-layout-row-meta', text: [element.type, element.automationId || element.id, file ? `${file}:${element.sourceLine || '?'}` : '', title(finding.severity), title(finding.confidence)].filter(Boolean).join(' / ') }),
        node('span', { 'aria-hidden': 'true', text: '>' }));
      if (finding.suppressed) row.classList.add('df-suppressed');
      list.append(row);
    }
    if (!view.findings.length) list.append(node('div', { class: 'df-empty', text: view.empty }));
    content.append(list);
  }
  document.addEventListener('visibilitychange', () => {
    if (document.hidden) { clearTimeout(timer); timer = null; pending = false; }
    else if (active && live && (stale || !report)) schedule();
  });
  return {
    root,
    activate(host) {
      active = true;
      root.classList.remove('df-hidden');
      host.replaceChildren(root);
      render();
      if (!report && !busy) run();
      else if (live && stale) schedule();
    },
    deactivate() {
      active = false;
      root.classList.add('df-hidden');
      clearTimeout(timer);
      timer = null;
      pending = false;
      filterMenu.open = false;
      show(null);
    },
    refresh: run,
    invalidate,
    selectionChanged() {
      if (selectedScope) invalidate('The selected subtree changed; rescan to review it.');
    },
    frameChanged(next) {
      if (revision && next && revision !== next) invalidate('The app layout changed; the previous scan is stale.');
      revision = next || revision;
    },
  };
}
