// Evidence capture (.mauitrace) for the Web Inspector.
//
// Nothing is downloaded until the user has seen — and confirmed — the server's plan describing
// exactly what the bundle will contain. The screenshot opt-in lives in that same dialog and is
// off every time the dialog opens; there is no "remembered" opt-in.

/** Formats a server plan into the rows the confirmation dialog renders. Pure — unit tested. */
export function formatEvidencePlan(plan) {
  const safe = plan && typeof plan === 'object' ? plan : {};
  const counts = safe.counts || {};
  const limits = safe.limits || {};
  const screenshot = safe.screenshot || {};
  const app = safe.app || {};
  const platform = safe.platform || {};

  const includes = (Array.isArray(safe.included) ? safe.included : []).map((entry) => ({
    name: String((entry && entry.name) || ''),
    detail: String((entry && entry.description) || ''),
    count: entry && typeof entry.count === 'number' ? entry.count : null,
  })).filter((entry) => entry.name);

  const excludes = (Array.isArray(safe.excluded) ? safe.excluded : []).map((entry) => ({
    name: String((entry && entry.name) || ''),
    detail: String((entry && entry.reason) || ''),
  })).filter((entry) => entry.name);

  const never = (Array.isArray(safe.neverIncluded) ? safe.neverIncluded : []).map(String);
  const warnings = (Array.isArray(safe.warnings) ? safe.warnings : []).map(String);

  const summaryParts = [];
  if (typeof counts.treeElements === 'number') summaryParts.push(`${counts.treeElements} elements`);
  if (typeof counts.problems === 'number') summaryParts.push(`${counts.problems} problems`);
  if (typeof counts.logs === 'number') summaryParts.push(`${counts.logs} log entries`);
  if (typeof counts.networkRequests === 'number') summaryParts.push(`${counts.networkRequests} request summaries`);
  if (typeof counts.layoutFindings === 'number') summaryParts.push(`${counts.layoutFindings} layout findings`);

  const limitParts = [];
  if (typeof limits.logs === 'number') limitParts.push(`${limits.logs} logs`);
  if (typeof limits.network === 'number') limitParts.push(`${limits.network} requests`);
  if (typeof limits.treeElements === 'number') limitParts.push(`${limits.treeElements} elements`);

  const subject = [app.name, platform.name].filter(Boolean).join(' · ');
  return {
    title: subject ? `Share evidence from ${subject}` : 'Share evidence bundle',
    summary: summaryParts.join(' · '),
    limits: limitParts.join(' · '),
    redaction: `Format v${safe.formatVersion || 1} · redaction ruleset v${safe.redactionVersion || 1}`,
    includes,
    excludes,
    never,
    warnings,
    screenshotRequested: !!screenshot.requested,
    screenshotNote: screenshot.included
      ? 'A screenshot will be included and may show on-screen data.'
      : (screenshot.omittedReason || 'No screenshot will be included.'),
    fileName: evidenceFileName(safe),
  };
}

/** Derives a safe download name from the plan (never trusts the server string blindly). */
export function evidenceFileName(plan) {
  const suggested = plan && typeof plan.suggestedFileName === 'string' ? plan.suggestedFileName : '';
  const base = suggested.split(/[\\/]/).pop() || '';
  const cleaned = base.replace(/[^A-Za-z0-9._-]/g, '-').replace(/^[.-]+/, '');
  if (cleaned && cleaned.toLowerCase().endsWith('.mauitrace')) return cleaned;
  const stamp = new Date().toISOString().replace(/[-:]/g, '').replace(/\..+$/, '').replace('T', '-');
  return `devflow-${stamp}.mauitrace`;
}

/**
 * Builds the preview request body. The authoritative preview must match the exact choices the
 * user confirmed before capture can proceed.
 */
export function buildPreviewBody({ choice, elementId, workflow }) {
  const confirmed = choice && typeof choice === 'object' ? choice : {};
  const body = {
    includeScreenshot: !!confirmed.includeScreenshot,
    includeWorkflow: !!confirmed.includeWorkflow,
  };
  if (elementId) body.elementId = String(elementId);
  if (confirmed.includeWorkflow && typeof workflow === 'string' && workflow.trim()) body.workflow = workflow;
  return body;
}

/**
 * Builds the capture request body. Pure — unit tested — because it is the last gate before app
 * data leaves the browser: anything not explicitly confirmed in the dialog must not appear here.
 */
export function buildCaptureBody({ choice, elementId, workflow }) {
  const confirmed = choice && typeof choice === 'object' ? choice : {};
  const body = { includeScreenshot: !!confirmed.includeScreenshot };
  if (elementId) body.elementId = String(elementId);
  if (confirmed.includeWorkflow && typeof workflow === 'string' && workflow.trim()) body.workflow = workflow;
  return body;
}

/**
 * Wires the Evidence toolbar action: preview → accessible confirmation → binary download.
 * `deps.api` is the shared inspector API (token-stamped POST helper).
 */
export function createEvidenceController(deps) {
  const {
    basePath,
    inspectorToken,
    api,
    setStatus,
    getSelectedId,
    getWorkflow,
    showEvidenceDialog: presentEvidenceDialog = showEvidenceDialog,
    showEvidenceFinalDialog: presentEvidenceFinalDialog = showEvidenceFinalDialog,
    downloadBlob: saveEvidenceBlob = downloadBlob,
  } = deps;
  let busy = false;

  async function previewEvidence(body) {
    const response = await api.postDetailed('/api/evidence/preview', body);
    return response && response.body && response.body.ok ? response.body.plan : null;
  }

  async function open() {
    if (busy) return;
    busy = true;
    try {
      setStatus('Preparing evidence preview…');
      const elementId = typeof getSelectedId === 'function' ? getSelectedId() : null;
      const workflow = typeof getWorkflow === 'function' ? getWorkflow() : null;
      const hasWorkflow = typeof workflow === 'string' && !!workflow.trim();
      const initialPlan = await previewEvidence(buildPreviewBody({
        choice: { includeScreenshot: false, includeWorkflow: false },
        elementId: elementId || undefined,
      }));
      if (!initialPlan) {
        setStatus('Could not prepare the evidence preview.');
        return;
      }

      const initialView = formatEvidencePlan(initialPlan);
      const choice = await presentEvidenceDialog(initialView, { hasWorkflow });
      if (!choice) {
        setStatus('Evidence capture cancelled.');
        return;
      }

      const needsAuthoritativeReview = choice.includeScreenshot || choice.includeWorkflow;
      const authoritativePlan = needsAuthoritativeReview
        ? await previewEvidence(buildPreviewBody({ choice, elementId: elementId || undefined, workflow }))
        : initialPlan;
      if (!authoritativePlan) {
        setStatus('Could not refresh the evidence preview.');
        return;
      }

      const authoritativeView = formatEvidencePlan(authoritativePlan);
      if (needsAuthoritativeReview) {
        setStatus('Review the final evidence preview before downloading…');
        const confirmed = await presentEvidenceFinalDialog(authoritativeView, { hasWorkflow, choice });
        if (!confirmed) {
          setStatus('Evidence capture cancelled.');
          return;
        }
      }
      setStatus(choice.includeScreenshot ? 'Capturing evidence with a screenshot…' : 'Capturing evidence…');
      const headers = { 'Content-Type': 'application/json' };
      if (inspectorToken) headers['X-DevFlow-Inspector-Token'] = inspectorToken;
      const captured = await fetch(`${basePath}/api/evidence/capture`, {
        method: 'POST',
        headers,
        body: JSON.stringify(buildCaptureBody({ choice, elementId, workflow })),
      });
      if (!captured.ok) {
        setStatus(`Could not capture evidence (${captured.status}).`);
        return;
      }

      const blob = await captured.blob();
      saveEvidenceBlob(blob, authoritativeView.fileName);
      setStatus(`Evidence bundle downloaded: ${authoritativeView.fileName}`);
    } catch (error) {
      console.error('evidence capture failed:', error);
      setStatus('Could not capture evidence.');
    } finally {
      busy = false;
    }
  }

  return Object.freeze({ open });
}

function downloadBlob(blob, fileName) {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

/**
 * Modal preview + confirmation. Resolves with `{ includeScreenshot, includeWorkflow }` on confirm,
 * or null on cancel. Every opt-in starts unchecked on every open, and each one states exactly what
 * it adds beyond the previewed plan. Keyboard accessible: labelled dialog, focus trap, Escape to
 * cancel, focus restored.
 */
function showEvidenceDialog(view, options) {
  return new Promise((resolve) => {
    renderEvidenceDialog(view, options, resolve, false);
  });
}

function showEvidenceFinalDialog(view, options) {
  return new Promise((resolve) => {
    renderEvidenceDialog(view, options, resolve, true);
  });
}

function renderEvidenceDialog(view, options, resolve, finalReview) {
  const hasWorkflow = !!(options && options.hasWorkflow);
  const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
  const backdrop = document.createElement('div');
  backdrop.className = 'df-evidence-backdrop';
  const box = document.createElement('div');
  box.className = 'df-evidence-dialog';
  box.setAttribute('role', 'dialog');
  box.setAttribute('aria-modal', 'true');

  const heading = document.createElement('h2');
  heading.id = 'df-evidence-title';
  heading.textContent = view.title;
  box.setAttribute('aria-labelledby', heading.id);

  const description = document.createElement('p');
  description.id = 'df-evidence-desc';
  description.className = 'df-evidence-desc';
  description.textContent = `${view.summary || 'No app data was available.'} — ${view.redaction}.${finalReview ? ' Final review — this is the exact bundle that will be downloaded if you confirm.' : ''}`;
  box.setAttribute('aria-describedby', description.id);

  box.append(heading, description);
  box.appendChild(buildList('Included', view.includes.map(entryLabel)));
  if (view.excludes.length) box.appendChild(buildList('Excluded', view.excludes.map(entryLabel)));
  box.appendChild(buildList('Never captured', view.never));
  if (view.limits) {
    const limits = document.createElement('p');
    limits.className = 'df-evidence-desc';
    limits.textContent = `Limits: ${view.limits}.`;
    box.appendChild(limits);
  }
  if (view.warnings.length) box.appendChild(buildList('Warnings', view.warnings, 'df-evidence-warn'));
  if (!finalReview) {
    const note = document.createElement('p');
    note.className = 'df-evidence-desc';
    note.textContent = 'If you opt in to workflow or screenshot capture, DevFlow will refresh this preview and ask you to confirm the final bundle again.';
    box.appendChild(note);
  }

  if (!finalReview) {
    const screenshotRow = document.createElement('div');
    screenshotRow.className = 'df-evidence-opt';
    const checkbox = document.createElement('input');
    checkbox.type = 'checkbox';
    checkbox.id = 'df-evidence-screenshot';
    checkbox.checked = false;
    const checkboxLabel = document.createElement('label');
    checkboxLabel.htmlFor = checkbox.id;
    checkboxLabel.textContent = 'Also include a screenshot (it may show on-screen data)';
    screenshotRow.append(checkbox, checkboxLabel);
    box.appendChild(screenshotRow);

    let workflowCheckbox = null;
    if (hasWorkflow) {
      const workflowRow = document.createElement('div');
      workflowRow.className = 'df-evidence-opt';
      workflowCheckbox = document.createElement('input');
      workflowCheckbox.type = 'checkbox';
      workflowCheckbox.id = 'df-evidence-workflow';
      workflowCheckbox.checked = false;
      const workflowLabel = document.createElement('label');
      workflowLabel.htmlFor = workflowCheckbox.id;
      workflowLabel.textContent = 'Also include the loaded workflow steps (they quote text and values you typed)';
      workflowRow.append(workflowCheckbox, workflowLabel);
      box.appendChild(workflowRow);
    }

    const actions = document.createElement('div');
    actions.className = 'df-evidence-actions';
    const cancel = document.createElement('button');
    cancel.type = 'button';
    cancel.className = 'df-tool-btn';
    cancel.textContent = 'Cancel';
    const confirm = document.createElement('button');
    confirm.type = 'button';
    confirm.className = 'df-tool-btn df-evidence-primary';
    confirm.textContent = 'Download bundle';
    actions.append(cancel, confirm);
    box.appendChild(actions);

    let done = false;
    const focusables = [checkbox, workflowCheckbox, cancel, confirm].filter(Boolean);
    const finish = (value) => {
      if (done) return;
      done = true;
      document.removeEventListener('keydown', onKey, true);
      backdrop.remove();
      if (previousFocus && previousFocus.isConnected) previousFocus.focus();
      resolve(value);
    };
    const onKey = (event) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        event.stopImmediatePropagation();
        finish(null);
        return;
      }
      if (event.key !== 'Tab') return;
      const first = focusables[0];
      const last = focusables[focusables.length - 1];
      if (!box.contains(document.activeElement)) {
        event.preventDefault();
        (event.shiftKey ? last : first).focus();
        return;
      }
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };

    cancel.addEventListener('click', () => finish(null));
    confirm.addEventListener('click', () => finish({
      includeScreenshot: checkbox.checked,
      includeWorkflow: !!(workflowCheckbox && workflowCheckbox.checked),
    }));
    backdrop.addEventListener('click', (event) => { if (event.target === backdrop) finish(null); });
    document.addEventListener('keydown', onKey, true);

    backdrop.appendChild(box);
    document.body.appendChild(backdrop);
    cancel.focus();
    return;
  }

  const actions = document.createElement('div');
  actions.className = 'df-evidence-actions';
  const cancel = document.createElement('button');
  cancel.type = 'button';
  cancel.className = 'df-tool-btn';
  cancel.textContent = 'Cancel';
  const confirm = document.createElement('button');
  confirm.type = 'button';
  confirm.className = 'df-tool-btn df-evidence-primary';
  confirm.textContent = 'Confirm and download';
  actions.append(cancel, confirm);
  box.appendChild(actions);

  let done = false;
  const focusables = [cancel, confirm];
  const finish = (value) => {
    if (done) return;
    done = true;
    document.removeEventListener('keydown', onKey, true);
    backdrop.remove();
    if (previousFocus && previousFocus.isConnected) previousFocus.focus();
    resolve(value);
  };
  const onKey = (event) => {
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopImmediatePropagation();
      finish(null);
      return;
    }
    if (event.key !== 'Tab') return;
    const first = focusables[0];
    const last = focusables[focusables.length - 1];
    if (!box.contains(document.activeElement)) {
      event.preventDefault();
      (event.shiftKey ? last : first).focus();
      return;
    }
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  };

  cancel.addEventListener('click', () => finish(null));
  confirm.addEventListener('click', () => finish(true));
  backdrop.addEventListener('click', (event) => { if (event.target === backdrop) finish(null); });
  document.addEventListener('keydown', onKey, true);

  backdrop.appendChild(box);
  document.body.appendChild(backdrop);
  cancel.focus();
}

function entryLabel(entry) {
  const count = typeof entry.count === 'number' ? ` (${entry.count})` : '';
  return entry.detail ? `${entry.name}${count} — ${entry.detail}` : `${entry.name}${count}`;
}

function buildList(title, items, extraClass) {
  const section = document.createElement('section');
  section.className = 'df-evidence-section' + (extraClass ? ' ' + extraClass : '');
  const heading = document.createElement('h3');
  heading.textContent = title;
  const list = document.createElement('ul');
  for (const item of items) {
    const li = document.createElement('li');
    li.textContent = item;
    list.appendChild(li);
  }
  section.append(heading, list);
  return section;
}
