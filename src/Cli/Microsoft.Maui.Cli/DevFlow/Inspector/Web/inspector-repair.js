export function renderRepairPanel(helpers) {
  const root = helpers.root('Repair');
  const repair = helpers.repair;
  const state = repair?.state?.() || {};
  const proposal = state.proposal?.proposal || state.proposal || null;
  const eligibility = state.eligibility;
  const hasFailedRun = !!state.current?.report?.failure;
  const lifecycle = state.proposal?.state || proposal?.state || null;
  const canMutate = state.canMutate === true;
  const approvalAvailable = state.approvalAvailable === true;
  const validationAvailable = state.validationAvailable !== false;
  const reasons = Array.isArray(eligibility?.reasons) ? eligibility.reasons : [];
  const ambiguous = eligibility?.failureCode === 'locator-ambiguous' ||
    reasons.some((reason) => String(reason?.code || '').includes('locator-ambiguous'));

  if (!hasFailedRun && !eligibility && !proposal)
    helpers.intro(root, 'Repair becomes available after a local test cannot find one control.');
  helpers.safety(root);

  if (state.error) {
    const error = document.createElement('p');
    error.className = 'df-workbench-safety';
    error.textContent = state.error;
    root.append(error);
  }

  if (!hasFailedRun && !eligibility && !proposal) {
    const empty = document.createElement('section');
    empty.className = 'df-authoring-section df-tool-empty-state';
    const heading = document.createElement('h4');
    heading.textContent = 'Nothing to repair yet';
    const message = document.createElement('p');
    message.className = 'df-workbench-intro';
    message.textContent = 'Run a saved test first. If it cannot find a control, return here or ask your agent to diagnose the failure.';
    empty.append(heading, message);
    helpers.action(empty, 'Open Results', () => helpers.selectStage?.('results'));
    root.append(empty);
  } else if (!eligibility && !proposal) {
    const classify = helpers.action(
      root,
      state.classifying ? 'Checking failure…' : 'Check latest failure',
      () => repair?.classify?.()
    );
    classify.classList.add('df-authoring-primary');
    classify.disabled = state.classifying || !repair;
    helpers.agentAction?.(root, {
      title: 'Diagnose with your agent',
      description: 'Send an exact, time-limited handoff for this failed run. The agent cannot apply a change.',
      prompt: () => helpers.run?.prepareFailureAgentPrompt?.(),
    });
  } else if (eligibility?.eligible !== true && !proposal) {
    const unavailable = document.createElement('section');
    unavailable.className = 'df-authoring-section df-tool-empty-state';
    const heading = document.createElement('h4');
    heading.textContent = ambiguous ? 'Choose the intended control yourself' : 'This failure cannot use selector repair';
    const message = document.createElement('p');
    message.className = 'df-workbench-intro';
    message.textContent = ambiguous
      ? 'The selector matched more than one control, so DevFlow will not guess.'
      : reasons[0]?.message || 'The failure did not meet the safe selector-only repair rules.';
    unavailable.append(heading, message);
    helpers.action(unavailable, 'Open Results', () => helpers.selectStage?.('results'));
    if (ambiguous) helpers.action(unavailable, 'Resolve in Improve', () => helpers.selectTab?.('improve'));
    root.append(unavailable);
  } else if (eligibility?.eligible === true && !proposal) {
    const ready = document.createElement('section');
    ready.className = 'df-authoring-section df-tool-ready-state';
    const heading = document.createElement('h4');
    heading.textContent = 'A safe test update may be possible';
    const message = document.createElement('p');
    message.className = 'df-workbench-intro';
    message.textContent = 'Create a suggested control update to review. Nothing changes automatically.';
    ready.append(heading, message);
    const propose = helpers.action(
      ready,
      state.proposing ? 'Creating suggestion…' : 'Create suggested update',
      () => repair?.propose?.()
    );
    propose.classList.add('df-authoring-primary');
    propose.disabled = state.proposing || !repair;
    root.append(ready);
  }

  if (proposal) {
    const candidate = proposal.candidate;
    const proposalCard = document.createElement('section');
    proposalCard.className = 'df-authoring-section df-tool-ready-state';
    const heading = document.createElement('h4');
    heading.textContent = `Suggested test update · ${String(lifecycle || 'proposed').replace(/-/g, ' ')}`;
    proposalCard.append(heading);
    const next = document.createElement('p');
    next.className = 'df-workbench-intro';
    next.textContent = lifecycle === 'proposed'
      ? 'Review exactly which control this step would use.'
      : lifecycle === 'previewed' && !(proposal.validationRunIds?.length)
        ? 'Try the suggested control in a reset test run without saving it.'
        : lifecycle === 'previewed'
          ? 'The suggestion worked in validation. Approve the reviewed update when ready.'
          : lifecycle === 'approved'
            ? 'Apply the approved test update.'
            : lifecycle === 'applied'
              ? 'The change is applied; verification is still required.'
              : 'Review the current repair state.';
    proposalCard.append(next);

    const agentOriginated = state.proposal?.agentOriginated === true;
    if (agentOriginated) {
      const notice = document.createElement('p');
      notice.className = 'df-workbench-safety';
      notice.textContent = 'Your agent prepared this suggestion. You may review or validate it, but agent-originated suggestions are never applied directly.';
      proposalCard.append(notice);
    }

    if (lifecycle === 'proposed') {
      const preview = helpers.action(proposalCard, 'Review suggested update', () => repair?.preview?.());
      preview.classList.add('df-authoring-primary');
    } else if (lifecycle === 'previewed' && !(proposal.validationRunIds?.length)) {
      if (validationAvailable && approvalAvailable) {
        const validate = helpers.action(proposalCard, 'Try this update', () => repair?.validate?.());
        validate.classList.add('df-authoring-primary');
        validate.disabled = !repair || !canMutate;
      } else if (!approvalAvailable) {
        const unavailable = document.createElement('p');
        unavailable.className = 'df-workbench-safety';
        unavailable.textContent = 'Native approval is unavailable in this preview. A trusted native host confirmation cannot be made here, and the browser cannot mint the temporary validation grant.';
        proposalCard.append(unavailable);
      } else {
        const unavailable = document.createElement('p');
        unavailable.className = 'df-workbench-note';
        unavailable.textContent = 'Transient validation is unavailable until a lifecycle-capable host is connected.';
        proposalCard.append(unavailable);
      }
    } else if (lifecycle === 'previewed' && proposal.validationRunIds?.length && !agentOriginated) {
      if (approvalAvailable) {
        const approval = helpers.action(proposalCard, 'Approve update', () => repair?.requestApproval?.());
        approval.classList.add('df-authoring-primary');
        approval.disabled = !repair || !canMutate;
      } else {
        const unavailable = document.createElement('p');
        unavailable.className = 'df-workbench-safety';
        unavailable.textContent = 'Native approval is unavailable in this preview. A trusted native host confirmation cannot be made here; browser or chat approval cannot issue the broker grant.';
        proposalCard.append(unavailable);
      }
    } else if (lifecycle === 'approved' && !agentOriginated) {
      const unavailable = document.createElement('p');
      unavailable.className = 'df-workbench-safety';
      unavailable.textContent = 'No trusted native-host approval client is available yet, so this exact digest cannot be applied here. The browser does not receive the grant.';
      proposalCard.append(unavailable);
    } else if (['applying', 'applied'].includes(lifecycle)) {
      const refresh = helpers.action(proposalCard, 'Refresh update status', () => repair?.refresh?.());
      refresh.classList.add('df-authoring-primary');
    }
    if (lifecycle === 'previewed') {
      helpers.action(proposalCard, 'Reject', () => repair?.reject?.());
    }
    root.append(proposalCard);

    const proposalDetails = document.createElement('details');
    proposalDetails.className = 'df-tool-details';
    const detailSummary = document.createElement('summary');
    detailSummary.textContent = 'Technical repair details';
    proposalDetails.append(detailSummary);
    if (proposal.diff?.markdown) {
      const label = document.createElement('h4');
      label.textContent = 'Selector-only diff';
      const pre = document.createElement('pre');
      pre.className = 'df-workbench-diff';
      pre.textContent = proposal.diff.markdown;
      proposalDetails.append(label, pre);
    }
    if (candidate) {
      const kind = candidate.selectorDescriptor?.kind || candidate.origin || 'unknown';
      const score = candidate.score ?? candidate.scores?.deterministicRankScore ?? 'n/a';
      const line = document.createElement('p');
      line.className = 'df-workbench-note';
      line.textContent = `Candidate ${candidate.candidateId || 'unknown'} · ${kind} · unique ${candidate.unique === true || candidate.validation?.unique === true ? 'yes' : 'no'} · score ${score}.`;
      proposalDetails.append(line);
    }
    const proof = proposal.unchangedAssertionsProof;
    if (proof) {
      helpers.list(proposalDetails, [
        `Checks unchanged: ${proof.unchanged === true ? 'proved' : 'not proved'}`,
        `Actions unchanged: ${proof.actionsUnchanged === true ? 'proved' : 'not proved'}`,
        `Values unchanged: ${proof.valuesUnchanged === true ? 'proved' : 'not proved'}`,
        `Order unchanged: ${proof.orderUnchanged === true ? 'proved' : 'not proved'}`,
      ]);
    }
    root.append(proposalDetails);
  }

  if (eligibility) {
    const eligibilityDetails = document.createElement('details');
    eligibilityDetails.className = 'df-tool-details';
    const summary = document.createElement('summary');
    summary.textContent = 'Failure classification details';
    eligibilityDetails.append(summary);
    if (reasons.length) {
      helpers.list(eligibilityDetails, reasons.map((reason) =>
        `${reason.code || 'reason'}: ${reason.message || 'No explanation supplied.'}`));
    }
    if (state.checkpoint) {
      const checkpoint = document.createElement('p');
      checkpoint.className = 'df-workbench-note';
      checkpoint.textContent = `Checkpoint: route ${state.checkpoint.route || 'missing'} · window ${state.checkpoint.window || 'missing'} · build ${state.checkpoint.appBuildFingerprint || 'missing'}.`;
      eligibilityDetails.append(checkpoint);
    }
    root.append(eligibilityDetails);
  }

  const policy = document.createElement('details');
  policy.className = 'df-tool-details';
  const policySummary = document.createElement('summary');
  policySummary.textContent = 'How this stays safe';
  policy.append(policySummary);
  helpers.list(policy, [
    'Only a current local missing-selector failure can qualify.',
    'Validation uses a temporary selector and does not commit a flow change.',
    'Applying changes one selector only; actions, checks, values, order, and source stay unchanged.',
    'Verification failure requires explicit rollback handling.',
  ]);
  helpers.capability(policy, 'openSourceDiff');
  root.append(policy);
  return root;
}
