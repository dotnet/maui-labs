const LIST_KEYS = Object.freeze({
  actions: 'allowedActions',
  selectors: 'allowedSelectors',
  routes: 'allowedRoutes',
  sideEffects: 'allowedSideEffectClasses',
});

function strings(values) {
  return [...new Set((Array.isArray(values) ? values : [])
    .filter((value) => typeof value === 'string' && value.trim())
    .map((value) => value.trim()))];
}

function readableAction(value) {
  return {
    'author-commit': 'save the test',
    run: 'run the test',
    cancel: 'cancel the run',
    assert: 'check an expected result',
    tap: 'tap a control',
    fill: 'enter text',
    scroll: 'scroll',
    navigate: 'navigate',
    back: 'go back',
  }[value] || readable(value);
}

export function agentRequestStarterPrompt(
  appName = null,
  platform = null,
  agentId = null,
  agentInstanceId = null
) {
  const target = [appName, platform].filter(Boolean).join(' on ');
  return [
    'Use only the restricted DevFlow test-agent tools.',
    agentId && agentInstanceId
      ? `Target exactly agentId "${String(agentId).slice(0, 256)}" and agentInstanceId "${String(agentInstanceId).slice(0, 256)}".`
      : 'Resolve an exact agentId and agentInstanceId before continuing.',
    target
      ? `Discover and explicitly target ${target}.`
      : 'Discover and explicitly target the connected app.',
    'Help me define the Goal if needed, then prepare the complete test draft with steps and expected results.',
    'Request one commit review, then wait. Do not run until I approve a separate run request.',
    'Chat approval or affirmative text expresses intent only; it does not authorize commit or run.',
    'A current Test Workbench broker grant is required separately for each commit or run.',
    'Do not apply repairs or source changes automatically.',
  ].join(' ');
}

export function normalizeAgentRequestScope(scope = {}) {
  return {
    allowedActions: strings(scope.allowedActions),
    allowedSelectors: strings(scope.allowedSelectors),
    allowedRoutes: strings(scope.allowedRoutes),
    allowedSideEffectClasses: strings(scope.allowedSideEffectClasses),
    maxActionCount: Number.isInteger(scope.maxActionCount) && scope.maxActionCount > 0
      ? scope.maxActionCount
      : 1,
    maxValueBytes: Number.isInteger(scope.maxValueBytes) && scope.maxValueBytes >= 0
      ? scope.maxValueBytes
      : 0,
  };
}

export function isNarrowedAgentRequestScope(requested, approved) {
  const original = normalizeAgentRequestScope(requested);
  const candidate = normalizeAgentRequestScope(approved);
  const subset = (values, allowed) => values.every((value) => allowed.includes(value));
  return candidate.allowedActions.length > 0 &&
    subset(candidate.allowedActions, original.allowedActions) &&
    subset(candidate.allowedSelectors, original.allowedSelectors) &&
    subset(candidate.allowedRoutes, original.allowedRoutes) &&
    subset(candidate.allowedSideEffectClasses, original.allowedSideEffectClasses) &&
    candidate.maxActionCount <= original.maxActionCount &&
    candidate.maxValueBytes <= original.maxValueBytes;
}

export function agentRequestSummary(request) {
  const scope = normalizeAgentRequestScope(request?.requestedScope);
  const actions = scope.allowedActions.map(readableAction);
  const actionText = actions.length === 1
    ? actions[0]
    : `${scope.allowedActions.length} action types`;
  const selectorText = scope.allowedSelectors.length === 0
    ? 'no element selectors'
    : scope.allowedSelectors.length === 1
      ? '1 exact selector'
      : `${scope.allowedSelectors.length} exact selectors`;
  const effects = strings(request?.deviceEffects);
  const deviceText = effects.length
    ? `; exact device changes: ${effects.join('; ')}`
    : '';
  return `${actionText}, ${selectorText}, up to ${scope.maxActionCount} action${scope.maxActionCount === 1 ? '' : 's'}${deviceText}`;
}

export function agentRequestGrantDurationSeconds(request) {
  return request?.kind === 'run' ? 300 : 600;
}

function readable(value) {
  return String(value || 'unknown').replace(/-/g, ' ');
}

function requestTitle(kind) {
  return {
    commit: 'Your agent prepared a test',
    run: 'Your agent would like to run this test once',
    'draft-change': 'Your agent suggests a test update',
    assertion: 'Your agent suggests expected results',
    exploration: 'Your agent would like to inspect test controls',
  }[kind] || `Review ${readable(kind)}`;
}

function formatExpiry(value) {
  const time = Date.parse(value || '');
  if (!Number.isFinite(time)) return 'unknown';
  const seconds = Math.max(0, Math.ceil((time - Date.now()) / 1000));
  if (seconds < 60) return `${seconds}s`;
  return `${Math.ceil(seconds / 60)}m`;
}

function appendText(doc, parent, tag, text, className = null) {
  const element = doc.createElement(tag);
  if (className) element.className = className;
  element.textContent = text;
  parent.append(element);
  return element;
}

function scopeGroup(doc, parent, label, values) {
  if (values.length === 0) return;
  const group = doc.createElement('div');
  group.className = 'df-agent-request-scope-group';
  appendText(doc, group, 'strong', label);
  const list = doc.createElement('ul');
  list.className = 'df-workbench-list';
  for (const value of values) {
    appendText(doc, list, 'li', value);
  }
  group.append(list);
  parent.append(group);
}

export function createAgentRequestController(options = {}) {
  const doc = options.document || document;
  const win = options.window || window;
  const api = options.inspectorApi;
  const hostBridge = options.hostBridge || null;
  const panel = options.panel || doc.getElementById('df-agent-requests');
  const body = options.body || doc.getElementById('df-agent-requests-body');
  const tab = options.tab || doc.getElementById('df-workbench-tab-requests');
  const toolbarBadge = options.toolbarBadge || doc.getElementById('df-test-agent-request-badge');
  const tabBadge = options.tabBadge || doc.getElementById('df-agent-requests-badge');
  const workbenchToggle = options.workbenchToggle || doc.getElementById('df-toggle-workbench');
  const baseWorkbenchAvailable = options.baseWorkbenchAvailable === true;
  // True when the approval inbox is the whole Tests panel because the guided journey is disabled.
  // Then the tab has to stay visible while empty, or the panel opens with no tab at all.
  const requestsArePrimary = options.requestsArePrimary === true;
  const onAvailabilityChanged = typeof options.onAvailabilityChanged === 'function'
    ? options.onAvailabilityChanged
    : () => {};
  const openPanel = typeof options.openPanel === 'function' ? options.openPanel : () => {};
  const setStatus = typeof options.setStatus === 'function' ? options.setStatus : () => {};
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
  const onTransition = typeof options.onTransition === 'function' ? options.onTransition : () => {};
  const agentId = options.agentId || null;
  const agentInstanceId = options.agentInstanceId || null;
  if (!api || !panel || !body) {
    return Object.freeze({
      start: () => {},
      stop: () => {},
      refresh: async () => {},
      pendingCount: () => 0,
    });
  }

  const busy = new Set();
  const expandedRequests = new Set();
  const knownStates = new Map();
  let requests = [];
  let appName = null;
  let platform = null;
  let brokerApprovalAvailable = false;
  let timer = null;
  let stopped = false;
  let responseFingerprint = null;
  let needsRender = false;

  function nativeApprovalAvailable() {
    return brokerApprovalAvailable && hostBridge?.has?.('nativeApproval') === true;
  }

  function updateBadge(element, count) {
    if (!element) return;
    if (count === 0) {
      element.hidden = true;
      element.textContent = '';
      element.removeAttribute('aria-label');
      return;
    }
    element.hidden = false;
    element.textContent = count > 99 ? '99+' : String(count);
    element.setAttribute('aria-label', `${count} pending agent request${count === 1 ? '' : 's'}`);
  }

  function syncChrome() {
    const pending = requests.filter((request) => request.state === 'pending').length;
    const available = requests.length > 0;
    if (workbenchToggle)
      workbenchToggle.hidden = !baseWorkbenchAvailable && !available;
    updateBadge(toolbarBadge, pending);
    updateBadge(tabBadge, pending);
    if (tab) {
      tab.dataset.available = String(available);
      tab.hidden = !available && !requestsArePrimary;
      tab.classList.toggle('df-agent-request-pending', pending > 0);
      tab.disabled = !available && !requestsArePrimary && !tab.classList.contains('df-active');
      tab.setAttribute('aria-disabled', String(tab.disabled));
      const label = pending > 0
        ? `Agent requests, ${pending} pending`
        : 'Agent requests';
      tab.setAttribute('aria-label', label);
      tab.title = !available
        ? 'Agent requests appear here after your agent prepares a test or asks to run it.'
        : pending > 0
        ? `${pending} agent request${pending === 1 ? '' : 's'} waiting for review`
        : 'Review requests from test agents';
      onAvailabilityChanged();
    }
  }

  function renderScope(request, pending, expandByDefault = false) {
    const requested = normalizeAgentRequestScope(request.requestedScope);
    const details = doc.createElement('details');
    details.className = 'df-agent-request-details';
    details.open = expandedRequests.has(request.approvalRequestId) || expandByDefault;
    details.addEventListener('toggle', () => {
      if (details.open) expandedRequests.add(request.approvalRequestId);
      else expandedRequests.delete(request.approvalRequestId);
    });
    const summary = doc.createElement('summary');
    summary.textContent = pending ? 'Review what your agent can do' : 'Reviewed permissions';
    details.append(summary);
    appendText(
      doc,
      details,
      'p',
      pending
        ? nativeApprovalAvailable()
          ? 'Narrow this exact scope if needed, then review it in the trusted native host.'
          : 'This surface is read-only. A trusted native host with native approval is required to approve, narrow, or reject.'
        : 'This is the exact scope that was reviewed.',
      'df-agent-request-help'
    );

    const grid = doc.createElement('div');
    grid.className = 'df-agent-request-scope';
    scopeGroup(doc, grid, 'Actions', requested.allowedActions);
    scopeGroup(doc, grid, 'Controls', requested.allowedSelectors);
    scopeGroup(doc, grid, 'Routes', requested.allowedRoutes);
    scopeGroup(doc, grid, 'App changes', requested.allowedSideEffectClasses);
    const deviceEffects = strings(request.deviceEffects);
    if (deviceEffects.length > 0)
      scopeGroup(doc, grid, 'Exact device changes', deviceEffects);

    const limits = doc.createElement('div');
    limits.className = 'df-agent-request-limits';
    for (const [label, value] of [
      ['Maximum actions', requested.maxActionCount],
      ['Maximum value bytes', requested.maxValueBytes],
    ]) {
      const row = doc.createElement('p');
      row.className = 'df-agent-request-limit';
      const name = doc.createElement('strong');
      name.textContent = `${label}: `;
      row.append(name);
      appendText(doc, row, 'span', String(value));
      limits.append(row);
    }
    grid.append(limits);
    details.append(grid);
    return details;
  }

  function renderNativeApprovalControls(request, review) {
    const requested = normalizeAgentRequestScope(request.requestedScope);
    const controls = [];
    const fieldsets = [
      ['Actions', 'allowedActions', requested.allowedActions],
      ['Controls', 'allowedSelectors', requested.allowedSelectors],
      ['Routes', 'allowedRoutes', requested.allowedRoutes],
      ['App changes', 'allowedSideEffectClasses', requested.allowedSideEffectClasses],
    ];
    const narrowing = doc.createElement('div');
    narrowing.className = 'df-agent-request-narrowing';
    appendText(doc, narrowing, 'p',
      'Select a subset only. Limits can only be reduced. The native host will show this exact scope before approval.',
      'df-agent-request-help');
    for (const [label, key, values] of fieldsets) {
      if (values.length === 0) continue;
      const fieldset = doc.createElement('fieldset');
      fieldset.className = 'df-agent-request-scope-group';
      appendText(doc, fieldset, 'legend', label);
      for (const value of values) {
        const row = doc.createElement('label');
        row.className = 'df-agent-request-choice';
        const checkbox = doc.createElement('input');
        checkbox.type = 'checkbox';
        checkbox.checked = true;
        checkbox.dataset.scopeKey = key;
        checkbox.value = value;
        row.append(checkbox);
        appendText(doc, row, 'span', value);
        fieldset.append(row);
        controls.push(checkbox);
      }
      narrowing.append(fieldset);
    }

    const limits = doc.createElement('div');
    limits.className = 'df-agent-request-limits';
    function limitInput(label, key, min, max) {
      const row = doc.createElement('label');
      row.className = 'df-agent-request-limit';
      appendText(doc, row, 'span', label);
      const input = doc.createElement('input');
      input.type = 'number';
      input.min = String(min);
      input.max = String(max);
      input.value = String(max);
      input.dataset.scopeLimit = key;
      row.append(input);
      limits.append(row);
      return input;
    }
    const actionCount = limitInput('Maximum actions', 'maxActionCount', 1, requested.maxActionCount);
    const valueBytes = limitInput('Maximum value bytes', 'maxValueBytes', 0, requested.maxValueBytes);
    narrowing.append(limits);

    const confirmed = doc.createElement('label');
    confirmed.className = 'df-agent-request-human-confirm';
    const confirmation = doc.createElement('input');
    confirmation.type = 'checkbox';
    confirmation.dataset.humanReviewed = 'true';
    confirmed.append(confirmation);
    appendText(doc, confirmed, 'span', 'I reviewed this exact scope and want the trusted native host to ask for approval.');
    narrowing.append(confirmed);

    const actions = doc.createElement('div');
    actions.className = 'df-agent-request-actions';
    const approve = doc.createElement('button');
    approve.type = 'button';
    approve.className = 'df-workbench-action df-workbench-action-primary';
    approve.textContent = busy.has(request.approvalRequestId) ? 'Working...' : 'Approve in native host';
    const reject = doc.createElement('button');
    reject.type = 'button';
    reject.className = 'df-workbench-action';
    reject.textContent = 'Reject';

    const approvedScope = () => {
      const selection = {};
      for (const key of Object.values(LIST_KEYS))
        selection[key] = controls
          .filter((checkbox) => checkbox.dataset.scopeKey === key && checkbox.checked)
          .map((checkbox) => checkbox.value);
      selection.maxActionCount = Number(actionCount.value);
      selection.maxValueBytes = Number(valueBytes.value);
      return normalizeAgentRequestScope(selection);
    };
    const sync = () => {
      const valid = isNarrowedAgentRequestScope(requested, approvedScope()) && confirmation.checked;
      approve.disabled = busy.has(request.approvalRequestId) || !valid;
      reject.disabled = busy.has(request.approvalRequestId);
    };
    for (const control of [...controls, actionCount, valueBytes, confirmation])
      control.addEventListener('change', sync);
    approve.addEventListener('click', () => approveRequest(request, approvedScope(), confirmation.checked));
    reject.addEventListener('click', () => rejectRequest(request));
    sync();
    actions.append(approve, reject);
    narrowing.append(actions);
    review.append(narrowing);
  }

  function renderRequest(request, expandByDefault = false) {
    const pending = request.state === 'pending';
    const card = doc.createElement('article');
    card.className = `df-agent-request-card df-agent-request-${request.state || 'unknown'}`;
    card.dataset.approvalRequestId = request.approvalRequestId || '';

    const header = doc.createElement('div');
    header.className = 'df-agent-request-card-header';
    appendText(doc, header, 'h4', requestTitle(request.kind));
    appendText(doc, header, 'span', readable(request.state), 'df-agent-request-state');
    card.append(header);
    appendText(doc, card, 'p', request.intent || 'No user-visible intent was supplied.', 'df-agent-request-intent');
    appendText(
      doc,
      card,
      'p',
      `${agentRequestSummary(request)} · expires in ${formatExpiry(request.expiresAt)}`,
      'df-agent-request-meta'
    );
    if (pending) {
      const review = renderScope(request, true, expandByDefault);
      card.append(review);
      const id = request.approvalRequestId;
      if (nativeApprovalAvailable()) {
        renderNativeApprovalControls(request, review);
      } else {
        appendText(
          doc,
          review,
          'p',
          'Native approval is unavailable in this surface. Browser or chat text cannot approve, narrow, reject, or issue a grant.',
          'df-workbench-safety'
        );
      }
    } else {
      const message = {
        approved: 'This request is no longer pending. Browser preview cannot establish usable authority or agent continuation.',
        consumed: 'Completed. This approval was used and cannot be used again.',
        rejected: 'Rejected. Your agent cannot continue with this request.',
        expired: 'Expired. Ask your agent to submit a fresh request.',
        stale: 'The app or test changed. Ask your agent to submit a fresh request.',
      }[request.state] || 'This request is no longer pending.';
      appendText(doc, card, 'p', message, 'df-agent-request-result');
      const reviewed = doc.createElement('details');
      reviewed.className = 'df-agent-request-details';
      const summary = doc.createElement('summary');
      summary.textContent = 'Reviewed scope';
      reviewed.append(summary);
      appendText(doc, reviewed, 'p', agentRequestSummary(request), 'df-agent-request-help');
      card.append(reviewed);
    }
    return card;
  }

  function render() {
    body.replaceChildren();
    const pending = requests.filter((request) => request.state === 'pending');
    const recent = requests.filter((request) => request.state !== 'pending').slice(0, 6);
    if (requests.length === 0) {
      const empty = doc.createElement('section');
      empty.className = 'df-authoring-section df-tool-empty-state df-agent-request-empty-state';
      appendText(doc, empty, 'h4', 'Work with your agent');
      appendText(
        doc,
        empty,
        'p',
        `Ask your coding agent to prepare or improve a DevFlow test${appName ? ` for ${appName}` : ''}. Its save and run requests will appear here for review.`,
        'df-workbench-intro'
      );
      const steps = doc.createElement('ol');
      steps.className = 'df-workbench-list';
      for (const text of [
        'Your agent prepares the draft and expected results.',
        'You review the test before saving it.',
        'You separately decide whether to run it once.',
      ]) appendText(doc, steps, 'li', text);
      empty.append(steps);
      const copy = doc.createElement('button');
      copy.type = 'button';
      copy.className = 'df-workbench-action';
      copy.textContent = 'Copy prompt for your agent';
      copy.addEventListener('click', async () => {
        const copied = await copyText(agentRequestStarterPrompt(
          appName,
          platform,
          agentId,
          agentInstanceId
        ));
        setStatus(copied
          ? 'Copied instructions. Paste them into your coding agent chat.'
          : 'Could not copy the agent prompt.');
      });
      empty.append(copy);
      body.append(empty);
      syncChrome();
      return;
    }

    if (pending.length > 0) {
      appendText(
        doc,
        body,
        'p',
        `${pending.length === 1 ? 'One agent request is' : `${pending.length} agent requests are`} pending. ${nativeApprovalAvailable()
          ? 'Review the exact scope, then use the trusted native host confirmation.'
          : 'This surface is read-only; a trusted native host with native approval is required.'}`,
        'df-agent-request-intro'
      );
      pending.forEach((request, index) => body.append(renderRequest(request, index === 0)));
    }
    if (recent.length > 0) {
      const history = doc.createElement('details');
      history.className = 'df-agent-request-history';
      const summary = doc.createElement('summary');
      summary.textContent = `Recent decisions (${recent.length})`;
      history.append(summary);
      for (const request of recent) history.append(renderRequest(request));
      body.append(history);
    }
    syncChrome();
  }

  async function rejectRequest(request) {
    const id = request.approvalRequestId;
    busy.add(id);
    render();
    const response = await api.postDetailed(`/api/workbench/agent-requests/${encodeURIComponent(id)}/reject`, {
      humanConfirmed: true,
      reasonCode: 'human-rejected',
    });
    busy.delete(id);
    if (!response.ok || !response.body?.ok) {
      setStatus(response.body?.error?.message || response.body?.error || response.error || 'Agent request rejection failed.');
    } else {
      setStatus(response.body.message || 'Agent request rejected.');
    }
    await refresh(true);
  }

  async function approveRequest(request, approvedScope, humanReviewed) {
    if (!humanReviewed || !isNarrowedAgentRequestScope(request.requestedScope, approvedScope)) {
      setStatus('Review a non-empty subset of the requested actions and explicit bounded limits before approval.');
      return;
    }
    if (!nativeApprovalAvailable()) {
      setStatus('Native approval is unavailable in this surface.');
      return;
    }
    const approvalRequestId = typeof request.approvalRequestId === 'string'
      ? request.approvalRequestId.slice(0, 256)
      : '';
    if (!approvalRequestId || approvalRequestId !== request.approvalRequestId) {
      setStatus('The approval request identifier is invalid. Refresh and review a new request.');
      return;
    }
    const id = approvalRequestId;
    busy.add(id);
    render();
    const result = await hostBridge.request('nativeApproval', {
      approvalRequestId,
      kind: typeof request.kind === 'string' ? request.kind.slice(0, 64) : '',
      intent: typeof request.intent === 'string' ? request.intent.slice(0, 1024) : '',
      approvedScope,
      grantDurationSeconds: agentRequestGrantDurationSeconds(request),
      appName: typeof appName === 'string' ? appName.slice(0, 256) : '',
      platform: typeof platform === 'string' ? platform.slice(0, 128) : '',
      scopeSummary: agentRequestSummary({ ...request, requestedScope: approvedScope }).slice(0, 1024),
    });
    busy.delete(id);
    if (!result?.ok) {
      setStatus(result?.message || result?.error || 'Native approval was cancelled or could not be completed.');
    } else {
      setStatus(result.message || 'Approved by the trusted native host.');
    }
    await refresh(true);
  }

  async function refresh(force = false) {
    if (stopped) return;
    const response = await api.getDetailed('/api/workbench/agent-requests');
    if (!response.ok || !response.body?.ok) {
      if (requests.length === 0 && (!panel.contains?.(doc.activeElement) || force)) render();
      return;
    }
    const nextRequests = Array.isArray(response.body.requests) ? response.body.requests : [];
    const nextAppName = response.body.appName || null;
    const nextPlatform = response.body.platform || null;
    const nextBrokerApprovalAvailable = response.body.approvalAvailable === true;
    const nextFingerprint = JSON.stringify({
      appName: nextAppName,
      platform: nextPlatform,
      requests: nextRequests,
    });
    if (!force && nextFingerprint === responseFingerprint && !needsRender) return;
    for (const request of nextRequests) {
      const id = request.approvalRequestId;
      const state = request.state;
      if (!id || !state) continue;
      const previous = knownStates.get(id);
      if (previous === undefined && state === 'pending') {
        onTransition('agent-requested', { approvalRequestId: id, durationMs: 0 });
        setStatus('An agent request is waiting in Tests > Agent requests.');
      } else if (previous && previous !== state) {
        const createdAt = Date.parse(request.createdAt || '');
        onTransition(`agent-${state}`, {
          approvalRequestId: id,
          durationMs: Number.isFinite(createdAt) ? Math.max(0, Date.now() - createdAt) : null,
        });
      }
      knownStates.set(id, state);
    }
    responseFingerprint = nextFingerprint;
    requests = nextRequests;
    appName = nextAppName;
    platform = nextPlatform;
    brokerApprovalAvailable = nextBrokerApprovalAvailable;
    const panelFocused = panel.contains?.(doc.activeElement);
    if (!force && panelFocused) {
      // Polling must keep the waiting count and badges current, but replacing this
      // panel while someone is reviewing it discards their focus and disclosure state.
      needsRender = true;
      syncChrome();
      return;
    }
    render();
    needsRender = false;
  }

  function start() {
    if (timer || stopped) return;
    hostBridge?.onCapabilitiesChanged?.(() => {
      if (!stopped) render();
    });
    refresh();
    timer = win.setInterval(refresh, 2000);
  }

  async function openRequest(approvalRequestId = null) {
    openPanel();
    await refresh(true);
    const card = approvalRequestId
      ? [...body.querySelectorAll('[data-approval-request-id]')]
        .find((candidate) => candidate.dataset.approvalRequestId === approvalRequestId)
      : null;
    if (approvalRequestId && !card) {
      setStatus('That agent request is no longer available. Review recent decisions or wait for a fresh request.');
    }
    const focusTarget = card?.querySelector('input, button, summary') ||
      body.querySelector('input, button, summary');
    card?.scrollIntoView?.({ block: 'nearest' });
    focusTarget?.focus?.({ preventScroll: true });
  }

  function stop() {
    stopped = true;
    if (timer) win.clearInterval(timer);
    timer = null;
  }

  return Object.freeze({
    start,
    stop,
    refresh,
    pendingCount: () => requests.filter((request) => request.state === 'pending').length,
    open: openRequest,
  });
}
