// Read-only capture: preview options, review attachments, then download locally.
export function formatEvidencePlan(plan) {
  const safe = plan && typeof plan === 'object' ? plan : {};
  const counts = safe.counts || {};
  const limits = safe.limits || {};
  const screenshot = safe.screenshot || {};
  const includedNames = new Set(list(safe.included).map(entry => entry?.name));
  const summary = [
    ['treeElements', 'tree.json', 'element', 'elements'],
    ['layoutFindings', 'layout.json', 'layout finding', 'layout findings'],
    ['problems', 'problems.json', 'problem', 'problems'],
    ['logs', 'logs.json', 'log entry', 'log entries'],
    ['networkRequests', 'network.json', 'request summary', 'request summaries'],
  ].filter(([key, entry]) => includedNames.has(entry) && Number.isFinite(counts[key]) && counts[key] >= 0)
    .map(([key, , singular, plural]) => `${counts[key]} ${counts[key] === 1 ? singular : plural}`);
  const limitText = [
    ['logs', 'logs'], ['network', 'requests'], ['treeElements', 'elements'],
  ].filter(([key]) => Number.isFinite(limits[key])).map(([key, label]) => `${limits[key]} ${label}`);
  const subject = [safe.app?.name, safe.platform?.name].filter(Boolean).join(' \u00b7 ');
  return {
    title: subject ? `Share evidence from ${subject}` : 'Share evidence bundle',
    summary: summary.join(' \u00b7 '),
    limits: limitText.join(' \u00b7 '),
    redaction: `Format v${safe.formatVersion || 1} \u00b7 redaction ruleset v${safe.redactionVersion || 1}`,
    includes: list(safe.included).filter(entry => entry?.name).map(entry => ({
      name: String(entry.name),
      detail: String(entry.description || ''),
      count: Number.isFinite(entry.count) ? entry.count : null,
    })),
    excludes: list(safe.excluded).filter(entry => entry?.name).map(entry => ({
      name: String(entry.name), detail: String(entry.reason || ''),
    })),
    never: list(safe.neverIncluded).map(String),
    warnings: list(safe.warnings).map(String),
    environment: formatEvidenceEnvironment(safe.environment),
    screenshotRequested: screenshot.requested === true,
    screenshotNote: screenshot.included === true
      ? 'A screenshot was requested for capture and may show on-screen data.'
      : String(screenshot.omittedReason || 'No screenshot will be included.'),
    fileName: evidenceFileName(safe),
  };
}

export function formatEvidenceEnvironment(environment) {
  const value = environment && typeof environment === 'object' ? environment : {};
  const rows = [];
  const add = (label, text) => { if (text) rows.push({ label, value: String(text) }); };
  const dimensions = (metrics, unit) =>
    Number.isFinite(metrics?.width) && metrics.width > 0 && Number.isFinite(metrics?.height) && metrics.height > 0
      ? `${metrics.width} \u00d7 ${metrics.height} ${unit}` : null;
  add('App version / build', [value.app?.version, value.app?.build].filter(Boolean).join(' / '));
  add('OS', [value.device?.platform || value.platform?.name, value.device?.osVersion].filter(Boolean).join(' '));
  add('Device', [value.device?.manufacturer, value.device?.model, value.device?.deviceType || value.platform?.deviceType].filter(Boolean).join(' \u00b7 '));
  add('Framework (not runtime)', [value.platform?.framework, value.platform?.frameworkVersion].filter(Boolean).join(' '));
  add('App viewport', dimensions(value.viewport, 'DIP'));
  add('App density', Number.isFinite(value.viewport?.density) && value.viewport.density > 0 ? `${value.viewport.density} px / DIP` : null);
  add('Display', dimensions(value.display, 'px'));
  add('Orientation / rotation', [value.display?.orientation, value.display?.rotation].filter(Boolean).join(' / '));
  add('App theme', value.theme?.effective);
  add('Connectivity', [value.connectivity?.networkAccess, ...list(value.connectivity?.connectionProfiles)].filter(Boolean).join(' \u00b7 '));
  const gaps = list(value.unavailable).filter(item => item?.name && item?.reason)
    .map(item => `${item.name}: ${item.reason}`);
  if (!environment) gaps.push('Environment metadata is unavailable in this preview.');
  return { rows, gaps };
}

export function evidenceFileName(plan) {
  const suggested = typeof plan?.suggestedFileName === 'string' ? plan.suggestedFileName : '';
  const base = suggested.split(/[\\/]/).pop() || '';
  const cleaned = base.replace(/[^A-Za-z0-9._-]/g, '-').replace(/^[.-]+/, '');
  if (cleaned.length <= 160 && cleaned.toLowerCase().endsWith('.mauitrace') &&
      !/^(con|prn|aux|nul|com[1-9]|lpt[1-9])\./i.test(cleaned)) return cleaned;
  const stamp = new Date().toISOString().replace(/[-:]/g, '').replace(/\..+$/, '').replace('T', '-');
  return `devflow-${stamp}.mauitrace`;
}

export function buildCaptureBody({ choice, elementId, workflow } = {}) {
  const body = { includeScreenshot: choice?.includeScreenshot === true };
  if (elementId) body.elementId = String(elementId);
  if (choice?.includeWorkflow === true && typeof workflow === 'string' && workflow.trim())
    body.workflow = workflow;
  return body;
}

export function buildPreviewBody(options = {}) {
  return {
    ...buildCaptureBody(options),
    includeWorkflow: options.choice?.includeWorkflow === true,
  };
}

export function createEvidenceController(deps) {
  const {
    basePath, inspectorToken, api, setStatus, getSelectedId, getWorkflow,
    showEvidenceDialog: choose = showEvidenceDialog,
    showEvidenceFinalDialog: confirm = view => showEvidenceDialog(view, { finalReview: true }),
    downloadBlob: save = downloadBlob,
  } = deps;
  let busy = false;

  async function preview(body) {
    const result = await api.postDetailed('/api/evidence/preview', body);
    if (!result?.ok || result.body?.ok !== true || !Array.isArray(result.body.plan?.included))
      throw new Error(result?.body?.error || 'Could not prepare the evidence preview. Check the agent connection and retry.');
    return formatEvidencePlan(result.body.plan);
  }

  async function open() {
    if (busy) return;
    busy = true;
    try {
      setStatus('Preparing evidence preview...');
      const elementId = getSelectedId?.();
      const workflow = getWorkflow?.();
      const hasWorkflow = typeof workflow === 'string' && !!workflow.trim();
      const initial = await preview(buildPreviewBody({ elementId }));
      const choice = await choose(initial, { hasWorkflow });
      if (!choice) {
        setStatus('Evidence capture cancelled.');
        return;
      }
      let reviewed = initial;
      if (choice.includeScreenshot === true || choice.includeWorkflow === true) {
        setStatus('Preparing the attachment preview...');
        reviewed = await preview(buildPreviewBody({ choice, elementId, workflow }));
        if (!await confirm(reviewed, { hasWorkflow, choice, finalReview: true })) {
          setStatus('Evidence capture cancelled.');
          return;
        }
      }
      setStatus('Capturing evidence...');
      const headers = { 'Content-Type': 'application/json' };
      if (inspectorToken) headers['X-DevFlow-Inspector-Token'] = inspectorToken;
      const response = await fetch(`${basePath}/api/evidence/capture`, {
        method: 'POST',
        headers,
        body: JSON.stringify(buildCaptureBody({ choice, elementId, workflow })),
      });
      if (!response.ok) {
        const error = await response.json().catch(() => null);
        throw new Error(error?.error || `Could not capture evidence (HTTP ${response.status}).`);
      }
      const blob = await response.blob();
      if (!blob.size || !response.headers.get('Content-Type')?.startsWith('application/zip'))
        throw new Error('The server did not return an evidence bundle. No file was downloaded.');
      await save(blob, reviewed.fileName);
      setStatus(`Evidence bundle downloaded: ${reviewed.fileName}`);
    } catch (error) {
      console.error('Evidence capture failed:', error);
      setStatus(error instanceof Error ? error.message : 'Could not capture evidence.');
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
  document.body.append(anchor);
  anchor.click();
  anchor.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

function showEvidenceDialog(view, { hasWorkflow = false, finalReview = false } = {}) {
  return new Promise(resolve => {
    const previousFocus = document.activeElement;
    const dialog = node('dialog', 'df-evidence-dialog');
    dialog.setAttribute('aria-labelledby', 'df-evidence-title');
    dialog.setAttribute('aria-describedby', 'df-evidence-description');
    const header = node('header', 'df-evidence-header');
    header.append(node('p', 'df-evidence-eyebrow', finalReview ? 'Review attachments' : 'Local evidence'));
    const title = node('h2', null, view.title);
    title.id = 'df-evidence-title';
    const description = node('p', 'df-evidence-desc',
      'A read-only snapshot of the current app. Nothing is uploaded or replayed.');
    description.id = 'df-evidence-description';
    header.append(title, description);

    const content = node('div', 'df-evidence-content');
    content.tabIndex = 0;
    content.setAttribute('aria-label', 'Evidence preview details');
    content.append(node('p', 'df-evidence-summary', view.summary || 'No app data was available.'));
    const environment = node('section', 'df-evidence-section');
    environment.append(node('h3', null, 'Capture environment'));
    const definitions = node('dl', 'df-evidence-environment');
    for (const row of view.environment.rows)
      definitions.append(node('dt', null, row.label), node('dd', null, row.value));
    environment.append(definitions);
    environment.append(node('p', 'df-evidence-desc',
      'These are live observations, not the original recording environment or an enforced replay contract. Preview and capture sample the app separately.'));
    if (view.environment.gaps.length) {
      const gaps = node('details', 'df-evidence-gaps');
      gaps.append(node('summary', null, `Not captured or enforced (${view.environment.gaps.length})`));
      gaps.append(items(view.environment.gaps));
      environment.append(gaps);
    }
    content.append(environment);
    content.append(section('Included', view.includes.map(entryLabel)));
    if (view.excludes.length) content.append(section('Excluded', view.excludes.map(entryLabel)));
    const privacy = node('details', 'df-evidence-section');
    privacy.append(node('summary', null, 'Redaction and capture limits'));
    privacy.append(items(view.never));
    privacy.append(node('p', 'df-evidence-desc', `${view.redaction}. Limits: ${view.limits || 'See the manifest'}.`));
    content.append(privacy);
    if (view.warnings.length) content.append(section('Warnings', view.warnings, 'df-evidence-warn'));

    let screenshot;
    let workflow;
    if (!finalReview) {
      const options = node('fieldset', 'df-evidence-options');
      options.append(node('legend', null, 'Optional attachments'));
      screenshot = checkbox(options, 'df-evidence-screenshot',
        'Include an app screenshot. Pixels may contain private on-screen data.');
      if (hasWorkflow)
        workflow = checkbox(options, 'df-evidence-workflow',
          'Include loaded workflow steps. Prose may contain typed or private information.');
      options.append(node('p', 'df-evidence-desc', 'Attachments start off every time. Opting in opens a refreshed preview before download.'));
      content.append(options);
    } else {
      content.append(node('p', 'df-evidence-desc', view.screenshotNote));
    }

    const actions = node('footer', 'df-evidence-actions');
    const cancel = node('button', 'df-tool-btn', 'Cancel');
    cancel.type = 'button';
    const accept = node('button', 'df-tool-btn df-evidence-primary',
      finalReview ? 'Confirm and download' : 'Download bundle');
    accept.type = 'button';
    const updateLabel = () => {
      accept.textContent = screenshot?.checked || workflow?.checked ? 'Review attachments' : 'Download bundle';
    };
    screenshot?.addEventListener('change', updateLabel);
    workflow?.addEventListener('change', updateLabel);
    actions.append(cancel, accept);
    dialog.append(header, content, actions);
    let settled = false;
    const finish = result => {
      if (settled) return;
      settled = true;
      dialog.close();
      dialog.remove();
      if (previousFocus?.isConnected) previousFocus.focus({ preventScroll: true });
      resolve(result);
    };
    cancel.addEventListener('click', () => finish(null));
    accept.addEventListener('click', () => finish(finalReview ? true : {
      includeScreenshot: screenshot.checked,
      includeWorkflow: workflow?.checked === true,
    }));
    dialog.addEventListener('cancel', event => {
      event.preventDefault();
      finish(null);
    });
    dialog.addEventListener('keydown', event => {
      event.stopPropagation();
      if (event.key !== 'Tab') return;
      const focusable = [...dialog.querySelectorAll('button, input, summary, [tabindex="0"]')]
        .filter(element => !element.disabled && element.getClientRects().length > 0);
      const first = focusable[0];
      const last = focusable.at(-1);
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    });
    document.body.append(dialog);
    dialog.showModal();
    cancel.focus();
  });
}

function list(value) { return Array.isArray(value) ? value : []; }

function node(tag, className, text) {
  const element = document.createElement(tag);
  if (className) element.className = className;
  if (text != null) element.textContent = text;
  return element;
}

function items(values) {
  const result = node('ul');
  for (const value of values) result.append(node('li', null, value));
  return result;
}

function section(title, values, extraClass = '') {
  const result = node('section', `df-evidence-section ${extraClass}`);
  result.append(node('h3', null, title), items(values));
  return result;
}

function checkbox(parent, id, text) {
  const row = node('label', 'df-evidence-opt');
  const input = node('input');
  input.type = 'checkbox';
  input.id = id;
  input.checked = false;
  row.append(input, node('span', null, text));
  parent.append(row);
  return input;
}

function entryLabel(entry) {
  const count = Number.isFinite(entry.count) ? ` (${entry.count})` : '';
  return `${entry.name}${count}${entry.detail ? ` - ${entry.detail}` : ''}`;
}
