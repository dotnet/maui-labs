// Local-only prototype-study evidence for the Test Workbench.
// This module deliberately has no transport dependency: it records a bounded,
// value-free session journal in sessionStorage and exports only that journal.

export const PROTOTYPE_STUDY_SCHEMA = 'maui-devflow-prototype-study';
export const PROTOTYPE_STUDY_KIND = 'local-session-evidence';
export const PROTOTYPE_STUDY_VERSION = 1;
export const PROTOTYPE_STUDY_STORAGE_KEY = 'maui-devflow-prototype-study-v2';
export const PROTOTYPE_STUDY_MAX_EVENTS = 256;

// Authoring-time protocol identity. Sessions recorded under different protocol
// versions, task ids, or arms are different measurements and must never be pooled.
export const PROTOTYPE_STUDY_PROTOCOL = 'maui-devflow-authoring-time';
export const PROTOTYPE_STUDY_PROTOCOL_VERSION = 1;
export const PROTOTYPE_STUDY_ARMS = Object.freeze(['assisted', 'unassisted-control']);
export const PROTOTYPE_STUDY_TASK_IDS = Object.freeze([
  'task-01-first-run-smoke',
  'task-02-form-entry-assertion',
  'task-03-navigation-round-trip',
  'task-04-list-scroll-select',
  'task-05-repair-a-broken-selector',
]);
export const PROTOTYPE_STUDY_EVENT_KINDS = Object.freeze([
  'workbench-opened',
  'goal-defined',
  'recording-started',
  'recording-stopped',
  'assertion-added',
  'test-saved',
  'run-started',
  'run-terminal',
  'results-opened',
  'improve-scanned',
  'agent-requested',
  'agent-approved',
  'agent-rejected',
  'agent-expired',
  'agent-stale',
  'agent-consumed',
  'repair-proposed',
  'repair-approved',
  'repair-rejected',
  'repair-applied',
  'repair-verified',
  'repair-rollback-required',
  'repair-rollback-failed',
  'repair-reverted',
]);

const EVENT_KINDS = new Set(PROTOTYPE_STUDY_EVENT_KINDS);
const STUDY_ARMS = new Set(PROTOTYPE_STUDY_ARMS);
const STUDY_TASK_IDS = new Set(PROTOTYPE_STUDY_TASK_IDS);
const PARTICIPANT_SALT_PATTERN = /^participant-[a-f0-9]{8,64}$/;
const PROVENANCE = new Set(['human', 'agent', 'mixed']);
const SELECTOR_QUALITY = new Set(['durable', 'fragile', 'unknown']);
const TERMINAL_STATES = new Set([
  'passed',
  'failed',
  'cancelled',
  'timed-out',
  'lease-lost',
  'infrastructure-error',
  'unknown-completion',
  'orphaned',
]);
const FAILURE_CLASSES = new Set([
  'locator-not-found',
  'assertion-failed',
  'action-failed',
  'timeout',
  'cancelled',
  'lease-lost',
  'infrastructure-error',
  'unknown-completion',
  'precondition-failed',
  'replay-divergence',
  'unclassified',
  'other',
]);
const REPAIR_EVENT_KINDS = new Set([
  'repair-proposed',
  'repair-approved',
  'repair-rejected',
  'repair-applied',
  'repair-verified',
  'repair-rollback-required',
  'repair-rollback-failed',
  'repair-reverted',
]);
const AGENT_APPROVAL_EVENT_KINDS = new Set([
  'agent-requested',
  'agent-approved',
  'agent-rejected',
  'agent-expired',
  'agent-stale',
  'agent-consumed',
]);
const AUTHORING_EVENT_KINDS = new Set([
  'goal-defined',
  'recording-started',
  'recording-stopped',
  'assertion-added',
  'test-saved',
]);
const SAFE_INPUT_KEYS = new Set([
  'provenance',
  'quick',
  'durationMs',
  'stepCount',
  'discarded',
  'hard',
  'selectorQuality',
  'hardAssertionCount',
  'durableSelectorCount',
  'fragileSelectorCount',
  'runId',
  'state',
  'failureClass',
  'findingCount',
  'approvalRequestId',
  'proposalId',
]);
const RESTRICTED_INPUT_KEY = /(goal|text|typed|value|selector|source|path|content|screenshot|prompt|reviewer|identity|url|payload|device|serial|secret|token|password)/i;

function boundedInteger(value, maximum = 100000) {
  const number = Number(value);
  if (!Number.isFinite(number)) return null;
  return Math.max(0, Math.min(maximum, Math.floor(number)));
}

function boundedDuration(value) {
  return boundedInteger(value, 24 * 60 * 60 * 1000);
}

function boundedMaxEvents(value) {
  const parsed = boundedInteger(value, 512);
  return parsed == null ? PROTOTYPE_STUDY_MAX_EVENTS : Math.max(1, parsed);
}

function isoTime(value) {
  const milliseconds = boundedInteger(value, Number.MAX_SAFE_INTEGER) ?? 0;
  try {
    return new Date(milliseconds).toISOString();
  } catch {
    return new Date(0).toISOString();
  }
}

function eventTime(event) {
  const parsed = Date.parse(event?.at || '');
  return Number.isFinite(parsed) ? parsed : null;
}

function safeEnum(value, values) {
  return typeof value === 'string' && values.has(value) ? value : null;
}

function normalizeFailureClass(value) {
  if (typeof value !== 'string' || !value.trim()) return null;
  const normalized = value.trim().toLowerCase().replace(/_/g, '-');
  if (FAILURE_CLASSES.has(normalized)) return normalized;
  if (normalized === 'timedout') return 'timeout';
  if (normalized === 'infrastructure') return 'infrastructure-error';
  return 'other';
}

function defaultRandomId() {
  try {
    if (typeof globalThis !== 'undefined' && typeof globalThis.crypto?.randomUUID === 'function')
      return globalThis.crypto.randomUUID();
    if (typeof globalThis !== 'undefined' && typeof globalThis.crypto?.getRandomValues === 'function') {
      const bytes = new Uint32Array(4);
      globalThis.crypto.getRandomValues(bytes);
      return [...bytes].map((value) => value.toString(36)).join('-');
    }
  } catch {
    // The nonpersistent fallback remains local and is explicitly marked unavailable on storage failure.
  }
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}

function opaqueLocalId(value, prefix) {
  const normalized = String(value || '').replace(/[^a-zA-Z0-9_-]/g, '').slice(0, 96);
  return `${prefix}-${normalized || 'session'}`;
}

function validOpaqueLocalId(value, prefix) {
  return typeof value === 'string' &&
    new RegExp(`^${prefix}-[a-zA-Z0-9_-]{1,96}$`).test(value);
}

function opaqueReference(kind, value, salt) {
  if (value == null || value === '') return null;
  const input = `${salt}|${kind}|${String(value).slice(0, 512)}`;
  let hash = 2166136261;
  for (let index = 0; index < input.length; index++) {
    hash ^= input.charCodeAt(index);
    hash = Math.imul(hash, 16777619);
  }
  return `${kind}-${(hash >>> 0).toString(36)}`;
}

function hasRestrictedInput(value, depth = 0) {
  if (!value || typeof value !== 'object' || depth > 3) return false;
  for (const [key, nested] of Object.entries(value)) {
    if (!SAFE_INPUT_KEYS.has(key) && RESTRICTED_INPUT_KEY.test(key)) return true;
    if (nested && typeof nested === 'object' && hasRestrictedInput(nested, depth + 1)) return true;
  }
  return false;
}

function deriveRecordingDuration(events, at) {
  for (let index = events.length - 1; index >= 0; index--) {
    if (events[index]?.kind !== 'recording-started') continue;
    const startedAt = eventTime(events[index]);
    return startedAt == null ? null : boundedDuration(at - startedAt);
  }
  return null;
}

function eventWithProvenance(event, data) {
  const provenance = safeEnum(data?.provenance, PROVENANCE);
  if (provenance) event.provenance = provenance;
  return event;
}

function sanitizeEvent(kind, data, at, state) {
  if (!EVENT_KINDS.has(kind) || hasRestrictedInput(data)) return null;
  const event = { kind, at: isoTime(at) };

  switch (kind) {
    case 'workbench-opened':
    case 'goal-defined':
      return eventWithProvenance(event, data);

    case 'recording-started':
      event.quick = data?.quick === true;
      return eventWithProvenance(event, data);

    case 'recording-stopped': {
      const duration = boundedDuration(data?.durationMs) ?? deriveRecordingDuration(state.events, at);
      const stepCount = boundedInteger(data?.stepCount);
      if (duration != null) event.durationMs = duration;
      if (stepCount != null) event.stepCount = stepCount;
      if (data?.discarded === true) event.discarded = true;
      return eventWithProvenance(event, data);
    }

    case 'assertion-added': {
      event.hard = data?.hard === true;
      const quality = safeEnum(data?.selectorQuality, SELECTOR_QUALITY);
      if (quality) event.selectorQuality = quality;
      return eventWithProvenance(event, data);
    }

    case 'test-saved': {
      for (const field of ['stepCount', 'hardAssertionCount', 'durableSelectorCount', 'fragileSelectorCount']) {
        const value = boundedInteger(data?.[field]);
        if (value != null) event[field] = value;
      }
      return eventWithProvenance(event, data);
    }

    case 'run-started': {
      const run = opaqueReference('run', data?.runId, state.redactionSalt);
      const stepCount = boundedInteger(data?.stepCount);
      if (run) event.run = run;
      if (stepCount != null) event.stepCount = stepCount;
      return eventWithProvenance(event, data);
    }

    case 'run-terminal': {
      const run = opaqueReference('run', data?.runId, state.redactionSalt);
      const terminalState = safeEnum(data?.state, TERMINAL_STATES);
      const duration = boundedDuration(data?.durationMs);
      const stepCount = boundedInteger(data?.stepCount);
      if (run) event.run = run;
      if (terminalState) event.state = terminalState;
      if (duration != null) event.durationMs = duration;
      if (stepCount != null) event.stepCount = stepCount;
      if (terminalState && terminalState !== 'passed')
        event.failureClass = normalizeFailureClass(data?.failureClass) || 'unclassified';
      return event;
    }

    case 'results-opened': {
      const run = opaqueReference('run', data?.runId, state.redactionSalt);
      const terminalState = safeEnum(data?.state, TERMINAL_STATES);
      if (run) event.run = run;
      if (terminalState) event.state = terminalState;
      return event;
    }

    case 'improve-scanned': {
      const findingCount = boundedInteger(data?.findingCount);
      event.findingCount = findingCount ?? 0;
      return event;
    }

    case 'agent-requested':
    case 'agent-approved':
    case 'agent-rejected':
    case 'agent-expired':
    case 'agent-stale':
    case 'agent-consumed': {
      const approval = opaqueReference('approval', data?.approvalRequestId, state.redactionSalt);
      const duration = boundedDuration(data?.durationMs);
      if (approval) event.approval = approval;
      if (duration != null) event.durationMs = duration;
      return eventWithProvenance(event, data);
    }

    default: {
      const proposal = opaqueReference('proposal', data?.proposalId, state.redactionSalt);
      if (proposal) event.proposal = proposal;
      return eventWithProvenance(event, data);
    }
  }
}

function validStoredEvent(event) {
  if (!event || typeof event !== 'object' || !EVENT_KINDS.has(event.kind) || eventTime(event) == null) return false;
  const allowed = new Set([
    'kind', 'at', 'provenance', 'quick', 'durationMs', 'stepCount', 'discarded', 'hard',
    'selectorQuality', 'hardAssertionCount', 'durableSelectorCount', 'fragileSelectorCount',
    'run', 'state', 'failureClass', 'findingCount', 'approval', 'proposal',
  ]);
  if (Object.keys(event).some((key) => !allowed.has(key))) return false;
  if (event.provenance != null && !safeEnum(event.provenance, PROVENANCE)) return false;
  if (event.selectorQuality != null && !safeEnum(event.selectorQuality, SELECTOR_QUALITY)) return false;
  if (event.state != null && !safeEnum(event.state, TERMINAL_STATES)) return false;
  if (event.failureClass != null && !safeEnum(event.failureClass, FAILURE_CLASSES)) return false;
  for (const key of ['durationMs', 'stepCount', 'hardAssertionCount', 'durableSelectorCount', 'fragileSelectorCount', 'findingCount']) {
    if (event[key] != null && boundedInteger(event[key], key === 'durationMs' ? 24 * 60 * 60 * 1000 : 100000) == null)
      return false;
  }
  for (const key of ['quick', 'discarded', 'hard']) {
    if (event[key] != null && typeof event[key] !== 'boolean') return false;
  }
  for (const key of ['run', 'approval', 'proposal']) {
    if (event[key] != null && (typeof event[key] !== 'string' || !/^[a-z]+-[a-z0-9]+$/i.test(event[key]))) return false;
  }
  return true;
}

function normalizedAssignment(value) {
  const source = value && typeof value === 'object' ? value : {};
  const participantSalt = typeof source.participantSalt === 'string' &&
    PARTICIPANT_SALT_PATTERN.test(source.participantSalt.trim().toLowerCase())
    ? source.participantSalt.trim().toLowerCase()
    : null;
  const taskId = typeof source.taskId === 'string' && STUDY_TASK_IDS.has(source.taskId.trim())
    ? source.taskId.trim()
    : null;
  const arm = typeof source.arm === 'string' && STUDY_ARMS.has(source.arm.trim())
    ? source.arm.trim()
    : null;
  return { participantSalt, taskId, arm };
}

function assignmentFromLocation(location) {
  try {
    const search = typeof location?.search === 'string' ? location.search : '';
    if (!search) return {};
    const query = new URLSearchParams(search);
    return {
      participantSalt: query.get('studyParticipant'),
      taskId: query.get('studyTask'),
      arm: query.get('studyArm'),
    };
  } catch {
    return {};
  }
}

function createSession(now, maxEventCount, randomId, recoveredFromCorruptStorage = false, assignment = {}) {
  const resolved = normalizedAssignment(assignment);
  return {
    schema: PROTOTYPE_STUDY_SCHEMA,
    kind: PROTOTYPE_STUDY_KIND,
    version: PROTOTYPE_STUDY_VERSION,
    protocol: PROTOTYPE_STUDY_PROTOCOL,
    protocolVersion: PROTOTYPE_STUDY_PROTOCOL_VERSION,
    localSessionId: opaqueLocalId(randomId(), 'local'),
    startedAt: isoTime(now()),
    redactionSalt: opaqueLocalId(randomId(), 'salt'),
    participantSalt: resolved.participantSalt,
    taskId: resolved.taskId,
    arm: resolved.arm,
    maxEventCount,
    retention: 'sessionStorage-current-tab',
    droppedEventCount: 0,
    recoveredFromCorruptStorage,
    events: [],
  };
}

function validStoredSession(value) {
  return value &&
    value.schema === PROTOTYPE_STUDY_SCHEMA &&
    value.kind === PROTOTYPE_STUDY_KIND &&
    value.version === PROTOTYPE_STUDY_VERSION &&
    value.protocol === PROTOTYPE_STUDY_PROTOCOL &&
    value.protocolVersion === PROTOTYPE_STUDY_PROTOCOL_VERSION &&
    validOpaqueLocalId(value.localSessionId, 'local') &&
    validOpaqueLocalId(value.redactionSalt, 'salt') &&
    (value.participantSalt == null || PARTICIPANT_SALT_PATTERN.test(value.participantSalt)) &&
    (value.taskId == null || STUDY_TASK_IDS.has(value.taskId)) &&
    (value.arm == null || STUDY_ARMS.has(value.arm)) &&
    eventTime({ at: value.startedAt }) != null &&
    Array.isArray(value.events) &&
    value.events.length <= 512 &&
    value.events.every(validStoredEvent) &&
    boundedMaxEvents(value.maxEventCount) === value.maxEventCount &&
    Number.isInteger(value.droppedEventCount) &&
    value.droppedEventCount >= 0 &&
    value.droppedEventCount <= 100000;
}

function dedupeKey(event) {
  if (event.kind === 'goal-defined') return event.kind;
  if (event.kind === 'run-started') return `${event.kind}|${event.run || 'unknown'}`;
  if (event.kind === 'run-terminal' || event.kind === 'results-opened')
    return `${event.kind}|${event.run || 'unknown'}|${event.state || 'unknown'}`;
  if (AGENT_APPROVAL_EVENT_KINDS.has(event.kind))
    return `${event.kind}|${event.approval || 'unknown'}`;
  if (REPAIR_EVENT_KINDS.has(event.kind)) return `${event.kind}|${event.proposal || 'unknown'}`;
  return null;
}

function countEvents(events, kind) {
  return events.filter((event) => event.kind === kind).length;
}

function firstEvent(events, kind) {
  return events.find((event) => event.kind === kind) || null;
}

function latestEvent(events, kind) {
  for (let index = events.length - 1; index >= 0; index--) {
    if (events[index].kind === kind) return events[index];
  }
  return null;
}

function elapsed(from, to) {
  const start = eventTime(from);
  const end = eventTime(to);
  return start == null || end == null || end < start ? null : end - start;
}

function elapsedFromSession(session, event) {
  return elapsed({ at: session.startedAt }, event);
}

function summarizeAuthoringMode(events) {
  const values = new Set(
    events
      .filter((event) => AUTHORING_EVENT_KINDS.has(event.kind))
      .map((event) => event.provenance)
      .filter(Boolean),
  );
  if (values.has('mixed') || (values.has('human') && values.has('agent'))) return 'mixed';
  if (values.has('human')) return 'human';
  if (values.has('agent')) return 'agent';
  return 'unknown';
}

function classificationCounts(events) {
  const counts = new Map();
  for (const event of events) {
    if (event.kind !== 'run-terminal' || event.state === 'passed') continue;
    const key = event.failureClass || 'unclassified';
    counts.set(key, (counts.get(key) || 0) + 1);
  }
  return Object.fromEntries([...counts.entries()].sort(([left], [right]) => left.localeCompare(right)));
}

function firstDiagnosticAfter(events, event) {
  const from = eventTime(event);
  if (from == null) return null;
  return events.find((candidate) =>
    ['improve-scanned', 'repair-proposed'].includes(candidate.kind) &&
    (eventTime(candidate) ?? -1) >= from) || null;
}

function summarizeAgentApprovals(events) {
  const approvalEvents = events.filter((event) => AGENT_APPROVAL_EVENT_KINDS.has(event.kind));
  const latestStates = new Map();
  const requestedAt = new Map();
  const decisionDurationsMs = [];
  for (const event of approvalEvents) {
    if (!event.approval) continue;
    if (event.kind === 'agent-requested') requestedAt.set(event.approval, event);
    else {
      latestStates.set(event.approval, event.kind.replace('agent-', ''));
      const requested = requestedAt.get(event.approval);
      const duration = event.durationMs ?? (requested ? elapsed(requested, event) : null);
      if (duration != null && ['agent-approved', 'agent-rejected'].includes(event.kind))
        decisionDurationsMs.push(duration);
    }
  }
  const count = (kind) => approvalEvents.filter((event) => event.kind === kind).length;
  const requested = count('agent-requested');
  const pending = [...requestedAt.keys()].filter((approval) => !latestStates.has(approval)).length;
  return {
    requested,
    approved: count('agent-approved'),
    rejected: count('agent-rejected'),
    expired: count('agent-expired'),
    stale: count('agent-stale'),
    consumed: count('agent-consumed'),
    pending,
    decisionDurationsMs,
    averageDecisionDurationMs: decisionDurationsMs.length
      ? Math.round(decisionDurationsMs.reduce((total, duration) => total + duration, 0) / decisionDurationsMs.length)
      : null,
  };
}

function summarizeSession(session, storageState) {
  const events = session.events;
  const goal = firstEvent(events, 'goal-defined');
  const recordingStops = events.filter((event) => event.kind === 'recording-stopped');
  const completedRecordings = recordingStops.filter((event) => event.discarded !== true);
  const saved = latestEvent(events, 'test-saved');
  const terminalRuns = events.filter((event) => event.kind === 'run-terminal');
  const passedRuns = terminalRuns.filter((event) => event.state === 'passed');
  const failedRuns = terminalRuns.filter((event) => event.state === 'failed');
  const firstTerminal = terminalRuns[0] || null;
  const firstFailed = terminalRuns.find((event) => event.state && event.state !== 'passed') || null;
  const firstDiagnostic = firstFailed ? firstDiagnosticAfter(events, firstFailed) : null;
  const recordingDurationMs = recordingStops.reduce((total, event) => total + (event.durationMs || 0), 0);
  const recordingDurationKnown = recordingStops.length > 0 && recordingStops.every((event) => event.durationMs != null);
  const latestReview = saved
    ? [...completedRecordings].reverse().find((event) => (eventTime(event) ?? Number.MAX_SAFE_INTEGER) <= (eventTime(saved) ?? -1)) || null
    : null;
  const fallbackAssertions = events.filter((event) => event.kind === 'assertion-added');
  const durableSelectorCount = saved?.durableSelectorCount ??
    fallbackAssertions.filter((event) => event.selectorQuality === 'durable').length;
  const fragileSelectorCount = saved?.fragileSelectorCount ??
    fallbackAssertions.filter((event) => event.selectorQuality === 'fragile').length;
  const selectorTotal = durableSelectorCount + fragileSelectorCount;
  const runDurationsMs = terminalRuns
    .map((event) => event.durationMs)
    .filter((value) => value != null);
  const replayStatus = terminalRuns.length < 2
    ? 'insufficient'
    : passedRuns.length === terminalRuns.length ? 'stable' : 'unstable';
  const authoringMode = summarizeAuthoringMode(events);
  const latestRollbackByProposal = new Map();
  for (const event of events) {
    if (!['repair-rollback-required', 'repair-rollback-failed', 'repair-reverted'].includes(event.kind))
      continue;
    latestRollbackByProposal.set(event.proposal || 'unknown', event.kind);
  }
  const repair = {
    proposed: countEvents(events, 'repair-proposed'),
    approved: countEvents(events, 'repair-approved'),
    rejected: countEvents(events, 'repair-rejected'),
    applied: countEvents(events, 'repair-applied'),
    verified: countEvents(events, 'repair-verified'),
    rollbackRequired: countEvents(events, 'repair-rollback-required'),
    rollbackFailed: countEvents(events, 'repair-rollback-failed'),
    reverted: countEvents(events, 'repair-reverted'),
    unresolvedRollback: [...latestRollbackByProposal.values()]
      .filter((kind) => kind !== 'repair-reverted').length,
  };
  const agentApprovals = summarizeAgentApprovals(events);
  const humanInvolvementNeeded = [];
  if (firstFailed && countEvents(events, 'results-opened') === 0)
    humanInvolvementNeeded.push('review-terminal-result');
  if (repair.proposed > repair.approved + repair.rejected)
    humanInvolvementNeeded.push('repair-decision');
  if (repair.applied > repair.verified + repair.reverted ||
      repair.unresolvedRollback > 0)
    humanInvolvementNeeded.push('repair-verification-or-rollback');
  if (agentApprovals.pending > 0)
    humanInvolvementNeeded.push('agent-approval-decision');

  const missingFields = [];
  if (authoringMode === 'unknown') missingFields.push('authoringMode');
  if (!goal) missingFields.push('timeToGoalMs');
  if (!recordingStops.length || !recordingDurationKnown) missingFields.push('recordingDurationMs');
  if (!saved || !latestReview) missingFields.push('reviewToSaveDurationMs');
  if (!firstTerminal) {
    missingFields.push('timeToFirstResultMs', 'runDurationsMs', 'failureClassificationCounts');
  }
  if (!saved) missingFields.push('savedTestMetrics');
  if (!selectorTotal) missingFields.push('durableFragileSelectorRatio');
  if (replayStatus === 'insufficient') missingFields.push('replayStability');
  if (firstFailed && !firstDiagnostic) missingFields.push('timeToDiagnosisProxyMs');
  if (session.droppedEventCount > 0) missingFields.push('completeEventHistory');
  if (storageState === 'unavailable') missingFields.push('sessionStoragePersistence');
  if (!session.arm) missingFields.push('studyArm');
  if (!session.taskId) missingFields.push('studyTaskId');
  if (!session.participantSalt) missingFields.push('participantSalt');

  const limitations = [
    'Local session only; closing the browser tab or clearing session data can remove this journal.',
    'No telemetry or network egress is performed.',
    'This is prototype-study evidence, not qualification, device evidence, or platform proof.',
    'Metrics are descriptive session proxies and do not establish causality.',
    'A single session carries no control arm. Authoring-time durations here are uninterpretable ' +
      'until paired with unassisted-control sessions for the same task.',
  ];
  if (session.recoveredFromCorruptStorage)
    limitations.push('A corrupt local journal was discarded before this session started.');
  if (session.droppedEventCount > 0)
    limitations.push('The event cap discarded older entries; retained metrics may be incomplete.');
  if (storageState === 'unavailable')
    limitations.push('sessionStorage is unavailable, so no new evidence was retained.');

  return {
    localSessionOnly: true,
    protocol: {
      name: PROTOTYPE_STUDY_PROTOCOL,
      protocolVersion: PROTOTYPE_STUDY_PROTOCOL_VERSION,
      taskId: session.taskId ?? null,
      arm: session.arm ?? null,
      participantLinkage: session.participantSalt ? 'salted' : 'unavailable',
    },
    storage: {
      available: storageState !== 'unavailable',
      status: storageState,
      retainedEventCount: events.length,
      droppedEventCount: session.droppedEventCount,
      maxEventCount: session.maxEventCount,
      retention: session.retention,
    },
    authoringMode,
    timeToGoalMs: goal ? elapsedFromSession(session, goal) : null,
    recordingDurationMs: recordingStops.length ? recordingDurationMs : null,
    reviewToSaveDurationMs: latestReview && saved ? elapsed(latestReview, saved) : null,
    timeToFirstResultMs: firstTerminal ? elapsedFromSession(session, firstTerminal) : null,
    firstRunToTerminalMs: firstTerminal?.durationMs ?? null,
    runDurationsMs,
    stepCount: saved?.stepCount ?? latestEvent(completedRecordings, 'recording-stopped')?.stepCount ?? null,
    hardAssertionCount: saved?.hardAssertionCount ??
      fallbackAssertions.filter((event) => event.hard === true).length,
    durableSelectorCount,
    fragileSelectorCount,
    durableSelectorRatio: selectorTotal ? durableSelectorCount / selectorTotal : null,
    selectorObservationScope: saved ? 'saved-test' : fallbackAssertions.length ? 'assertion-events-only' : 'missing',
    runs: Math.max(countEvents(events, 'run-started'), terminalRuns.length),
    runsStarted: countEvents(events, 'run-started'),
    terminalRuns: terminalRuns.length,
    passed: passedRuns.length,
    failed: failedRuns.length,
    nonPassedTerminalRuns: terminalRuns.length - passedRuns.length,
    replayStability: {
      status: replayStatus,
      successfulReplays: passedRuns.length,
      terminalRuns: terminalRuns.length,
      passRate: terminalRuns.length ? passedRuns.length / terminalRuns.length : null,
    },
    failureClassificationCounts: classificationCounts(events),
    timeToDiagnosisProxyMs: firstFailed && firstDiagnostic ? elapsed(firstFailed, firstDiagnostic) : null,
    diagnosisProxy: 'first improve scan or repair proposal after a non-pass terminal result',
    repair,
    improve: {
      scans: countEvents(events, 'improve-scanned'),
      findings: events
        .filter((event) => event.kind === 'improve-scanned')
        .reduce((total, event) => total + (event.findingCount || 0), 0),
    },
    agentApprovals,
    humanInvolvement: {
      resultsOpened: countEvents(events, 'results-opened'),
      repairApprovals: repair.approved,
      repairRejections: repair.rejected,
      agentApprovalDecisions: agentApprovals.approved + agentApprovals.rejected,
      needsAttention: humanInvolvementNeeded,
    },
    missingFields,
    limitations,
  };
}

function storageFromOptions(storage) {
  if (storage) return storage;
  try {
    return typeof window !== 'undefined' ? window.sessionStorage : null;
  } catch {
    return null;
  }
}

export function createPrototypeStudyJournal(options = {}) {
  const now = typeof options.now === 'function' ? options.now : () => Date.now();
  const randomId = typeof options.randomId === 'function' ? options.randomId : defaultRandomId;
  const storage = storageFromOptions(options.storage);
  const storageKey = typeof options.storageKey === 'string' && options.storageKey
    ? options.storageKey
    : PROTOTYPE_STUDY_STORAGE_KEY;
  const configuredMaxEvents = boundedMaxEvents(options.maxEventCount);
  const requestedAssignment = normalizedAssignment(
    options.assignment ??
    assignmentFromLocation(options.location ?? (typeof window !== 'undefined' ? window.location : null)));
  let storageState = storage ? 'available' : 'unavailable';
  let session = null;

  function persist() {
    if (!storage) {
      storageState = 'unavailable';
      return false;
    }
    try {
      storage.setItem(storageKey, JSON.stringify(session));
      return true;
    } catch {
      storageState = 'unavailable';
      return false;
    }
  }

  if (storage) {
    try {
      const raw = storage.getItem(storageKey);
      if (raw) {
        const parsed = JSON.parse(raw);
        if (validStoredSession(parsed)) {
          session = {
            ...parsed,
            maxEventCount: Math.min(parsed.maxEventCount, configuredMaxEvents),
            events: parsed.events.slice(-Math.min(parsed.maxEventCount, configuredMaxEvents)),
          };
          const discardedByConfiguredLimit = parsed.events.length - session.events.length;
          session.droppedEventCount += Math.max(0, discardedByConfiguredLimit);
        } else {
          storage.removeItem(storageKey);
          storageState = 'recovered-corrupt';
        }
      }
    } catch {
      storageState = 'recovered-corrupt';
      try {
        storage.removeItem(storageKey);
      } catch {
        storageState = 'unavailable';
      }
    }
  }

  if (!session) {
    session = createSession(now, configuredMaxEvents, randomId, storageState === 'recovered-corrupt', requestedAssignment);
    if (!persist() && storageState !== 'unavailable') storageState = 'unavailable';
  }

  function record(kind, data = {}) {
    if (storageState === 'unavailable') return false;
    const event = sanitizeEvent(kind, data, now(), session);
    if (!event) return false;
    const key = dedupeKey(event);
    if (key && session.events.some((existing) => dedupeKey(existing) === key)) return false;

    const previousEvents = session.events;
    const previousDropped = session.droppedEventCount;
    session.events = [...previousEvents, event];
    if (session.events.length > session.maxEventCount) {
      const excess = session.events.length - session.maxEventCount;
      session.events = session.events.slice(excess);
      session.droppedEventCount += excess;
    }
    if (!persist()) {
      session.events = previousEvents;
      session.droppedEventCount = previousDropped;
      return false;
    }
    return true;
  }

  function clear() {
    if (!storage || storageState === 'unavailable') return false;
    try {
      storage.removeItem(storageKey);
    } catch {
      storageState = 'unavailable';
      return false;
    }
    storageState = 'available';
    session = createSession(now, configuredMaxEvents, randomId, false, requestedAssignment);
    return persist();
  }

  // Protocol assignment may only be stamped before the first recorded event. Re-stamping a
  // session that already has timing evidence would let an operator move a slow session into the
  // other arm after seeing the result, which is the exact bias this study must not permit.
  function assign(assignment) {
    const resolved = normalizedAssignment(assignment);
    if (storageState === 'unavailable') return { assigned: false, reason: 'storage-unavailable' };
    if (session.events.length > 0) return { assigned: false, reason: 'session-already-has-evidence' };
    if (!resolved.arm) return { assigned: false, reason: 'unknown-arm' };
    if (!resolved.taskId) return { assigned: false, reason: 'unknown-task' };
    // A malformed salt would silently become null and the session would export as unlinkable,
    // so reject it here where the operator can still fix it.
    if (!resolved.participantSalt) return { assigned: false, reason: 'unknown-participant' };
    const previous = { participantSalt: session.participantSalt, taskId: session.taskId, arm: session.arm };
    session.participantSalt = resolved.participantSalt;
    session.taskId = resolved.taskId;
    session.arm = resolved.arm;
    if (!persist()) {
      Object.assign(session, previous);
      return { assigned: false, reason: 'storage-unavailable' };
    }
    return { assigned: true, reason: null };
  }

  function summary() {
    return summarizeSession(session, storageState);
  }

  return Object.freeze({
    record,
    clear,
    assign,
    summary,
    exportEvidence() {
      const eligibility = [];
      if (!session.arm) eligibility.push('arm-unassigned');
      if (!session.taskId) eligibility.push('task-unassigned');
      if (!session.participantSalt) eligibility.push('participant-unlinkable');
      if (storageState !== 'available') eligibility.push('storage-degraded');
      if (session.droppedEventCount > 0) eligibility.push('event-history-truncated');
      return {
        schema: PROTOTYPE_STUDY_SCHEMA,
        kind: PROTOTYPE_STUDY_KIND,
        version: PROTOTYPE_STUDY_VERSION,
        localSessionOnly: true,
        protocol: {
          name: PROTOTYPE_STUDY_PROTOCOL,
          protocolVersion: PROTOTYPE_STUDY_PROTOCOL_VERSION,
          taskId: session.taskId,
          arm: session.arm,
          participantSalt: session.participantSalt,
          arms: [...PROTOTYPE_STUDY_ARMS],
          eligibleForAggregation: eligibility.length === 0,
          ineligibleReasons: eligibility,
          statement: 'An authoring-time number from the assisted arm alone is not a result. ' +
            'It is only interpretable against unassisted-control sessions for the same taskId.',
        },
        session: {
          id: session.localSessionId,
          startedAt: session.startedAt,
          maxEventCount: session.maxEventCount,
          retention: session.retention,
        },
        events: session.events.map((event) => ({ ...event })),
        summary: summary(),
      };
    },
  });
}

function createElement(doc, tag, className, text) {
  const element = doc.createElement(tag);
  if (className) element.className = className;
  if (text != null) element.textContent = text;
  return element;
}

function formatDuration(milliseconds) {
  if (!Number.isFinite(milliseconds) || milliseconds < 0) return 'Not recorded';
  const seconds = Math.floor(milliseconds / 1000);
  const minutes = Math.floor(seconds / 60);
  return minutes ? `${minutes}m ${String(seconds % 60).padStart(2, '0')}s` : `${seconds}s`;
}

function metric(grid, label, value) {
  grid.append(
    createElement(grid.ownerDocument, 'dt', 'df-study-metric-label', label),
    createElement(grid.ownerDocument, 'dd', 'df-study-metric-value', value),
  );
}

/**
 * Builds the collapsed Results card. The controller exposes only an already-sanitized summary
 * and explicit file download/clear operations, so this renderer never receives flow content.
 */
export function renderPrototypeStudyEvidenceCard(controller, doc = typeof document === 'undefined' ? null : document) {
  if (!controller || !doc) return null;
  const summary = controller.summary?.();
  if (!summary || typeof summary !== 'object') return null;

  const card = createElement(doc, 'details', 'df-prototype-study-evidence');
  card.setAttribute('aria-label', 'Prototype evidence local only');
  card.append(createElement(doc, 'summary', null, 'Prototype evidence (local only)'));
  const content = createElement(doc, 'div', 'df-prototype-study-evidence-content');
  content.append(createElement(
    doc,
    'p',
    'df-workbench-note',
    'Local prototype-study evidence only. No telemetry or network egress. This is not qualification or platform proof.',
  ));

  const metrics = createElement(doc, 'dl', 'df-study-metrics');
  metric(metrics, 'Authoring', summary.authoringMode || 'unknown');
  metric(metrics, 'Goal', formatDuration(summary.timeToGoalMs));
  metric(metrics, 'Recording', formatDuration(summary.recordingDurationMs));
  metric(metrics, 'Review to save', formatDuration(summary.reviewToSaveDurationMs));
  metric(metrics, 'First result', formatDuration(summary.timeToFirstResultMs));
  metric(metrics, 'Steps / hard checks', `${summary.stepCount ?? '—'} / ${summary.hardAssertionCount ?? '—'}`);
  metric(
    metrics,
    'Durable selectors',
    summary.durableSelectorRatio == null
      ? `${summary.durableSelectorCount ?? 0} durable / ${summary.fragileSelectorCount ?? 0} fragile`
      : `${summary.durableSelectorCount} durable / ${summary.fragileSelectorCount} fragile (${Math.round(summary.durableSelectorRatio * 100)}%)`,
  );
  metric(metrics, 'Runs', `${summary.passed ?? 0} pass / ${summary.failed ?? 0} fail`);
  metric(metrics, 'Replay stability', summary.replayStability?.status || 'insufficient');
  metric(
    metrics,
    'Agent approvals',
    `${summary.agentApprovals?.approved ?? 0} approved / ${summary.agentApprovals?.rejected ?? 0} rejected / ${summary.agentApprovals?.pending ?? 0} pending`,
  );
  metric(metrics, 'Improve', `${summary.improve?.scans ?? 0} scan(s) / ${summary.improve?.findings ?? 0} finding(s)`);
  metric(
    metrics,
    'Repair funnel',
    `${summary.repair?.proposed ?? 0} proposed / ${summary.repair?.approved ?? 0} approved / ${summary.repair?.verified ?? 0} verified`,
  );
  const humanAttention = summary.humanInvolvement?.needsAttention;
  metric(
    metrics,
    'Human involvement',
    Array.isArray(humanAttention) && humanAttention.length
      ? humanAttention.join(', ')
      : 'No pending local review signal',
  );
  content.append(metrics);

  const limitations = Array.isArray(summary.limitations) ? summary.limitations : [];
  const limitationHeading = createElement(doc, 'h4', null, 'Limitations');
  const limitationList = createElement(doc, 'ul', 'df-study-limitations');
  for (const limitation of limitations) limitationList.append(createElement(doc, 'li', null, limitation));
  content.append(limitationHeading, limitationList);

  const controls = createElement(doc, 'div', 'df-authoring-actions');
  const status = createElement(doc, 'p', 'df-study-action-status');
  status.setAttribute('aria-live', 'polite');
  const download = createElement(doc, 'button', 'df-workbench-action', 'Download session evidence');
  download.type = 'button';
  download.addEventListener('click', () => {
    const downloaded = controller.downloadSessionEvidence?.();
    status.textContent = downloaded
      ? 'Downloaded a file-only local session evidence JSON document.'
      : 'Could not create the local session evidence download.';
  });
  const clear = createElement(doc, 'button', 'df-workbench-action', 'Clear local session evidence');
  clear.type = 'button';
  const cancel = createElement(doc, 'button', 'df-workbench-action', 'Keep local session evidence');
  cancel.type = 'button';
  cancel.hidden = true;
  let awaitingClearConfirmation = false;
  clear.addEventListener('click', () => {
    if (!awaitingClearConfirmation) {
      awaitingClearConfirmation = true;
      clear.textContent = 'Confirm clear local session evidence';
      cancel.hidden = false;
      status.textContent = 'Confirm clearing this tab-local evidence, or keep it.';
      return;
    }
    const cleared = controller.clearLocalSessionEvidence?.();
    status.textContent = cleared
      ? 'Local session evidence was cleared.'
      : 'Local session evidence could not be cleared.';
    awaitingClearConfirmation = false;
    clear.textContent = 'Clear local session evidence';
    cancel.hidden = true;
  });
  cancel.addEventListener('click', () => {
    awaitingClearConfirmation = false;
    clear.textContent = 'Clear local session evidence';
    cancel.hidden = true;
    status.textContent = 'Kept local session evidence.';
  });
  controls.append(download, clear, cancel);
  content.append(controls, status);
  card.append(content);
  return card;
}
