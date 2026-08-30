function clone(value) {
  return value == null ? value : JSON.parse(JSON.stringify(value));
}

function list(value) {
  return Array.isArray(value) ? value : [];
}

function splitLines(value) {
  return String(value || '').split(/\r?\n/).map((item) => item.trim()).filter(Boolean);
}

function splitCsv(value) {
  return String(value || '').split(',').map((item) => item.trim()).filter(Boolean);
}

export function agentPreparationPrompt(goal = '') {
  const boundedGoal = String(goal || '')
    .replace(/[\u0000-\u001f\u007f]/g, ' ')
    .trim()
    .slice(0, 1000);
  return [
    'Use only the restricted DevFlow test-agent tools.',
    boundedGoal
      ? `Prepare a complete test for the connected app with this Goal: ${boundedGoal}`
      : 'Help me define the Goal, then prepare a complete test for the connected app.',
    'Include the steps and expected results in the initial draft.',
    'Request one commit review, then wait. Do not run until I approve a separate run request.',
    'Do not apply repairs or source changes automatically.',
  ].join(' ');
}

function el(tag, props = {}, children = []) {
  const node = document.createElement(tag);
  for (const [name, value] of Object.entries(props)) {
    if (value == null) continue;
    if (name === 'className') node.className = value;
    else if (name === 'text') node.textContent = value;
    else if (name === 'checked') node.checked = !!value;
    else if (name === 'disabled') node.disabled = !!value;
    else if (name === 'value') node.value = value;
    else if (name.startsWith('aria-')) node.setAttribute(name, String(value));
    else node.setAttribute(name, String(value));
  }
  for (const child of children) node.append(child);
  return node;
}

function labelField(label, control, hint = null) {
  const wrapper = el('label', { className: 'df-authoring-field' });
  wrapper.append(el('span', { className: 'df-authoring-field-label', text: label }), control);
  if (hint) wrapper.append(el('span', { className: 'df-authoring-field-hint', text: hint }));
  return wrapper;
}

function input(value, onInput, options = {}) {
  const control = el('input', {
    className: 'df-authoring-input',
    type: options.type || 'text',
    id: options.id,
    value: value ?? '',
    placeholder: options.placeholder,
    maxlength: options.maxlength || 4096,
    required: options.required ? 'true' : null,
    'aria-invalid': options.ariaInvalid,
    'aria-describedby': options.ariaDescribedBy,
  });
  control.addEventListener('input', () => onInput(control.value));
  return control;
}

function textarea(value, onInput, options = {}) {
  const control = el('textarea', {
    className: 'df-authoring-textarea',
    id: options.id,
    placeholder: options.placeholder,
    maxlength: options.maxlength || 16384,
    rows: options.rows || 3,
    required: options.required ? 'true' : null,
    'aria-invalid': options.ariaInvalid,
    'aria-describedby': options.ariaDescribedBy,
  });
  control.value = value ?? '';
  control.addEventListener('input', () => onInput(control.value));
  return control;
}

export function createPlanDraft(flowName = 'scenario.md', flowDigest = '') {
  const now = new Date().toISOString();
  const name = flowName || 'scenario.md';
  return {
    schema: 1,
    planId: `plan_${Math.random().toString(36).slice(2, 12)}`,
    revision: 1,
    flow: { path: name, digest: flowDigest || '' },
    title: '',
    goal: '',
    scenarios: [{ scenarioId: 'scenario-1', description: '', acceptanceCriterionIds: [], risks: [] }],
    assumptions: [],
    risks: [],
    preconditions: [],
    reset: { required: true, strategy: '', resetIdentity: '', seedFingerprint: '', backendStateFingerprint: '' },
    acceptanceCriteria: [],
    requirements: { requiredCapabilities: [], requiredSemantics: [] },
    requiredPlatforms: [],
    explorationBudget: { maxActions: 0, maxDurationSeconds: 0, allowedScopes: [] },
    prohibitedActionClasses: [],
    provenance: { actorKind: 'human', channel: 'inspector', provider: '', intent: 'human-authored', recordedAt: now },
    reviews: [],
    approvals: [],
    sideEffectPolicy: 'none',
    businessOracles: [],
    independentBusinessOracles: [],
    checkpoint: {},
  };
}

function defaultPlan(draft) {
  return createPlanDraft(draft.flowName, draft.flowDigest);
}

function findings(parent, title, values, kind) {
  if (!list(values).length) return;
  const section = el('section', { className: `df-authoring-findings df-authoring-findings-${kind}` });
  section.append(el('strong', { text: title }));
  const lines = el('ul');
  for (const value of values) lines.append(el('li', { text: String(value) }));
  section.append(lines);
  parent.append(section);
}

function action(parent, text, callback, options = {}) {
  const button = el('button', {
    className: `df-workbench-action ${options.primary ? 'df-authoring-primary' : ''}`,
    type: 'button',
    text,
    disabled: options.disabled,
    title: options.title,
  });
  button.addEventListener('click', callback);
  parent.append(button);
  return button;
}

function section(parent, title, description = null) {
  const node = el('section', { className: 'df-authoring-section' });
  node.append(el('h4', { text: title }));
  if (description) node.append(el('p', { className: 'df-authoring-section-hint', text: description }));
  parent.append(node);
  return node;
}

function optionalGroup(parent, title, description) {
  const group = el('details', { className: 'df-test-detail-group' });
  group.append(el('summary', { text: title }));
  if (description) {
    group.append(el('p', {
      className: 'df-authoring-section-hint df-test-detail-intro',
      text: description,
    }));
  }
  parent.append(group);
  return group;
}

function arrayTextEditor(parent, title, values, onChange, placeholder) {
  const control = textarea(list(values).join('\n'), (text) => onChange(splitLines(text)), { placeholder, rows: 3 });
  parent.append(labelField(title, control, 'One item per line.'));
}

function createScenarioEditor(parent, plan, update) {
  const host = section(parent, 'Scenarios', 'Describe the intent; scenarios never become executable steps.');
  const cards = el('div', { className: 'df-authoring-cards' });
  list(plan.scenarios).forEach((scenario, index) => {
    const card = el('fieldset', { className: 'df-authoring-card' });
    card.append(el('legend', { text: `Scenario ${index + 1}` }));
    const change = (property, value) => {
      const next = clone(plan);
      next.scenarios[index][property] = value;
      update(next);
    };
    card.append(labelField('Scenario ID', input(scenario.scenarioId, (value) => change('scenarioId', value), { placeholder: 'scenario-login' })));
    card.append(labelField('Description', textarea(scenario.description, (value) => change('description', value), { placeholder: 'What this scenario demonstrates.' })));
    card.append(labelField(
      'Acceptance criterion links',
      input(list(scenario.acceptanceCriterionIds).join(', '), (value) => change('acceptanceCriterionIds', splitCsv(value)), { placeholder: 'criterion-login' }),
      'Comma-separated criterion IDs.'
    ));
    action(card, 'Remove scenario', () => {
      const next = clone(plan);
      next.scenarios.splice(index, 1);
      update(next, true);
    });
    cards.append(card);
  });
  host.append(cards);
  action(host, 'Add scenario', () => {
    const next = clone(plan);
    next.scenarios.push({
      scenarioId: `scenario-${next.scenarios.length + 1}`,
      description: '',
      acceptanceCriterionIds: [],
      risks: [],
    });
    update(next, true);
  });
}

function createPreconditionEditor(parent, plan, update) {
  const host = section(parent, 'Preconditions', 'Conditions a host must establish or verify before a future run.');
  const cards = el('div', { className: 'df-authoring-cards' });
  list(plan.preconditions).forEach((precondition, index) => {
    const card = el('fieldset', { className: 'df-authoring-card' });
    card.append(el('legend', { text: `Precondition ${index + 1}` }));
    const change = (property, value) => {
      const next = clone(plan);
      next.preconditions[index][property] = value;
      update(next);
    };
    card.append(labelField('ID', input(precondition.preconditionId, (value) => change('preconditionId', value), { placeholder: 'signed-out' })));
    card.append(labelField('Description', textarea(precondition.description, (value) => change('description', value), { placeholder: 'App starts signed out.' })));
    const required = el('input', { type: 'checkbox', checked: precondition.required !== false });
    required.addEventListener('change', () => change('required', required.checked));
    card.append(labelField('Required', required));
    action(card, 'Remove precondition', () => {
      const next = clone(plan);
      next.preconditions.splice(index, 1);
      update(next, true);
    });
    cards.append(card);
  });
  host.append(cards);
  action(host, 'Add precondition', () => {
    const next = clone(plan);
    next.preconditions.push({ preconditionId: `precondition-${next.preconditions.length + 1}`, description: '', required: true });
    update(next, true);
  });
}

function createCriteriaEditor(parent, plan, update) {
  const host = section(parent, 'Acceptance criteria', 'Required criteria should be linked from at least one semantic step.');
  const cards = el('div', { className: 'df-authoring-cards' });
  list(plan.acceptanceCriteria).forEach((criterion, index) => {
    const card = el('fieldset', { className: 'df-authoring-card' });
    card.append(el('legend', { text: `Criterion ${index + 1}` }));
    const change = (property, value) => {
      const next = clone(plan);
      next.acceptanceCriteria[index][property] = value;
      update(next);
    };
    card.append(labelField('Criterion ID', input(criterion.criterionId, (value) => change('criterionId', value), { placeholder: 'criterion-login' })));
    card.append(labelField('Description', textarea(criterion.description, (value) => change('description', value), { placeholder: 'Observable outcome.' })));
    card.append(labelField('Independent oracle ID', input(criterion.businessOracleId, (value) => change('businessOracleId', value), { placeholder: 'account-session' })));
    const required = el('input', { type: 'checkbox', checked: criterion.required !== false });
    required.addEventListener('change', () => change('required', required.checked));
    card.append(labelField('Required', required));
    action(card, 'Remove criterion', () => {
      const next = clone(plan);
      next.acceptanceCriteria.splice(index, 1);
      update(next, true);
    });
    cards.append(card);
  });
  host.append(cards);
  action(host, 'Add acceptance criterion', () => {
    const next = clone(plan);
    next.acceptanceCriteria.push({
      criterionId: `criterion-${next.acceptanceCriteria.length + 1}`,
      description: '',
      required: true,
    });
    update(next, true);
  });
}

function createPolicyEditor(parent, plan, update) {
  const host = section(parent, 'Reset, side effects, platform scope, and oracles');
  const policy = el('select', { className: 'df-authoring-select' });
  for (const [value, text] of [
    ['none', 'No side effects'],
    ['app-state-resettable', 'App state resettable'],
    ['test-tenant-resettable', 'Test tenant resettable'],
    ['compensated', 'Compensated'],
    ['non-replayable', 'Non-replayable'],
  ]) {
    const option = el('option', { value, text });
    option.selected = plan.sideEffectPolicy === value;
    policy.append(option);
  }
  policy.value = plan.sideEffectPolicy || 'none';
  policy.addEventListener('change', () => {
    const next = clone(plan);
    next.sideEffectPolicy = policy.value;
    update(next);
  });
  host.append(labelField('Side-effect policy', policy, 'This describes replay admission; saving never starts a run.'));

  const reset = plan.reset || {};
  const resetGroup = el('div', { className: 'df-authoring-grid' });
  const resetChange = (property, value) => {
    const next = clone(plan);
    next.reset = next.reset || {};
    next.reset[property] = value;
    update(next);
  };
  resetGroup.append(
    labelField('Reset strategy', input(reset.strategy, (value) => resetChange('strategy', value), { placeholder: 'fixture reset' })),
    labelField('Reset identity', input(reset.resetIdentity, (value) => resetChange('resetIdentity', value), { placeholder: 'sample-reset-v1' })),
    labelField('App seed fingerprint', input(reset.seedFingerprint, (value) => resetChange('seedFingerprint', value), { placeholder: 'sha256:…' })),
    labelField('Backend fingerprint', input(reset.backendStateFingerprint, (value) => resetChange('backendStateFingerprint', value), { placeholder: 'sha256:…' })),
  );
  host.append(resetGroup);

  const platformChange = (value) => {
    const next = clone(plan);
    next.requiredPlatforms = splitCsv(value);
    update(next);
  };
  host.append(labelField(
    'Required platforms',
    input(list(plan.requiredPlatforms).join(', '), platformChange, { placeholder: 'android, windows' }),
    'Comma-separated platform scope. This additive field is preserved by test-plan-v1.'
  ));
  const capabilityChange = (value) => {
    const next = clone(plan);
    next.requirements = next.requirements || {};
    next.requirements.requiredCapabilities = splitCsv(value).map((name) => ({ name, required: true }));
    update(next);
  };
  host.append(labelField(
    'Required capabilities',
    input(list(plan.requirements?.requiredCapabilities).map((item) => item.name).filter(Boolean).join(', '), capabilityChange, { placeholder: 'navigation, visual-tree' }),
    'Comma-separated capabilities required by a future runner.'
  ));

  const oracle = list(plan.independentBusinessOracles)[0] || list(plan.businessOracles)[0] || {};
  const oracleChange = (property, value) => {
    const next = clone(plan);
    next.independentBusinessOracles = list(next.independentBusinessOracles);
    if (!next.independentBusinessOracles.length) next.independentBusinessOracles.push({ required: true, independent: true });
    next.independentBusinessOracles[0][property] = value;
    update(next);
  };
  const oracleGroup = el('div', { className: 'df-authoring-grid' });
  oracleGroup.append(
    labelField('Independent oracle ID', input(oracle.oracleId, (value) => oracleChange('oracleId', value), { placeholder: 'order-recorded' })),
    labelField('Oracle requirement', input(oracle.description, (value) => oracleChange('description', value), { placeholder: 'Independent backend confirmation' })),
  );
  host.append(oracleGroup);

  const budget = plan.explorationBudget || {};
  const budgetChange = (property, value) => {
    const next = clone(plan);
    next.explorationBudget = next.explorationBudget || {};
    next.explorationBudget[property] = value;
    update(next);
  };
  const budgetGroup = el('div', { className: 'df-authoring-grid' });
  budgetGroup.append(
    labelField('Exploration max actions', input(budget.maxActions, (value) => budgetChange('maxActions', Math.max(0, Number(value) || 0)), { type: 'number', placeholder: '0' })),
    labelField('Exploration max duration (seconds)', input(budget.maxDurationSeconds, (value) => budgetChange('maxDurationSeconds', Math.max(0, Number(value) || 0)), { type: 'number', placeholder: '0' })),
    labelField('Exploration scopes', input(list(budget.allowedScopes).join(', '), (value) => budgetChange('allowedScopes', splitCsv(value)), { placeholder: 'safe-ui, test-data' })),
  );
  host.append(el('h5', { text: 'Exploration limits' }), budgetGroup);
}

function createProvenanceEditor(parent, plan, update) {
  const host = section(parent, 'Provenance and review');
  const provenance = plan.provenance || {};
  const change = (property, value) => {
    const next = clone(plan);
    next.provenance = next.provenance || {};
    next.provenance[property] = value;
    update(next);
  };
  const grid = el('div', { className: 'df-authoring-grid' });
  grid.append(
    labelField('Actor kind', input(provenance.actorKind, (value) => change('actorKind', value), { placeholder: 'human' })),
    labelField('Actor ID', input(provenance.actorId, (value) => change('actorId', value), { placeholder: 'optional local identity' })),
    labelField('Channel', input(provenance.channel, (value) => change('channel', value), { placeholder: 'inspector' })),
    labelField('Provider', input(provenance.provider, (value) => change('provider', value), { placeholder: 'none' })),
  );
  host.append(grid);
  host.append(el('p', {
    className: 'df-authoring-section-hint',
    text: `Revision ${plan.revision || 'draft'} · ${list(plan.reviews).length} review record(s) · ${list(plan.approvals).length} approval record(s).`,
  }));
}

export function renderPlanPanel(helpers) {
  const root = helpers.root('Goal');
  const authoring = helpers.authoring;
  if (!authoring || typeof authoring.state !== 'function') {
    helpers.intro(root, 'Add a Goal, then record the actions that prove it.');
    return root;
  }

  const draft = authoring.state();
  let plan = clone(draft.plan || defaultPlan(draft));
  const goalComplete = !!String(plan.goal || '').trim();
  const firstTest = !goalComplete && !draft.flow && !draft.markdown && draft.savedBundle !== true;
  const artifactMissing = list(draft.errors).some((value) =>
    /workflow test no longer exists|workflow artifact no longer exists|flow-not-found/i.test(String(value)));

  const update = (next, rerender = false) => {
    plan = next;
    authoring.update?.({
      plan: next,
      planJson: JSON.stringify(next, null, 2),
      planDirty: true,
      stale: false,
      errors: [],
      warnings: [],
      issues: [],
      checkPassed: false,
      diffReviewed: false,
      attentionStepSequence: null,
      guidanceMessage: null,
    }, rerender);
  };

  if (draft.stale || artifactMissing) {
    const recovery = section(
      root,
      artifactMissing ? 'Saved test unavailable' : 'Saved test changed',
      artifactMissing
        ? 'The saved file is no longer available. Your current draft is still preserved.'
        : 'The saved version changed after this draft was opened. Reload it before saving, or keep the draft as a download.'
    );
    recovery.classList.add('df-workbench-recovery');
    action(recovery, 'Reload saved test', () => authoring.reloadSavedTest?.(), { primary: true });
    action(recovery, 'Download current draft', () => authoring.downloadTestDraft?.());
  }

  if (firstTest) {
    const welcome = section(
      root,
      'Create your first test',
      'Describe what should work, then demonstrate it in the app. DevFlow turns your actions into a test you can review and run again.'
    );
    welcome.classList.add('df-onboarding-callout');
    welcome.append(el('p', {
      className: 'df-workbench-note',
      text: 'You can create it here or ask your coding agent to prepare the draft. Nothing is saved or run until you review it.',
    }));
  } else {
    helpers.intro(root, 'Describe the outcome this test must prove. This is the only required field before adding steps.');
  }
  root.classList.toggle('df-goal-incomplete', !goalComplete);
  const goalStart = el('section', { className: 'df-goal-start' });
  if (draft.savedTestPickerOpen) {
    const picker = section(
      root,
      'Open saved test',
      draft.savedTestsLoading
        ? 'Loading tests from this project…'
        : 'Choose a test from this project, or open a Markdown test file.'
    );
    picker.classList.add('df-saved-test-picker');
    if (draft.savedTestsError) {
      picker.append(el('p', { className: 'df-workbench-safety', text: draft.savedTestsError }));
    }
    const tests = list(draft.savedTests);
    const select = el('select', {
      className: 'df-authoring-select',
      id: 'df-saved-test-select',
      'aria-label': 'Saved project test',
      disabled: draft.savedTestsLoading || !tests.length,
    });
    select.append(el('option', {
      value: '',
      text: draft.savedTestsLoading
        ? 'Loading tests…'
        : tests.length
          ? 'Choose a saved test…'
          : 'No saved tests found',
    }));
    for (const test of tests) {
      select.append(el('option', {
        value: test.name,
        text: test.name,
        title: test.modifiedAt ? `Modified ${new Date(test.modifiedAt).toLocaleString()}` : test.name,
      }));
    }
    picker.append(select);
    const pickerActions = el('div', { className: 'df-authoring-actions' });
    const open = action(pickerActions, 'Open test', () => authoring.loadSavedTest?.(select.value), {
      primary: true,
      disabled: true,
    });
    open.id = 'df-saved-test-open';
    select.addEventListener('change', () => {
      open.disabled = !select.value;
    });
    const chooseFile = action(pickerActions, 'Choose Markdown file', () => authoring.chooseSavedTestFile?.());
    chooseFile.id = 'df-saved-test-file';
    const cancel = action(pickerActions, 'Cancel', () => authoring.closeSavedTestPicker?.());
    cancel.id = 'df-saved-test-cancel';
    picker.append(pickerActions);
  }
  const goal = textarea(plan.goal, (value) => {
    const next = clone(plan);
    next.goal = value;
    authoring.noteGoalDefined?.(value);
    update(next);
  }, {
    id: 'df-goal-input',
    placeholder: 'What must this test prove?',
    rows: 3,
    required: true,
    ariaInvalid: String(!goalComplete),
    ariaDescribedBy: 'df-goal-help df-goal-error',
  });
  goal.addEventListener('input', () => {
    const ready = !!goal.value.trim();
    goal.setAttribute('aria-invalid', String(!ready));
    root.classList.toggle('df-goal-incomplete', !ready);
  });
  goalStart.append(labelField('What should this test prove? (required)', goal));
  goalStart.append(el('p', {
    id: 'df-goal-help',
    className: 'df-authoring-field-hint',
    text: 'For example: Adding a todo updates the count and shows the new item.',
  }));
  const goalError = el('p', {
    id: 'df-goal-error',
    className: `df-goal-message${goalComplete ? '' : ' df-goal-message-required'}`,
    role: goalComplete ? 'status' : 'alert',
    text: goalComplete
      ? draft.guidanceMessage === 'Enter a Goal before recording steps.'
        ? 'Goal ready. Add the steps that prove it.'
        : draft.guidanceMessage || 'Goal ready. Add the steps that prove it.'
      : draft.guidanceMessage || 'Enter a Goal before recording steps.',
  });
  goalStart.append(goalError);
  const quickActions = el('div', { className: 'df-authoring-actions' });
  const record = action(quickActions, draft.recording ? 'Stop recording' : 'Record steps', () => {
    if (draft.recording) authoring.stopRecording?.();
    else authoring.startRecording?.();
  }, {
    primary: !draft.stale && !artifactMissing,
    disabled: draft.stale || artifactMissing || !goalComplete || !draft.canDrive || !!draft.recordingStopping,
    title: 'Record steps for this Goal. Recording never starts a run.',
  });
  if (draft.stale || artifactMissing) record.classList.add('df-workbench-action-secondary');
  record.setAttribute('aria-describedby', 'df-goal-help df-goal-error');
  goal.addEventListener('input', () => {
    const ready = !!goal.value.trim();
    record.disabled = draft.stale || artifactMissing ||
      !ready || !draft.canDrive || !!draft.recordingStopping;
    goalError.classList.toggle('df-goal-message-required', !ready);
    goalError.setAttribute('role', ready ? 'status' : 'alert');
    goalError.textContent = ready
      ? draft.guidanceMessage && draft.guidanceMessage !== 'Enter a Goal before recording steps.'
        ? draft.guidanceMessage
        : 'Goal ready. Add the steps that prove it.'
      : 'Enter a Goal before recording steps.';
  });
  const openSaved = action(quickActions, 'Open saved test', () => authoring.openSavedTest?.(), {
    disabled: !!draft.recording || !!draft.recordingStopping,
  });
  openSaved.classList.add('df-workbench-action-secondary');
  goalStart.append(quickActions);
  helpers.agentAction?.(goalStart, {
    title: 'Prepare a draft with your agent',
    description: 'Ask your host agent to prepare a bounded draft. This preview can review or reject agent requests, but trusted approval is not available yet.',
    prompt: () => agentPreparationPrompt(plan.goal),
  });
  if (!draft.savedTestPickerOpen) root.append(goalStart);

  findings(root, 'Check test issues', draft.errors, 'error');
  findings(root, 'Warnings', draft.warnings, 'warning');

  const identity = optionalGroup(
    root,
    'Name and file (optional)',
    'Use a custom display name or Markdown filename only when the defaults are not enough.'
  );
  const identitySettings = section(identity, 'Test identity');
  identitySettings.append(labelField(
    'Test name',
    input(plan.title, (value) => {
      const next = clone(plan);
      next.title = value;
      update(next);
    }, { id: 'df-test-name-input', placeholder: 'Optional display name' }),
    'Optional. The Goal remains the required description.'
  ));
  identitySettings.append(labelField(
    'Flow filename',
    input(draft.flowName, (value) => authoring.update?.({ flowName: value.trim(), planDirty: true }, false), { placeholder: 'login.md' }),
    'Top-level .md name only.'
  ));

  const outcomes = optionalGroup(
    root,
    'Scenarios and outcomes (optional)',
    'Add structured scenarios or acceptance criteria when the Goal alone is not specific enough.'
  );
  createScenarioEditor(outcomes, plan, update);
  createCriteriaEditor(outcomes, plan, update);

  const setup = optionalGroup(
    root,
    'Setup, safety, and platforms (optional)',
    'Declare assumptions, reset rules, side effects, platform scope, or exploration limits only when they affect execution.'
  );
  const constraints = section(setup, 'Assumptions and constraints');
  arrayTextEditor(constraints, 'Assumptions', plan.assumptions, (value) => {
    const next = clone(plan);
    next.assumptions = value;
    update(next);
  }, 'Known test assumptions');
  arrayTextEditor(constraints, 'Risk tags', plan.risks, (value) => {
    const next = clone(plan);
    next.risks = value;
    update(next);
  }, 'Potential risks');
  arrayTextEditor(constraints, 'Prohibited actions', plan.prohibitedActionClasses, (value) => {
    const next = clone(plan);
    next.prohibitedActionClasses = value;
    update(next);
  }, 'Examples: payment, production-write');
  createPreconditionEditor(setup, plan, update);
  createPolicyEditor(setup, plan, update);

  const review = optionalGroup(
    root,
    'Review metadata (optional)',
    'Record provenance and review context only when this test needs an audit trail.'
  );
  createProvenanceEditor(review, plan, update);

  helpers.safety(root);
  return root;
}
