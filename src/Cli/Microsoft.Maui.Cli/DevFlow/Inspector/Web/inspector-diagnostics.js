/**
 * Presentation logic for the Inspector's Layout and Performance data tabs.
 *
 * Kept in its own module (like inspector-evidence.js) so the formatting rules are unit-testable
 * without a DOM. The rules here are part of the product contract:
 *
 *  - A layout report is never summarised as "no problems". Absent findings are reported alongside
 *    the coverage that produced them, because unavailable geometry is `incomplete`, not a pass.
 *  - A performance summary never invents a frame rate. When the agent cannot measure frames the
 *    row states why instead of showing a modelled number.
 */

/** Findings rendered in the dock before the list is truncated (JSON/MCP still return everything). */
export const LAYOUT_FINDING_LIMIT = 200;

const OUTCOME_LABELS = Object.freeze({
  violation: 'Violation',
  observation: 'Observation',
  incomplete: 'Incomplete',
  pass: 'Pass',
  notApplicable: 'Not applicable',
});

const SEVERITY_RANKS = Object.freeze({ info: 0, minor: 1, moderate: 2, serious: 3, critical: 4 });
const CONFIDENCE_RANKS = Object.freeze({ low: 0, medium: 1, high: 2, exact: 3 });
const RULE_LABELS = Object.freeze({
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
});

function outcomeLabel(outcome) {
  return OUTCOME_LABELS[outcome] || 'Finding';
}

function titleCase(value) {
  const normalized = text(value).replace(/[-_]/g, ' ');
  return normalized ? normalized[0].toUpperCase() + normalized.slice(1) : '';
}

export function layoutRuleLabel(ruleId) {
  const value = text(ruleId);
  return RULE_LABELS[value] || value.replace(/^layout\./, '').split('-').map(titleCase).join(' ');
}

function text(value, fallback = '') {
  return value === null || value === undefined ? fallback : String(value);
}

function shortFileName(path) {
  if (!path) return null;
  const normalized = String(path).replace(/\\/g, '/');
  const index = normalized.lastIndexOf('/');
  return index >= 0 ? normalized.slice(index + 1) : normalized;
}

function finiteNumber(value) {
  if (value === null || value === undefined || value === '') return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function bounds(region) {
  const value = region && region.bounds;
  if (!value) return null;
  const x = finiteNumber(value.x);
  const y = finiteNumber(value.y);
  const width = finiteNumber(value.width);
  const height = finiteNumber(value.height);
  return [x, y, width, height].every((item) => item !== null) ? { x, y, width, height } : null;
}

function region(value) {
  const safeBounds = bounds(value);
  if (!safeBounds) return null;
  return {
    bounds: safeBounds,
    points: (Array.isArray(value.points) ? value.points : [])
      .map((point) => ({ x: finiteNumber(point && point.x), y: finiteNumber(point && point.y) }))
      .filter((point) => point.x !== null && point.y !== null),
    precision: text(value.precision, 'unknown'),
  };
}

function safeElement(element) {
  if (!element || !element.id) return null;
  return {
    id: String(element.id),
    parentId: element.parentId ? String(element.parentId) : null,
    type: text(element.type, 'Element'),
    automationId: element.automationId ? String(element.automationId) : null,
    role: element.role ? String(element.role) : null,
    interactive: element.interactive === true,
    sourceFile: element.sourceFile ? String(element.sourceFile) : null,
    sourceLine: Number.isInteger(finiteNumber(element.sourceLine)) ? Number(element.sourceLine) : null,
    sourceColumn: Number.isInteger(finiteNumber(element.sourceColumn)) ? Number(element.sourceColumn) : null,
  };
}

function safeEvidence(evidence) {
  if (!evidence || typeof evidence !== 'object') return null;
  // Text evidence is projected to its structural members only. `text` and `textLength` were
  // removed from the report in schema 2.1 and are never surfaced here even when a legacy 2.0
  // agent still sends them: a length is derived from content this layer must not read, and
  // carrying one would let a report be misread as evidence that text was captured.
  const textEvidence = evidence.text && typeof evidence.text === 'object'
    ? {
        kind: evidence.text.kind ? String(evidence.text.kind) : null,
        isTruncated: typeof evidence.text.isTruncated === 'boolean' ? evidence.text.isTruncated : null,
        renderedLineCount: finiteNumber(evidence.text.renderedLineCount),
        maximumLines: finiteNumber(evidence.text.maximumLines),
        ellipsisCount: finiteNumber(evidence.text.ellipsisCount),
        measurementSource: text(evidence.text.measurementSource, 'unknown'),
      }
    : null;
  return {
    fullRegion: region(evidence.fullRegion),
    visibleRegion: region(evidence.visibleRegion),
    contentRegion: region(evidence.contentRegion),
    lostAreaRatio: finiteNumber(evidence.lostAreaRatio),
    clipChain: (Array.isArray(evidence.clipChain) ? evidence.clipChain : []).map((clip) => ({
      clipperElementId: clip && clip.clipperElementId ? String(clip.clipperElementId) : null,
      kind: text(clip && clip.kind, 'unknown-platform-clip'),
      precision: text(clip && clip.precision, 'unknown'),
      lostAreaRatio: finiteNumber(clip && clip.lostAreaRatio),
      region: region(clip && clip.region),
    })),
    text: textEvidence,
    overlap: evidence.overlap && typeof evidence.overlap === 'object'
      ? {
          intersectionRegion: region(evidence.overlap.intersectionRegion),
          overlapAreaRatio: finiteNumber(evidence.overlap.overlapAreaRatio),
          blockedAreaLowerBound: finiteNumber(evidence.overlap.blockedAreaLowerBound),
          blockedAreaUpperBound: finiteNumber(evidence.overlap.blockedAreaUpperBound),
          sampleCount: Number(evidence.overlap.sampleCount) || 0,
        }
      : null,
    limitations: Array.isArray(evidence.limitations) ? evidence.limitations.map(String) : [],
  };
}

export function filterLayoutFindings(findings, filters = {}) {
  const outcome = filters.outcome || 'all';
  const minimumSeverity = filters.minimumSeverity || 'info';
  const minimumConfidence = filters.minimumConfidence || 'low';
  const rule = String(filters.rule || '').trim().toLowerCase();
  const includeSuppressed = filters.includeSuppressed !== false;

  return (Array.isArray(findings) ? findings : []).filter((finding) => {
    if (!includeSuppressed && finding && finding.suppressed === true) return false;
    const findingOutcome = text(finding && finding.outcome, 'observation');
    if (outcome === 'actionable' && findingOutcome !== 'violation' && findingOutcome !== 'incomplete') return false;
    if (outcome === 'violations' && findingOutcome !== 'violation') return false;
    if (outcome === 'observations' && findingOutcome !== 'observation') return false;
    if (outcome === 'incomplete' && findingOutcome !== 'incomplete') return false;
    if (outcome === 'passes' && findingOutcome !== 'pass') return false;
    if ((SEVERITY_RANKS[text(finding && finding.severity, 'info')] || 0) <
        (SEVERITY_RANKS[minimumSeverity] || 0)) return false;
    if ((CONFIDENCE_RANKS[text(finding && finding.confidence, 'low')] || 0) <
        (CONFIDENCE_RANKS[minimumConfidence] || 0)) return false;
    if (rule && !text(finding && finding.ruleId).toLowerCase().includes(rule)) return false;
    return true;
  });
}

function findingSignature(finding) {
  return JSON.stringify({
    ruleId: finding && finding.ruleId,
    subtype: finding && finding.subtype,
    outcome: finding && finding.outcome,
    severity: finding && finding.severity,
    confidence: finding && finding.confidence,
    actionability: finding && finding.actionability,
    elementId: finding && finding.element && finding.element.id,
    relatedElements: (Array.isArray(finding && finding.relatedElements)
      ? finding.relatedElements
      : []).map((related) => [
        related && related.relation,
        related && related.element && related.element.id,
      ]),
    message: finding && finding.message,
    suppressed: finding && finding.suppressed === true,
  });
}

export function diffLayoutReports(previous, current) {
  if (!current || !Array.isArray(current.findings)) return null;
  const previousFindings = Array.isArray(previous && previous.findings) ? previous.findings : [];
  const previousById = new Map(previousFindings
    .filter((finding) => finding && finding.id)
    .map((finding) => [String(finding.id), finding]));
  const currentById = new Map(current.findings
    .filter((finding) => finding && finding.id)
    .map((finding) => [String(finding.id), finding]));
  const added = [];
  const updated = [];
  const removed = [];

  for (const [id, finding] of currentById) {
    const old = previousById.get(id);
    if (!old) added.push(finding);
    else if (findingSignature(old) !== findingSignature(finding)) updated.push(finding);
  }
  for (const id of previousById.keys()) {
    if (!currentById.has(id)) removed.push(id);
  }

  if (!added.length && !updated.length && !removed.length) return null;
  return { added, updated, removed };
}

export function chooseLayoutScopeRoot(elements) {
  const candidates = Array.isArray(elements) ? elements : [];
  const page = candidates.find((element) =>
    element && /Page$/i.test(String(element.type || '')));
  return String((page || candidates[0])?.id || '') || null;
}

/** Builds the view model the Layout tab renders. Pure: no DOM, no network. */
export function formatLayoutReport(report, filters = null) {
  const summary = (report && report.summary) || {};
  const scope = (report && report.scope) || {};
  const coverage = (report && report.coverage) || {};
  const systemEvidence = (report && report.systemEvidence) || null;
  const allFindings = Array.isArray(report && report.findings) ? report.findings : [];
  const findings = filters ? filterLayoutFindings(allFindings, filters) : allFindings;

  const violations = Number(summary.violations) || 0;
  const observations = Number(summary.observations) || 0;
  const incomplete = Number(summary.incomplete) || 0;
  const passes = Number(summary.passes) || 0;
  const notApplicable = Number(summary.notApplicable) || 0;
  const suppressed = Number(summary.suppressed) || 0;

  const headline = violations > 0
    ? `${violations} violation${violations === 1 ? '' : 's'}`
    : 'No violations in the evaluated elements';

  const parts = [
    headline,
    `${observations} observation${observations === 1 ? '' : 's'}`,
    `${incomplete} incomplete`,
    passes ? `${passes} pass${passes === 1 ? '' : 'es'}` : null,
    notApplicable ? `${notApplicable} not applicable` : null,
    suppressed ? `${suppressed} suppressed` : null,
  ].filter(Boolean);

  const elementsExamined = Number(scope.elementsExamined) || 0;
  const coverageOverall = text(coverage.overall, 'unavailable');
  const status = violations > 0
    ? { tone: 'error', label: `${violations} issue${violations === 1 ? '' : 's'}` }
    : observations > 0
      ? { tone: 'warning', label: `${observations} observation${observations === 1 ? '' : 's'}` }
      : incomplete > 0
        ? { tone: 'neutral', label: 'Coverage incomplete' }
        : { tone: 'success', label: 'No findings' };

  const truncated = findings.length > LAYOUT_FINDING_LIMIT;
  const shown = truncated ? findings.slice(0, LAYOUT_FINDING_LIMIT) : findings;

  return {
    title: 'Layout',
    headline,
    status,
    counts: {
      violations,
      observations,
      incomplete,
      passes,
      notApplicable,
      suppressed,
    },
    summary: parts.join(' · '),
    scope: `${elementsExamined} element${elementsExamined === 1 ? '' : 's'} examined`,
    scopeRootId: scope.rootElementId ? String(scope.rootElementId) : null,
    scopeTruncated: scope.truncated === true,
    coverage: `Coverage: ${coverageOverall}`,
    coverageOverall,
    coverageLabel: titleCase(coverageOverall),
    version: `schema v${text(report && report.schemaVersion, '?')} · rules v${text(report && report.ruleSetVersion, '?')}`,
    snapshot: {
      id: text(report && report.snapshot && report.snapshot.id),
      capturedAt: text(report && report.snapshot && report.snapshot.capturedAt, text(report && report.capturedUtc)),
      platform: text(report && report.snapshot && report.snapshot.platform, text(report && report.platform, 'unknown')),
      treeRevision: text(report && report.snapshot && report.snapshot.treeRevision),
      diagnosticsRevision: text(report && report.snapshot && report.snapshot.diagnosticsRevision),
      stable: report && report.snapshot ? report.snapshot.stable === true : null,
      stabilityReason: text(report && report.snapshot && report.snapshot.stabilityReason),
    },
    rules: (Array.isArray(coverage.rules) ? coverage.rules : []).map((rule) => {
      const support = text(rule.support, 'unavailable');
      const evaluated = Number(rule.evaluated) || 0;
      const skipped = Number(rule.skipped) || 0;
      return {
        ruleId: text(rule.ruleId),
        label: layoutRuleLabel(rule.ruleId),
        support,
        supportLabel: titleCase(support),
        evaluated,
        skipped,
        detail: `${evaluated} evaluated · ${skipped} skipped`,
        limitations: Array.isArray(rule.limitations) ? rule.limitations.map(String) : [],
      };
    }),
    findings: shown.map((finding) => ({
      id: text(finding.id),
      ruleId: text(finding.ruleId),
      ruleLabel: layoutRuleLabel(finding.ruleId),
      outcome: text(finding.outcome, 'observation'),
      outcomeLabel: outcomeLabel(finding.outcome),
      subtype: finding.subtype ? String(finding.subtype) : null,
      severity: text(finding.severity, 'info'),
      severityLabel: titleCase(finding.severity || 'info'),
      confidence: text(finding.confidence, 'medium'),
      confidenceLabel: titleCase(finding.confidence || 'medium'),
      actionability: text(finding.actionability, 'review'),
      message: text(finding.message),
      explanation: text(finding.explanation),
      element: safeElement(finding.element),
      elementId: finding.element && finding.element.id ? String(finding.element.id) : null,
      relatedElements: (Array.isArray(finding.relatedElements) ? finding.relatedElements : [])
        .map((related) => ({
          relation: text(related && related.relation, 'related'),
          element: safeElement(related && related.element),
        }))
        .filter((related) => related.element),
      fixCategories: Array.isArray(finding.fixCategories) ? finding.fixCategories.map(String) : [],
      evidence: safeEvidence(finding.evidence),
      suppressed: finding.suppressed === true,
      suppressionReason: finding.suppressionReason ? String(finding.suppressionReason) : null,
      context: [
        finding.element ? finding.element.type : null,
        finding.element && finding.element.automationId ? `#${finding.element.automationId}` : null,
        finding.element && finding.element.sourceFile
          ? `${shortFileName(finding.element.sourceFile)}${finding.element.sourceLine ? `:${finding.element.sourceLine}` : ''}`
          : null,
      ].filter(Boolean).join(' · '),
      limitations: Array.isArray(finding.limitations) ? finding.limitations.map(String) : [],
    })),
    totalFindings: allFindings.length,
    matchingFindings: findings.length,
    findingsTruncated: truncated,
    limitations: Array.isArray(coverage.limitations) ? coverage.limitations.map(String) : [],
    neverCaptured: Array.isArray(coverage.neverCaptured) ? coverage.neverCaptured.map(String) : [],
    systemEvidence: systemEvidence ? {
      status: text(systemEvidence.status, 'unavailable'),
      deviceId: text(systemEvidence.deviceId),
      captureSkewMs: finiteNumber(systemEvidence.captureSkewMs),
      geometryStable: systemEvidence.geometryStable === true,
      foregroundOwner: text(systemEvidence.foregroundOwner),
      keyboardVisible: typeof systemEvidence.keyboardVisible === 'boolean'
        ? systemEvidence.keyboardVisible
        : null,
      screenshotCaptured: systemEvidence.screenshotCaptured === true,
      externalElementCount: Array.isArray(systemEvidence.elements) ? systemEvidence.elements.length : 0,
      limitations: Array.isArray(systemEvidence.limitations)
        ? systemEvidence.limitations.map(String)
        : [],
    } : null,
  };
}

/** Builds the bounded, text-safe payload offered through the Inspector's Copilot bridge. */
export function createLayoutDataPayload(report, selectedFindingId = null, filters = null) {
  const allFindings = Array.isArray(report && report.findings) ? report.findings : [];
  const selected = selectedFindingId
    ? allFindings.find((finding) => String(finding && finding.id) === selectedFindingId)
    : null;
  const projectedFindings = selectedFindingId
    ? (selected ? [selected] : [])
    : filters
      ? filterLayoutFindings(allFindings, filters)
      : allFindings;
  const view = formatLayoutReport({ ...report, findings: projectedFindings });
  const findings = selectedFindingId
    ? view.findings
    : view.findings.slice(0, 100);
  return {
    schemaVersion: text(report && report.schemaVersion),
    ruleSetVersion: text(report && report.ruleSetVersion),
    snapshot: view.snapshot,
    summary: report && report.summary ? report.summary : {},
    scope: report && report.scope ? report.scope : {},
    coverage: {
      overall: report && report.coverage ? text(report.coverage.overall, 'unavailable') : 'unavailable',
      rules: view.rules,
      limitations: view.limitations,
      neverCaptured: view.neverCaptured,
    },
    systemEvidence: view.systemEvidence,
    selectedFindingId: selectedFindingId || null,
    findings: findings.map((finding) => ({
      id: finding.id,
      ruleId: finding.ruleId,
      subtype: finding.subtype,
      outcome: finding.outcome,
      severity: finding.severity,
      confidence: finding.confidence,
      actionability: finding.actionability,
      element: finding.element,
      relatedElements: finding.relatedElements,
      message: finding.message,
      explanation: finding.explanation,
      fixCategories: finding.fixCategories,
      evidence: finding.evidence,
      suppressed: finding.suppressed,
      suppressionReason: finding.suppressionReason,
      limitations: finding.limitations,
    })),
    truncated: !selectedFindingId && projectedFindings.length > findings.length,
  };
}

function formatBytes(bytes) {
  if (bytes === null || bytes === undefined || Number.isNaN(Number(bytes))) return 'n/a';
  const value = Number(bytes);
  const units = ['B', 'KB', 'MB', 'GB'];
  let size = Math.abs(value);
  let unit = 0;
  while (size >= 1024 && unit < units.length - 1) {
    size /= 1024;
    unit += 1;
  }
  const rounded = size >= 100 ? size.toFixed(0) : size.toFixed(1);
  return `${value < 0 ? '-' : ''}${rounded} ${units[unit]}`;
}

function formatDelta(bytes) {
  if (bytes === null || bytes === undefined) return 'n/a';
  const value = Number(bytes);
  return `${value >= 0 ? '+' : ''}${formatBytes(value)}`;
}

function formatNumber(value, suffix = '') {
  if (value === null || value === undefined || Number.isNaN(Number(value))) return 'n/a';
  const rounded = Math.round(Number(value) * 100) / 100;
  return `${rounded}${suffix}`;
}

/** Builds the view model the Performance tab renders. Pure: no DOM, no network. */
export function formatPerformanceSummary(summary) {
  const session = (summary && summary.session) || {};
  const memory = (summary && summary.memory) || {};
  const gc = (summary && summary.gc) || {};
  const cpu = (summary && summary.cpu) || {};
  const threads = (summary && summary.threads) || {};
  const frames = (summary && summary.frames) || {};
  const markers = (summary && summary.markers) || {};
  const capability = (summary && summary.capability) || {};

  const metrics = [
    {
      label: 'Managed memory',
      value: `${formatBytes(memory.managedStartBytes)} → ${formatBytes(memory.managedEndBytes)}`,
      detail: `peak ${formatBytes(memory.managedPeakBytes)} · delta ${formatDelta(memory.managedDeltaBytes)}`,
    },
    {
      label: 'Process memory',
      value: memory.processSupported && memory.processEndBytes !== null && memory.processEndBytes !== undefined
        ? `${formatBytes(memory.processStartBytes)} → ${formatBytes(memory.processEndBytes)}`
        : 'not observable',
      detail: memory.processSupported && memory.processEndBytes !== null && memory.processEndBytes !== undefined
        ? `${text(memory.processKind, 'unknown')} · peak ${formatBytes(memory.processPeakBytes)} · delta ${formatDelta(memory.processDeltaBytes)}`
        : text(memory.processUnsupportedReason, 'This platform does not expose a process resident/physical memory counter.'),
    },
    {
      label: 'Native heap',
      value: memory.nativeSupported && memory.nativeEndBytes !== null && memory.nativeEndBytes !== undefined
        ? `${formatBytes(memory.nativeStartBytes)} → ${formatBytes(memory.nativeEndBytes)}`
        : 'not observable',
      detail: memory.nativeSupported && memory.nativeEndBytes !== null && memory.nativeEndBytes !== undefined
        ? `${text(memory.nativeKind, 'unknown')} · peak ${formatBytes(memory.nativePeakBytes)} · delta ${formatDelta(memory.nativeDeltaBytes)}`
        : text(memory.nativeUnsupportedReason, 'This platform does not expose a native-heap-specific counter.'),
    },
    {
      label: 'GC collections',
      value: gc.supported
        ? `gen0 +${Number(gc.gen0Delta) || 0} · gen1 +${Number(gc.gen1Delta) || 0} · gen2 +${Number(gc.gen2Delta) || 0}`
        : 'not observable',
      detail: gc.supported ? 'Counted across the retained sample window.' : '',
    },
    {
      label: 'CPU',
      value: cpu.supported && cpu.averagePercent !== null && cpu.averagePercent !== undefined
        ? `avg ${formatNumber(cpu.averagePercent, '%')} · peak ${formatNumber(cpu.peakPercent, '%')}`
        : 'not observable',
      detail: '',
    },
    {
      label: 'Threads',
      value: threads.supported && threads.peakCount !== null && threads.peakCount !== undefined
        ? `peak ${threads.peakCount}`
        : 'not observable',
      detail: '',
    },
    {
      label: 'Frames',
      value: frames.supported
        ? `avg ${formatNumber(frames.averageFps)} fps · min ${formatNumber(frames.minimumFps)} fps`
        : 'not measured',
      detail: frames.supported
        ? `${[frames.source, frames.quality].filter(Boolean).join(' / ')} · ` +
          `p95 ${formatNumber(frames.frameTimeMsP95)} ms · worst ${formatNumber(frames.worstFrameTimeMs)} ms · ` +
          `jank ${frames.jankFrameCount === null || frames.jankFrameCount === undefined ? 'n/a' : frames.jankFrameCount} · ` +
          `stalls ${frames.uiThreadStallCount === null || frames.uiThreadStallCount === undefined ? 'n/a' : frames.uiThreadStallCount}`
        : text(frames.unsupportedReason, 'No native frame timing source is available.'),
    },
    {
      label: 'Markers',
      value: `${Number(markers.total) || 0} marker${(Number(markers.total) || 0) === 1 ? '' : 's'} · ${Number(markers.spanCount) || 0} span${(Number(markers.spanCount) || 0) === 1 ? '' : 's'}`,
      detail: `ui ${Number(markers.ui) || 0} · network ${Number(markers.network) || 0} · navigation ${Number(markers.navigation) || 0}`,
    },
  ];

  const warnings = Array.isArray(summary && summary.warnings) ? summary.warnings.map(String) : [];

  return {
    title: session.active ? 'Performance · recording' : 'Performance · stopped',
    active: !!session.active,
    session: `${Number(session.sampleCount) || 0} sample${(Number(session.sampleCount) || 0) === 1 ? '' : 's'} · ` +
      `${formatNumber((Number(session.sampledDurationMs) || 0) / 1000)} s · every ${Number(session.sampleIntervalMs) || 0} ms`,
    mode: `${text(capability.platform, 'unknown')} · mode ${text(capability.mode, 'unknown')}` +
      (capability.lowPerturbation ? ' · read-only profile build' : ''),
    perturbed: !capability.lowPerturbation,
    perturbationNote: capability.lowPerturbation
      ? 'Explicit profile mode: the agent is read-only and low-perturbation.'
      : 'Debug mode: Hot Reload, the debugger, and DevFlow diagnostics perturb these numbers. Compare runs, do not trust absolute values.',
    metrics,
    hotspots: (Array.isArray(summary && summary.hotspots) ? summary.hotspots : []).map((hotspot) => ({
      name: `${text(hotspot.kind)}/${text(hotspot.name)}`,
      screen: hotspot.screen ? String(hotspot.screen) : null,
      p95: formatNumber(hotspot.p95DurationMs, ' ms'),
      max: formatNumber(hotspot.maxDurationMs, ' ms'),
      count: Number(hotspot.count) || 0,
      errorCount: Number(hotspot.errorCount) || 0,
    })),
    warnings,
    limitations: Array.isArray(capability.limitations) ? capability.limitations.map(String) : [],
  };
}
