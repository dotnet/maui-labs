function clone(value) {
  return value == null ? value : JSON.parse(JSON.stringify(value));
}

function list(value) {
  return Array.isArray(value) ? value : [];
}

let selectedReviewStepKey = null;
let pendingReviewFocusKey = null;
let pendingReviewFocusIndex = null;
const selectorChecks = new Map();

function stepKey(step, index) {
  if (step?.stepId) return `stepId:${step.stepId}`;
  if (step?.id) return `id:${step.id}`;
  const seq = Number(step?.seq);
  return Number.isFinite(seq) ? `seq:${seq}:${index}` : `legacy:${index}`;
}

function selectorCheckKey(flow, step, index) {
  return JSON.stringify([
    flow?.flowId || null,
    flow?.revision || null,
    flow?.name || null,
    stepKey(step, index),
    effectiveSelector(step) || null,
  ]);
}

function resequence(flow, priorSteps = list(flow.steps)) {
  const nextSteps = list(flow.steps);
  const newSequenceByStep = new Map(nextSteps.map((step, index) => [step, index + 1]));
  const priorStepBySequence = new Map(
    priorSteps.map((step) => [Number(step?.seq), step]),
  );
  if (Array.isArray(flow.deviceSteps)) {
    flow.deviceSteps = flow.deviceSteps.flatMap((deviceStep) => {
      const afterStep = Number(deviceStep?.afterStep);
      if (afterStep === 0) return [{ ...deviceStep, afterStep: 0 }];
      const owner = priorStepBySequence.get(afterStep);
      const nextSequence = owner ? newSequenceByStep.get(owner) : null;
      return nextSequence == null ? [] : [{ ...deviceStep, afterStep: nextSequence }];
    });
  }
  flow.steps = nextSteps.map((step, index) => ({ ...step, seq: index + 1 }));
  return flow;
}

export function moveFlowStep(flow, index, offset) {
  const next = clone(flow);
  const priorSteps = list(next?.steps).slice();
  const target = index + offset;
  if (!Array.isArray(next?.steps) || index < 0 || index >= next.steps.length ||
      target < 0 || target >= next.steps.length) return next;
  [next.steps[index], next.steps[target]] = [next.steps[target], next.steps[index]];
  return resequence(next, priorSteps);
}

export function removeFlowStep(flow, index) {
  const next = clone(flow);
  const priorSteps = list(next?.steps).slice();
  if (!Array.isArray(next?.steps) || index < 0 || index >= next.steps.length) return next;
  next.steps.splice(index, 1);
  return resequence(next, priorSteps);
}

export function mergeRecordedFlow(baseFlow, appendedFlow) {
  const merged = clone(baseFlow) || {};
  const baseSteps = list(merged.steps);
  const appendedSteps = list(appendedFlow?.steps);
  const baseCount = baseSteps.length;
  merged.steps = [...baseSteps, ...appendedSteps]
    .map((step, index) => ({ ...step, seq: index + 1 }));

  const deviceSteps = [
    ...list(merged.deviceSteps),
    ...list(appendedFlow?.deviceSteps).map((step) => ({
      ...step,
      afterStep: baseCount + Math.max(0, Number(step?.afterStep) || 0),
    })),
  ];
  if (deviceSteps.length > 0)
    merged.deviceSteps = deviceSteps;
  return merged;
}

function el(tag, props = {}, children = []) {
  const node = document.createElement(tag);
  for (const [name, value] of Object.entries(props)) {
    if (value == null) continue;
    if (name === 'className') node.className = value;
    else if (name === 'text') node.textContent = value;
    else if (name === 'value') node.value = value;
    else if (name === 'checked') node.checked = !!value;
    else if (name === 'disabled') node.disabled = !!value;
    else node.setAttribute(name, String(value));
  }
  for (const child of children) node.append(child);
  return node;
}

function button(text, onClick, options = {}) {
  const node = el('button', {
    className: `df-workbench-action ${options.primary ? 'df-authoring-primary' : ''}`,
    type: 'button',
    text,
    disabled: options.disabled,
    title: options.title,
  });
  node.addEventListener('click', onClick);
  return node;
}

function field(label, control, hint = null) {
  const node = el('label', { className: 'df-authoring-field' });
  node.append(el('span', { className: 'df-authoring-field-label', text: label }), control);
  if (hint) node.append(el('span', { className: 'df-authoring-field-hint', text: hint }));
  return node;
}

function input(value, onInput, options = {}) {
  const node = el('input', {
    className: 'df-authoring-input',
    type: options.type || 'text',
    value: value ?? '',
    placeholder: options.placeholder,
    maxlength: options.maxlength || 4096,
  });
  node.addEventListener('input', () => onInput(node.value));
  return node;
}

function textarea(value, onInput, options = {}) {
  const node = el('textarea', { className: 'df-authoring-textarea', rows: options.rows || 2, maxlength: options.maxlength || 8192 });
  node.value = value ?? '';
  node.addEventListener('input', () => onInput(node.value));
  return node;
}

function parseFlow(markdown) {
  if (typeof markdown !== 'string') return null;
  const match = /```json maui-test\s*\r?\n([\s\S]*?)\r?\n```/.exec(markdown);
  if (!match) return null;
  try { return JSON.parse(match[1]); } catch { return null; }
}

function replaceFlowPayload(markdown, flow) {
  const payload = JSON.stringify(flow, null, 2);
  const expression = /(```json maui-test\s*\r?\n)[\s\S]*?(\r?\n```)/;
  if (typeof markdown === 'string' && expression.test(markdown))
    return markdown.replace(expression, `$1${payload}$2`);
  return `# Scenario: ${flow.name || 'scenario'}\n\n\`\`\`json maui-test\n${payload}\n\`\`\`\n`;
}

function effectiveSelector(step) {
  const selector = step?.args?.selector;
  return selector && Object.keys(selector).length ? selector : step?.target;
}

function selectorKind(selector) {
  if (!selector) return 'none';
  if (selector.automationId) return 'automationId';
  if (selector.text) return 'text';
  if (selector.typeIndex || selector.selectorKind === 'typeIndex') return 'typeIndex';
  if (selector.id) return 'runtimeId';
  return 'none';
}

export function isStrictAuthoringSelector(selector) {
  if (!selector || typeof selector !== 'object') return false;
  const scopedItemValid = (!selector.stableItemKey && !selector.collectionScope) ||
    (!!selector.stableItemKey && !!selector.collectionScope && !!selector.automationId);
  const forms = (selector.automationId ? 1 : 0) +
    (selector.text ? 1 : 0) +
    (selector.typeIndex || selector.selectorKind === 'typeIndex' ? 1 : 0) +
    (selector.id ? 1 : 0);
  return scopedItemValid && forms === 1 && !!(selector.automationId || selector.text || selector.typeIndex) &&
    selector.matchCount !== 0 && selector.matchCount !== undefined &&
    selector.matchCount !== null && selector.matchCount === 1 &&
    selector.quality !== 'ambiguous';
}

export function isObservationOnlyAssertion(kind) {
  return kind === 'pageChanged';
}

function selectorLabel(selector) {
  if (!selector) return 'No selector';
  if (selector.automationId) {
    return selector.stableItemKey && selector.collectionScope
      ? `AutomationId: ${selector.automationId} · stable item in ${selector.collectionScope}`
      : `AutomationId: ${selector.automationId}`;
  }
  if (selector.text) return `Text: ${selector.text}`;
  const typeIndex = selector.typeIndex || selector;
  if (typeIndex.type) return `Type index: ${typeIndex.type}[${typeIndex.index ?? 0}]`;
  if (selector.id) return 'Legacy runtime ID (not editable)';
  return 'No selector';
}

function looksSensitive(...values) {
  return values.filter(Boolean).join(' ').toLowerCase().match(/password|passcode|secret|token|apikey|api[_-]?key|credential|authorization|cookie|private|pin|otp|cvv|ssn/);
}

function makeSelectorFromSelected(selected) {
  if (!selected) return null;
  if (selected.automationId) {
    return {
      automationId: selected.automationId,
      ...(selected.stableItemKey && selected.collectionScope
        ? {
          stableItemKey: selected.stableItemKey,
          collectionScope: selected.collectionScope,
        }
        : {}),
    };
  }
  // Never copy live text from a selected control into a draft assertion. A text fallback can
  // accidentally persist an Entry/Editor value, so the composer requires an AutomationId.
  return null;
}

export function authoringIssues(draft) {
  if (list(draft?.issues).length) return list(draft.issues);
  return [
    ...list(draft?.errors).map((message) => ({ message, severity: 'error', blocking: true })),
    ...list(draft?.warnings).map((message) => ({ message, severity: 'warning', blocking: false })),
  ].map((issue) => {
    const match = /^step (\d+)(?: \(([^)]+)\))?: (.+)$/i.exec(String(issue.message || ''));
    const detail = match?.[3] || String(issue.message || '');
    const code = /ambiguous selector/i.test(detail)
      ? 'selector-ambiguous'
      : /resolve exactly one/i.test(detail)
        ? 'selector-match-count'
        : /fragile selector|selector is fragile/i.test(detail)
          ? 'selector-fragile'
          : /expected result|outcome check/i.test(detail)
            ? 'expected-result-missing'
            : 'review-required';
    return {
      ...issue,
      code,
      stepSequence: match ? Number(match[1]) : null,
      action: match?.[2] || null,
      remediation: ['selector-ambiguous', 'selector-match-count', 'selector-fragile'].includes(code)
        ? 'resolve-selector'
        : code === 'expected-result-missing'
          ? 'add-expected-result'
          : 'review',
    };
  });
}

function stepIssues(draft, step, index) {
  const sequence = Number(step?.seq) || index + 1;
  return authoringIssues(draft).filter((issue) => Number(issue?.stepSequence) === sequence);
}

function selectorIssues(draft, step, index) {
  return stepIssues(draft, step, index)
    .filter((issue) => issue?.remediation === 'resolve-selector');
}

function renderReviewIssueSummary(root, flow, draft, authoring, helpers) {
  const blocking = authoringIssues(draft).filter((issue) => issue?.blocking === true);
  if (!blocking.length) return;
  const first = blocking[0];
  const sequence = Number(first.stepSequence);
  const card = el('section', { className: 'df-review-blocker df-authoring-section' });
  card.append(
    el('h4', {
      text: Number.isInteger(sequence)
        ? `Step ${sequence} needs attention`
        : 'This test needs attention',
    }),
    el('p', {
      className: 'df-workbench-intro',
      text: first.remediation === 'resolve-selector'
        ? 'Choose a stable control for this step before checking or saving the test.'
        : first.message || 'Review the blocking issue before continuing.',
    }),
  );
  const actions = el('div', { className: 'df-authoring-actions' });
  if (Number.isInteger(sequence)) {
    actions.append(button(`Resolve step ${sequence}`, () => {
      const index = list(flow.steps).findIndex((step, stepIndex) =>
        (Number(step?.seq) || stepIndex + 1) === sequence);
      if (index >= 0) {
        selectedReviewStepKey = stepKey(flow.steps[index], index);
        authoring.clearAttention?.();
        helpers.rerender?.();
      }
    }, { primary: true }));
  }
  actions.append(button('Check again', () => authoring.validateFlow?.()));
  card.append(actions);
  helpers.agentAction?.(card, {
    title: 'Ask your agent to help resolve this',
    description: 'Your agent can inspect the draft and current app, explain the blocker, and prepare an inert update request.',
    prompt: [
      'Use only the restricted DevFlow test-agent tools.',
      `Inspect the current human-authored draft and resolve the blocker for ${Number.isInteger(sequence) ? `step ${sequence}` : 'the test'}.`,
      first.message || '',
      'Prefer a unique AutomationId or a scoped stable item key. Do not use runtime IDs, coordinates, or type/index ordering.',
      'Request only the bounded draft-change approval needed for the proposed selector. Do not run or apply source changes.',
    ].filter(Boolean).join(' '),
  });
  root.append(card);
}

export function usableSelectorFromMatch(match, matches, truncated) {
  if (!match?.automationId) return null;
  if (match.stableItemKey && match.collectionScope) {
    return {
      automationId: match.automationId,
      stableItemKey: match.stableItemKey,
      collectionScope: match.collectionScope,
    };
  }
  if (truncated) return null;
  const sameId = list(matches)
    .filter((candidate) => candidate?.automationId === match.automationId)
    .length;
  return sameId === 1 ? { automationId: match.automationId } : null;
}

function details(parent, title, value) {
  const row = el('div', { className: 'df-step-detail' });
  row.append(el('strong', { text: `${title}: ` }), el('span', { text: value }));
  parent.append(row);
}

function findings(parent, values, type) {
  if (!list(values).length) return;
  const host = el('section', { className: `df-authoring-findings df-authoring-findings-${type}` });
  const lines = el('ul');
  for (const value of values) lines.append(el('li', { text: String(value) }));
  host.append(lines);
  parent.append(host);
}

function applyFlow(authoring, draft, flow, rerender = true) {
  selectorChecks.clear();
  authoring.update?.({
    flow,
    markdown: replaceFlowPayload(draft.markdown, flow),
    flowDigest: null,
    flowDirty: true,
    stale: false,
    errors: [],
    warnings: [],
    issues: [],
    checkPassed: false,
    diffReviewed: false,
    attentionStepSequence: null,
  }, rerender);
}

function renderSelectorEditor(card, flow, stepIndex, draft, authoring) {
  const current = effectiveSelector(flow.steps[stepIndex]);
  const kind = selectorKind(current);
  const host = el('details', { className: 'df-selector-editor' });
  host.append(el('summary', { text: 'Selector (advanced)' }));
  const select = el('select', { className: 'df-authoring-select' });
  for (const [value, text] of [
    ['automationId', 'AutomationId'],
    ['text', 'Exact text'],
    ['typeIndex', 'Type + index (fragile)'],
  ]) select.append(el('option', { value, text }));
  select.value = kind === 'runtimeId' || kind === 'none' ? 'automationId' : kind;
  const value = input(
    current?.automationId || current?.text || current?.typeIndex?.type || current?.type || '',
    () => {},
    { placeholder: select.value === 'typeIndex' ? 'Button' : 'Identifier or exact text' }
  );
  const index = input(
    current?.typeIndex?.index ?? current?.index ?? 0,
    () => {},
    { type: 'number', placeholder: '0' }
  );
  const stableItemKey = input(current?.stableItemKey || '', () => {}, { placeholder: 'Stable model item ID' });
  const collectionScope = input(current?.collectionScope || '', () => {}, { placeholder: 'Collection AutomationId' });
  const indexField = field('Index', index, 'Only used for Type + index.');
  const stableItemField = field('Stable item key', stableItemKey, 'Optional. Use with collection scope for repeated items.');
  const collectionScopeField = field('Collection scope', collectionScope, 'AutomationId of the containing collection.');
  const updateVisibility = () => {
    indexField.hidden = select.value !== 'typeIndex';
    stableItemField.hidden = select.value !== 'automationId';
    collectionScopeField.hidden = select.value !== 'automationId';
    value.placeholder = select.value === 'typeIndex' ? 'Button' :
      select.value === 'text' ? 'Exact visible text' : 'AutomationId';
  };
  select.addEventListener('change', updateVisibility);
  updateVisibility();
  host.append(
    field('Selector type', select),
    field('Selector value', value),
    indexField,
    stableItemField,
    collectionScopeField
  );
  host.append(button('Validate and apply selector', async () => {
    const candidate = select.value === 'automationId'
      ? {
        automationId: value.value.trim(),
        ...(stableItemKey.value.trim() && collectionScope.value.trim()
          ? {
            stableItemKey: stableItemKey.value.trim(),
            collectionScope: collectionScope.value.trim(),
          }
          : {}),
      }
      : select.value === 'text'
        ? { text: value.value }
        : { typeIndex: { type: value.value.trim(), index: Math.max(0, Number(index.value) || 0) } };
    if (!(candidate.automationId || candidate.text || candidate.typeIndex?.type)) {
      authoring.message?.('Provide a durable selector value before validating.');
      return;
    }
    const result = await authoring.verifySelector?.(candidate);
    if (!result || result.ok !== true || result.matchCount !== 1) {
      authoring.message?.((result && result.error) || 'Selector did not resolve exactly one element.');
      return;
    }
    const next = clone(flow);
    const step = next.steps[stepIndex];
    candidate.matchCount = result.matchCount;
    candidate.quality = result.quality || (candidate.automationId ? 'durable' : 'fragile');
    if (step.args?.selector) step.args.selector = candidate;
    else step.target = candidate;
    step.fragile = !candidate.automationId;
    applyFlow(authoring, draft, next);
    authoring.message?.('Selector verified with exactly one match and applied to the draft.');
  }, { primary: true }));
  card.append(host);
}

function renderSelectorResolution(parent, flow, stepIndex, draft, authoring, helpers, issues) {
  const step = flow.steps[stepIndex];
  const sequence = Number(step?.seq) || stepIndex + 1;
  const key = selectorCheckKey(flow, step, stepIndex);
  const check = selectorChecks.get(key);
  const current = effectiveSelector(step);
  const host = el('section', { className: 'df-selector-resolution df-authoring-section' });
  host.append(
    el('h5', { text: `Step ${sequence} needs a stable control` }),
    el('p', {
      className: 'df-workbench-intro',
      text: issues.some((issue) => issue?.code === 'selector-ambiguous' || issue?.code === 'selector-match-count')
        ? 'The recorded control matches more than one element. DevFlow will not guess which one you intended.'
        : 'This control may move or change between runs. Choose a stable control before saving.',
    }),
    el('p', {
      className: 'df-workbench-note',
      text: `Recorded control: ${selectorLabel(current)}`,
    }),
  );

  const actions = el('div', { className: 'df-authoring-actions' });
  const checkButton = button(
    check?.loading ? 'Checking controls…' : 'Check matching controls',
    async () => {
      selectorChecks.set(key, { loading: true });
      helpers.rerender?.();
      const result = await authoring.verifySelector?.(current);
      selectorChecks.set(key, result || { ok: false, error: 'Selector verification is unavailable.' });
      helpers.rerender?.();
    },
    { primary: true, disabled: check?.loading || !current }
  );
  actions.append(
    checkButton,
    button('Select intended control in app', () => {
      helpers.inspectApp?.();
      authoring.message?.('Select the intended control in the app, then return to Review and choose Use selected control.');
    }),
    button('Use selected control', async () => {
      const selected = authoring.selectedElement?.();
      const selector = makeSelectorFromSelected(selected);
      if (!selector) {
        authoring.message?.('Select a control with a stable AutomationId before updating this step.');
        return;
      }
      const result = await authoring.applyHumanSelectedSelector?.({
        stepId: step?.stepId,
        stepSequence: sequence,
        selector,
      });
      if (result?.ok) selectorChecks.delete(key);
    })
  );
  host.append(actions);

  if (check && !check.loading) {
    const matches = list(check.matches || check.ambiguity?.matches);
    const totalCount = Number(check.totalCount ?? check.ambiguity?.totalCount ?? check.matchCount) || matches.length;
    const truncated = check.truncated === true || check.ambiguity?.truncated === true;
    if (check.ok === true && check.matchCount === 1) {
      host.append(el('p', {
        className: 'df-workbench-note',
        text: 'The recorded control currently resolves exactly once. Apply it again to refresh the saved selector facts.',
      }));
      host.append(button('Use verified control', async () => {
        const result = await authoring.applyHumanSelectedSelector?.({
          stepId: step?.stepId,
          stepSequence: sequence,
          selector: current,
        });
        if (result?.ok) selectorChecks.delete(key);
      }, { primary: true }));
    } else if (matches.length) {
      host.append(el('p', {
        className: 'df-workbench-safety',
        text: `${totalCount} controls match. Choose only a control with a unique AutomationId or a stable repeated-item key.`,
      }));
      const cards = el('div', { className: 'df-selector-match-list' });
      let usable = 0;
      for (const match of matches) {
        const selector = usableSelectorFromMatch(match, matches, truncated);
        if (selector) usable++;
        const card = el('article', { className: 'df-selector-match' });
        card.append(
          el('h6', { text: match.type || 'Control' }),
          el('p', {
            className: 'df-workbench-note',
            text: match.automationId
              ? `AutomationId: ${match.automationId}`
              : 'No AutomationId',
          }),
        );
        if (match.stableItemKey && match.collectionScope) {
          card.append(el('p', {
            className: 'df-authoring-field-hint',
            text: `Stable repeated item in ${match.collectionScope}`,
          }));
        }
        const cardActions = el('div', { className: 'df-authoring-actions' });
        cardActions.append(button('Highlight in app', () => authoring.selectLiveElement?.(match.id)));
        if (selector) {
          cardActions.append(button('Use this control', async () => {
            const result = await authoring.applyHumanSelectedSelector?.({
              stepId: step?.stepId,
              stepSequence: sequence,
              selector,
            });
            if (result?.ok) selectorChecks.delete(key);
          }, { primary: true }));
        }
        card.append(cardActions);
        cards.append(card);
      }
      host.append(cards);
      if (usable === 0) {
        host.append(el('p', {
          className: 'df-workbench-safety',
          text: 'None of these repeated controls has enough stable identity to save safely. Add a stable item key to the app, rebuild it, and record this step again.',
        }));
        helpers.agentAction?.(host, {
          title: 'Ask your agent to make this control testable',
          description: 'The app needs stable identity for each repeated item before this step can be replayed safely.',
          prompt: [
            'Improve the connected MAUI app testability for this repeated control.',
            `Step ${sequence} uses duplicate AutomationId ${current?.automationId || '(missing)'}.`,
            'Add a stable model item ID and bind Microsoft.Maui.DevFlow.Agent.Core.DevFlowTest.StableItemKey on the repeated item-template root.',
            'Keep the child AutomationId, do not use visible text or type/index ordering, rebuild the app, and explain that the step must be recorded again.',
          ].join(' '),
        });
      }
    } else {
      host.append(el('p', {
        className: 'df-workbench-safety',
        text: check.error || 'The recorded control could not be resolved in the current app.',
      }));
    }
  }
  parent.append(host);
}

function renderAssertionComposer(parent, flow, draft, authoring, options = {}) {
  const host = el('section', { className: 'df-authoring-section' });
  host.append(el('h4', { text: 'Add expected result' }));
  host.append(el('p', {
    className: 'df-authoring-section-hint',
    text: 'Describe one observable result that proves the selected step worked.',
  }));
  const selected = authoring.selectedElement?.();
  const fixedStepIndex = Number.isInteger(options.stepIndex) ? options.stepIndex : null;
  const stepSelector = fixedStepIndex == null ? null : effectiveSelector(flow.steps[fixedStepIndex]);
  const selectedSelector = makeSelectorFromSelected(selected) ||
    (stepSelector?.automationId
      ? {
        automationId: stepSelector.automationId,
        ...(stepSelector.stableItemKey && stepSelector.collectionScope
          ? {
            stableItemKey: stepSelector.stableItemKey,
            collectionScope: stepSelector.collectionScope,
          }
          : {}),
      }
      : null);
  const kind = el('select', { className: 'df-authoring-select' });
  for (const [value, text] of [
    ['exists', 'The step target exists'],
    ['notExists', 'The target no longer exists'],
    ['propEquals', 'A target property equals a value'],
    ['routeIs', 'The current route equals a value'],
    ['pageChanged', 'The page changed (observation only)'],
  ]) kind.append(el('option', { value, text }));
  const target = fixedStepIndex == null ? el('select', { className: 'df-authoring-select' }) : null;
  if (target) {
    list(flow.steps).forEach((step, index) =>
      target.append(el('option', { value: String(index), text: `${step.seq || index + 1}. ${step.label || step.action}` })));
  }
  const property = input('Text', () => {}, { placeholder: 'Text' });
  const selectorAutomationId = input(selectedSelector?.automationId || '', () => {}, {
    placeholder: 'AutomationId of the target that should disappear',
  });
  // The selected element supplies only a durable selector and property default. Never copy its
  // current value into an authored assertion: an apparently ordinary Entry can still hold a secret.
  const expected = input('', () => {}, { placeholder: 'Expected value (never prefilled)' });
  const note = textarea('', () => {}, { rows: 2 });
  const selectorHint = el('p', {
    className: 'df-workbench-note',
    text: selectedSelector
      ? `Result target: ${selectorLabel(selectedSelector)}`
      : 'Select an element with an AutomationId before adding a target result.',
  });
  const updateKind = () => {
    const elementAssertion = kind.value === 'exists' || kind.value === 'notExists' || kind.value === 'propEquals';
    property.closest('label')?.toggleAttribute('hidden', kind.value !== 'propEquals');
    expected.closest('label')?.toggleAttribute('hidden', kind.value !== 'propEquals' && kind.value !== 'routeIs');
    selectorAutomationId.closest('label')?.toggleAttribute('hidden', kind.value !== 'notExists');
    selectorHint.hidden = !elementAssertion;
  };
  kind.addEventListener('change', updateKind);
  host.append(field('Expected result', kind));
  if (target) host.append(field('After step', target));
  host.append(
    selectorHint,
    field('Target AutomationId', selectorAutomationId),
    field('Property', property),
    field('Expected value', expected),
    field('Observation note', note),
  );
  updateKind();
  const status = el('p', { className: 'df-workbench-status' });
  host.append(status);
  host.append(button('Check current app now (optional)', async () => {
    const assertion = buildAssertion();
    if (!assertion) return;
    const result = await authoring.verifyAssertion?.(assertion);
    status.textContent = !result
      ? 'Verification is unavailable.'
      : result.observationOnly
        ? 'This is an observation only and does not unlock Run.'
        : result.passed === true
          ? `Verification passed${result.matchCount ? ` with ${result.matchCount} unique match` : ''}.`
          : `Verification did not pass${result.error ? `: ${result.error}` : '.'}`;
  }));
  host.append(button('Add expected result', async () => {
    const assertion = buildAssertion();
    if (!assertion) return;
    if (assertion.kind === 'exists' || assertion.kind === 'propEquals') {
      const checked = await authoring.verifySelector?.(assertion.selector);
      if (!checked || checked.ok !== true || checked.matchCount !== 1) {
        status.textContent = (checked && checked.error) || 'The selected selector is not uniquely resolvable.';
        return;
      }
      assertion.selector.matchCount = checked.matchCount;
      assertion.selector.quality = checked.quality || (assertion.selector.automationId ? 'durable' : 'fragile');
    } else if (assertion.kind === 'notExists') {
      assertion.selector.quality = 'durable';
    }
    const next = clone(flow);
    const index = fixedStepIndex == null
      ? Math.max(0, Math.min(next.steps.length - 1, Number(target?.value) || 0))
      : Math.max(0, Math.min(next.steps.length - 1, fixedStepIndex));
    next.steps[index].asserts = list(next.steps[index].asserts);
    next.steps[index].asserts.push(assertion);
    applyFlow(authoring, draft, next);
    authoring.noteAssertionAdded?.(assertion);
    status.textContent = assertion.verify ? 'Expected result added to the draft.' : 'Observation note added to the draft.';
  }, { primary: true, disabled: !flow.steps.length }));

  function buildAssertion() {
    const result = { kind: kind.value, verify: kind.value !== 'pageChanged' };
    if (kind.value === 'exists') {
      if (!selectedSelector) {
        status.textContent = 'Select an element with a durable selector before adding this result.';
        return null;
      }
      result.selector = selectedSelector;
    } else if (kind.value === 'notExists') {
      const automationId = selectorAutomationId.value.trim();
      if (!selectedSelector && !automationId) {
        status.textContent = 'Provide the AutomationId of the target that should no longer exist.';
        return null;
      }
      result.selector = selectedSelector || { automationId };
    } else if (kind.value === 'propEquals') {
      if (!selectedSelector) {
        status.textContent = 'Select an element with a durable selector before adding this result.';
        return null;
      }
      if (looksSensitive(property.value, selected?.automationId, selected?.text)) {
        status.textContent = 'Sensitive values are not persisted or used as property assertions.';
        return null;
      }
      result.selector = selectedSelector;
      result.name = property.value.trim() || 'Text';
      result.expected = expected.value;
    } else if (kind.value === 'routeIs') {
      result.expected = expected.value.trim();
      if (!result.expected) {
        status.textContent = 'Provide an expected route.';
        return null;
      }
    } else {
      result.note = note.value.trim() || 'Page change observed.';
    }
    return result;
  }
  parent.append(host);
}

function expectedResultLabel(assertion) {
  if (assertion?.kind === 'exists') return 'Target exists';
  if (assertion?.kind === 'notExists') return 'Target no longer exists';
  if (assertion?.kind === 'propEquals')
    return `${assertion.name || 'Property'} equals ${assertion.expected ?? '(empty)'}`;
  if (assertion?.kind === 'routeIs') return `Route equals ${assertion.expected || '(empty)'}`;
  if (assertion?.kind === 'pageChanged') return assertion.note || 'Page changed';
  return assertion?.kind || 'Expected result';
}

function renderReviewEditor(root, flow, draft, authoring, helpers) {
  root.classList.add('df-review-workspace');
  const steps = list(flow.steps);
  const requestedIndex = steps.findIndex((step, index) => stepKey(step, index) === selectedReviewStepKey);
  const attentionSequence = Number(draft.attentionStepSequence);
  const attentionIndex = Number.isInteger(attentionSequence)
    ? steps.findIndex((step, index) => (Number(step?.seq) || index + 1) === attentionSequence)
    : -1;
  const selectedIndex = attentionIndex >= 0
    ? attentionIndex
    : requestedIndex >= 0
      ? requestedIndex
      : steps.length ? 0 : -1;
  if (selectedIndex >= 0 && selectedReviewStepKey == null)
    selectedReviewStepKey = stepKey(steps[selectedIndex], selectedIndex);
  const layout = el('div', { className: 'df-review-layout' });
  const rail = el('div', {
    className: 'df-review-step-list',
    role: 'list',
    'aria-label': 'Recorded steps',
  });

  const focusStepRow = (index) => {
    setTimeout(() => {
      root.querySelector(`[data-step-index="${index}"]`)?.focus();
    }, 50);
  };

  const selectStep = (index, focus = false) => {
    const step = steps[index];
    if (!step) return;
    selectedReviewStepKey = stepKey(step, index);
    pendingReviewFocusKey = focus ? selectedReviewStepKey : null;
    pendingReviewFocusIndex = focus ? index : null;
    authoring.clearAttention?.();
    helpers.rerender?.();
  };

  for (let index = 0; index < steps.length; index++) {
    const step = steps[index];
    const key = stepKey(step, index);
    const selected = index === selectedIndex;
    const issues = stepIssues(draft, step, index);
    const blocking = issues.some((issue) => issue?.blocking === true);
    const item = el('div', {
      className: 'df-review-step-item',
      role: 'listitem',
      'aria-current': selected ? 'step' : null,
    });
    const row = el('button', {
      className: `df-review-step-row${selected ? ' df-selected' : ''}${blocking ? ' df-review-step-blocked' : ''}`,
      type: 'button',
      'data-step-index': String(index),
    });
    row.append(
      el('span', { className: 'df-review-step-number', text: String(index + 1) }),
      el('span', { className: 'df-review-step-title', text: step.label || step.action || 'Step' }),
      el('span', {
        className: 'df-review-step-summary',
        text: blocking
          ? 'Needs attention'
          : `${step.action || 'action'} · ${list(step.asserts).filter((assertion) => assertion?.verify !== false).length} expected result${list(step.asserts).filter((assertion) => assertion?.verify !== false).length === 1 ? '' : 's'}`,
      }),
    );
    row.addEventListener('click', () => selectStep(index, true));
    row.addEventListener('keydown', (event) => {
      let next = null;
      if (event.key === 'ArrowDown' || event.key === 'ArrowRight') next = Math.min(steps.length - 1, index + 1);
      else if (event.key === 'ArrowUp' || event.key === 'ArrowLeft') next = Math.max(0, index - 1);
      else if (event.key === 'Home') next = 0;
      else if (event.key === 'End') next = steps.length - 1;
      if (next == null || next === index) return;
      event.preventDefault();
      selectStep(next, true);
    });
    item.append(row);
    rail.append(item);
  }
  layout.append(rail);

  const detail = el('section', { className: 'df-review-step-editor' });
  if (selectedIndex < 0) {
    detail.append(
      el('h4', { text: 'Select a step' }),
      el('p', {
        className: 'df-workbench-intro',
        text: 'Choose a recorded step to edit, reorder, remove, or add its expected result.',
      }),
    );
    layout.append(detail);
    root.append(layout);
    return;
  }

  const step = steps[selectedIndex];
  detail.append(el('h4', { text: `Step ${selectedIndex + 1}: ${step.label || step.action || 'Step'}` }));
  details(detail, 'Recorded action', step.action || 'unknown');
  details(detail, 'Target', selectorLabel(effectiveSelector(step)));

  const editActions = el('div', { className: 'df-authoring-actions' });
  editActions.append(
    button('Move up', () => {
      const next = moveFlowStep(flow, selectedIndex, -1);
      const nextIndex = Math.max(0, selectedIndex - 1);
      selectedReviewStepKey = stepKey(next.steps[nextIndex], nextIndex);
      applyFlow(authoring, draft, next);
    }, { disabled: selectedIndex === 0 }),
    button('Move down', () => {
      const next = moveFlowStep(flow, selectedIndex, 1);
      const nextIndex = Math.min(next.steps.length - 1, selectedIndex + 1);
      selectedReviewStepKey = stepKey(next.steps[nextIndex], nextIndex);
      applyFlow(authoring, draft, next);
    }, { disabled: selectedIndex === steps.length - 1 }),
    button('Remove step', () => {
      const next = removeFlowStep(flow, selectedIndex);
      const nextIndex = Math.min(selectedIndex, next.steps.length - 1);
      selectedReviewStepKey = nextIndex >= 0 ? stepKey(next.steps[nextIndex], nextIndex) : null;
      applyFlow(authoring, draft, next);
    }),
  );
  detail.append(editActions);

  const activeSelectorIssues = selectorIssues(draft, step, selectedIndex);
  if (activeSelectorIssues.length) {
    renderSelectorResolution(
      detail,
      flow,
      selectedIndex,
      draft,
      authoring,
      helpers,
      activeSelectorIssues
    );
  }

  const results = el('section', { className: 'df-review-expected-results' });
  results.append(el('h5', { text: 'Expected results' }));
  const assertions = list(step.asserts);
  if (!assertions.length) {
    results.append(el('p', {
      className: 'df-workbench-safety',
      text: 'Add one expected result to unlock Run.',
    }));
  } else {
    for (let assertionIndex = 0; assertionIndex < assertions.length; assertionIndex++) {
      const assertion = assertions[assertionIndex];
      const row = el('div', { className: 'df-review-expected-result' });
      row.append(
        el('span', {
          text: `${assertion.verify === false ? 'Observation' : 'Verified result'}: ${expectedResultLabel(assertion)}`,
        }),
        button('Remove', () => {
          const next = clone(flow);
          next.steps[selectedIndex].asserts = list(next.steps[selectedIndex].asserts);
          next.steps[selectedIndex].asserts.splice(assertionIndex, 1);
          applyFlow(authoring, draft, next);
        }),
      );
      results.append(row);
    }
  }
  if (activeSelectorIssues.length) {
    results.append(el('p', {
      className: 'df-workbench-note',
      text: 'Resolve the control for this step before adding an expected result.',
    }));
  } else {
    const addResult = el('details', { className: 'df-expected-result-editor' });
    addResult.append(el('summary', { text: 'Add expected result' }));
    addResult.open = !assertions.some((assertion) => assertion?.verify !== false);
    renderAssertionComposer(addResult, flow, draft, authoring, { stepIndex: selectedIndex });
    results.append(addResult);
  }
  detail.append(results);

  const stepDetails = el('details', { className: 'df-review-step-details' });
  stepDetails.append(el('summary', { text: 'Step details (optional)' }));
  const label = input(step.label, () => {}, { placeholder: step.action || 'Step name' });
  const intent = textarea(step.intent, () => {}, { rows: 2 });
  stepDetails.append(
    field('Step name', label, 'Optional label shown in the step list.'),
    field('Purpose', intent, 'Optional plain-language explanation.'),
    button('Save step', () => {
      const next = clone(flow);
      next.steps[selectedIndex].label = label.value.trim() || undefined;
      next.steps[selectedIndex].intent = intent.value.trim() || undefined;
      applyFlow(authoring, draft, next);
    }, { primary: true }),
  );
  renderSelectorEditor(stepDetails, flow, selectedIndex, draft, authoring);
  detail.append(stepDetails);
  layout.append(detail);
  root.append(layout);
  if (pendingReviewFocusIndex === selectedIndex) {
    const focusIndex = selectedIndex;
    pendingReviewFocusIndex = null;
    pendingReviewFocusKey = null;
    setTimeout(() => {
      root.querySelector(`[data-step-index="${focusIndex}"]`)?.focus();
    }, 0);
  }
}

export function renderStepsPanel(helpers) {
  const stage = helpers.workbenchState?.().selectedStage || 'record';
  const reviewing = stage === 'review';
  const root = helpers.root(reviewing ? 'Review' : 'Steps');
  const authoring = helpers.authoring;
  if (!authoring || typeof authoring.state !== 'function') {
    helpers.intro(root, 'Add a Goal before recording steps.');
    return root;
  }
  const draft = authoring.state();
  const readiness = draft.readiness || {};
  const flow = clone(draft.flow || parseFlow(draft.markdown));
  const hasSteps = !!flow && list(flow.steps).length > 0;

  if (!readiness.goal && !draft.recording) {
    helpers.intro(root, 'Add the Goal first. Steps become available after the test has an outcome to prove.');
    const actions = el('div', { className: 'df-authoring-actions' });
    actions.append(button('Go to Goal', () => helpers.focusGoal?.(), { primary: true }));
    root.append(actions);
    helpers.safety(root);
    return root;
  }

  if (!reviewing && draft.recording) {
    helpers.intro(root, draft.appendingRecording
      ? 'Recording more steps. Interact with the app, then stop to append them to the draft.'
      : 'Recording is active. Interact with the app, then stop when the required steps are captured.');
    const actions = el('div', { className: 'df-authoring-actions' });
    actions.append(
      button('Stop recording', () => authoring.stopRecording?.(), {
        primary: true,
        disabled: !draft.canDrive || !!draft.recordingStopping,
      }),
      button('Discard recording', () => authoring.cancelRecording?.(), {
        disabled: !draft.canDrive || !!draft.recordingStopping,
      }),
    );
    root.append(actions, el('p', {
      className: 'df-workbench-state',
      text: `${draft.recordingSteps || 0} step${draft.recordingSteps === 1 ? '' : 's'} captured`,
    }));
    helpers.safety(root);
    return root;
  }

  if (!reviewing) {
    findings(root, draft.errors, 'error');
    findings(root, draft.warnings, 'warning');
  } else {
    const nonStepErrors = list(draft.errors).filter((value) => !/^step \d+/i.test(String(value)));
    const nonStepWarnings = list(draft.warnings).filter((value) => !/^step \d+/i.test(String(value)));
    findings(root, nonStepErrors, 'error');
    findings(root, nonStepWarnings, 'warning');
  }
  if (draft.saving) root.append(el('p', { className: 'df-workbench-note', text: 'Saving test…' }));

  if (!reviewing && !hasSteps) {
    helpers.intro(root, 'Record the app interactions that prove the Goal. Recording does not start a test run.');
    const actions = el('div', { className: 'df-authoring-actions' });
    actions.append(button('Start recording', () => authoring.startRecording?.(), {
      primary: true,
      disabled: !draft.canDrive || !!draft.recordingStopping,
    }));
    root.append(actions);
    helpers.safety(root);
    return root;
  }

  if (reviewing && !hasSteps) {
    helpers.intro(root, 'There are no steps to review yet.');
    const actions = el('div', { className: 'df-authoring-actions' });
    actions.append(button('Go to Steps', () => helpers.selectStage?.('record'), { primary: true }));
    root.append(actions);
    helpers.safety(root);
    return root;
  }

  if (!reviewing) {
    helpers.intro(root, `${flow.steps.length} recorded step${flow.steps.length === 1 ? '' : 's'}. Select one to edit it in Review.`);
    const actions = el('div', { className: 'df-authoring-actions' });
    actions.append(button('Continue to Review', () => helpers.selectStage?.('review'), { primary: true }));
    root.append(actions);
    const timeline = el('div', {
      className: 'df-review-step-list df-steps-summary-list',
      role: 'list',
      'aria-label': 'Captured steps',
    });
    for (let index = 0; index < flow.steps.length; index++) {
      const step = flow.steps[index];
      const verified = list(step.asserts).filter((assertion) => assertion?.verify !== false).length;
      const item = el('div', {
        className: 'df-review-step-item',
        role: 'listitem',
      });
      const row = el('button', {
        className: 'df-review-step-row',
        type: 'button',
      });
      row.append(
        el('span', { className: 'df-review-step-number', text: String(index + 1) }),
        el('span', { className: 'df-review-step-title', text: step.label || step.action || 'Step' }),
        el('span', {
          className: 'df-review-step-summary',
          text: `${step.action || 'action'} · ${verified} expected result${verified === 1 ? '' : 's'}`,
        }),
      );
      row.addEventListener('click', () => {
        selectedReviewStepKey = stepKey(step, index);
        helpers.selectStage?.('review');
      });
      item.append(row);
      timeline.append(item);
    }
    root.append(timeline);

    const options = el('details', { className: 'df-recording-options' });
    options.append(el('summary', { text: 'Recording options' }));
    const optionActions = el('div', { className: 'df-authoring-actions' });
    optionActions.append(
      button('Start a new recording', () => authoring.startRecording?.(), { disabled: !draft.canDrive }),
      button('Download recording draft', () => authoring.downloadRecordingDraft?.()),
    );
    if (draft.workspaceAvailable === false || draft.rawDraft) {
      optionActions.append(button('Save draft with host', () => authoring.saveRecordingDraftFallback?.(), {
        title: 'Uses the host save fallback when available; otherwise downloads the Markdown draft.',
      }));
    }
    options.append(optionActions);
    root.append(options);
    helpers.safety(root);
    return root;
  }

  renderReviewIssueSummary(root, flow, draft, authoring, helpers);
  helpers.intro(root, draft.bindingStale
    ? 'The recorded steps changed after this plan was saved. Review them, then save the updated test.'
    : readiness.savedBundle && readiness.hardOutcomeCheck
      ? 'Review the recorded steps. This saved test is ready to run.'
      : readiness.hardOutcomeCheck
        ? 'Review the recorded steps, then check and save the test.'
        : 'Review the recorded steps and add one expected result before saving.');

  const reviewActions = el('div', { className: 'df-authoring-actions df-review-toolbar' });
  reviewActions.append(button('Record more steps', () => authoring.startAppendingRecording?.(), {
    disabled: !draft.canDrive,
  }));
  root.append(reviewActions);
  renderReviewEditor(root, flow, draft, authoring, helpers);

  const actions = el('div', { className: 'df-authoring-actions df-review-actions' });
  const blockingIssues = authoringIssues(draft).filter((issue) => issue?.blocking === true);
  if (readiness.savedBundle && readiness.hardOutcomeCheck && !draft.flowDirty && !draft.planDirty) {
    actions.append(button('Continue to Run', () => helpers.selectStage?.('run'), { primary: true }));
  } else if (blockingIssues.length === 0 && readiness.hardOutcomeCheck) {
    if (draft.checkPassed !== true) {
      actions.append(button('Check test', () => authoring.validateFlow?.(), {
        primary: true,
        disabled: !!draft.saving,
      }));
    } else if (draft.diffReviewed !== true) {
      actions.append(button('Review changes', () => authoring.diffFlow?.(), {
        primary: true,
        disabled: !!draft.saving,
      }));
    } else {
    actions.append(button(
      draft.stale ? 'Overwrite saved test' : draft.bindingStale ? 'Save updated test' : 'Save test',
      () => authoring.commitBundle?.(draft.stale === true), {
      primary: true,
      disabled: !!draft.saving,
      title: 'Saves the flow and managed plan together. Saving never starts a run.',
    }));
    }
  }
  if (actions.childElementCount) root.append(actions);

  if (draft.diff) {
    const diff = el('details', { className: 'df-authoring-diff', open: true });
    diff.append(el('summary', { text: 'Changes to save' }), el('pre', { text: draft.diff }));
    root.append(diff);
  }
  if ((draft.errors?.length || draft.workspaceAvailable === false) && draft.markdown) {
    const recovery = el('details', { className: 'df-review-recovery' });
    recovery.append(el('summary', { text: 'Draft recovery' }));
    const recoveryActions = el('div', { className: 'df-authoring-actions' });
    recoveryActions.append(button('Download current draft', () => authoring.downloadRecordingDraft?.()));
    recovery.append(recoveryActions);
    root.append(recovery);
  }
  helpers.safety(root);
  return root;
}
