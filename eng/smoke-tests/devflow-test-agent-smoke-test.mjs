#!/usr/bin/env node
// End-to-end verification of the restricted test-agent loop, spoken over the same MCP protocol an
// editor uses. It exists because editors have repeatedly failed to dispatch these tools, and a
// failure there is indistinguishable, from the outside, from the product being broken. This
// answers that question directly: it needs no editor, no chat model, and no human.
//
// Requires a connected Debug DevFlow agent whose app registers the well-known reset action, and a
// broker started with DEVFLOW_PREVIEW_AGENT_AUTHORING=true and DEVFLOW_PREVIEW_REPAIR_PROPOSALS=true.
//
//   node eng/smoke-tests/devflow-test-agent-smoke-test.mjs [--maui <path-to-maui>]
//
// Exit code 0 means the whole loop worked: author, live validate, commit, an admitted run that
// reached a terminal state, a classified selector failure, and an accepted repair proposal.
import { spawn } from 'node:child_process';
import process from 'node:process';

const argv = process.argv.slice(2);
const mauiArg = argv.indexOf('--maui');
const EXE = mauiArg >= 0 ? argv[mauiArg + 1] : (process.env.MAUI_EXE ?? 'maui');

const steps = [];
let failed = false;
const step = (name, ok, detail) => {
  steps.push({ name, ok, detail });
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? `  — ${detail}` : ''}`);
  if (!ok) failed = true;
  return ok;
};

const child = spawn(EXE, ['devflow', 'mcp', '--profile', 'test-agent'], {
  stdio: ['pipe', 'pipe', 'pipe'],
  env: {
    ...process.env,
    DEVFLOW_PREVIEW_AGENT_AUTHORING: 'true',
    DEVFLOW_PREVIEW_REPAIR_PROPOSALS: 'true',
  },
});

let nextId = 1;
const pending = new Map();
let buffer = '';
child.stdout.on('data', chunk => {
  buffer += chunk.toString('utf8');
  let index;
  while ((index = buffer.indexOf('\n')) >= 0) {
    const line = buffer.slice(0, index).trim();
    buffer = buffer.slice(index + 1);
    if (!line) continue;
    let message;
    try { message = JSON.parse(line); } catch { continue; }
    const resolve = pending.get(message.id);
    if (resolve) { pending.delete(message.id); resolve(message); }
  }
});

const send = (method, params) => new Promise((resolve, reject) => {
  const id = nextId++;
  pending.set(id, resolve);
  child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
  setTimeout(() => {
    if (pending.delete(id)) reject(new Error(`timed out waiting for ${method}`));
  }, 180000);
});

const call = async (name, args) => {
  const response = await send('tools/call', { name, arguments: args });
  if (response.error) return { transportError: response.error };
  const text = response.result?.content?.map(part => part.text ?? '').join('') ?? '';
  try { return JSON.parse(text); } catch { return { raw: text }; }
};

const sleep = ms => new Promise(r => setTimeout(r, ms));
let seq = 0;
const envelope = (intent, extra = {}) => ({
  schema: 1,
  requestId: `smoke-${intent}-${Date.now()}-${seq++}`,
  idempotencyKey: `smoke-idem-${intent}-${Date.now()}-${seq++}`,
  intent,
  policyVersion: 'test-agent-policy-v1',
  deadlineMs: 120000,
  provenance: { actorKind: 'agent', actorId: 'devflow-smoke', channel: 'mcp', provider: 'devflow-smoke', intent },
  ...extra,
});

const TITLE = 'Ledger verified item';
const DRIFTED = 'ShowModalButtonRenamed';
const REPAIRED = 'ShowModalButton';
const FLOW_PATH = 'devflow-smoke-drifted-trailing-action.md';

try {
  await send('initialize', {
    protocolVersion: '2024-11-05',
    capabilities: {},
    clientInfo: { name: 'devflow-smoke', version: '1' },
  });
  child.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');

  const listed = await send('tools/list', {});
  const tools = (listed.result?.tools ?? []).map(t => t.name);
  step('the restricted profile advertises its 13 tools', tools.length === 13, `${tools.length} advertised`);

  // Schema size is not cosmetic: hosts virtualise tools once the advertised surface grows, and a
  // virtualised tool has to be discovered before it can be called.
  const biggest = (listed.result?.tools ?? [])
    .map(t => [t.name, JSON.stringify(t.inputSchema ?? {}).length])
    .sort((a, b) => b[1] - a[1])[0];
  const totalSchema = (listed.result?.tools ?? [])
    .reduce((sum, t) => sum + JSON.stringify(t.inputSchema ?? {}).length, 0);
  step('the advertised tool schemas stay small', totalSchema < 40000,
    `total ${totalSchema}, largest ${biggest?.[0]} ${biggest?.[1]}`);

  const agents = await call('maui_test_agents', { request: {} });
  const target0 = agents?.data?.targets?.[0];
  if (!step('a DevFlow agent is connected', Boolean(target0), target0?.appName ?? 'none connected')) {
    throw new Error('no connected agent; start the app with a Debug DevFlow build first');
  }
  const target = {
    agentId: target0.agentId,
    agentInstanceId: target0.agentInstanceId,
    appBuildFingerprint: '1.0:1',
  };

  const plan = {
    schema: 1,
    planId: 'devflow-smoke-plan',
    revision: 1,
    flow: { path: FLOW_PATH },
    title: 'devflow-smoke-drifted-trailing-action',
    goal: 'The todo is committed and independently verified; only a trailing action selector drifted.',
    scenarios: [{
      scenarioId: 'add-todo-is-committed',
      description: 'A user adds a todo and the app commits it, then a trailing action addresses a renamed control.',
      acceptanceCriterionIds: ['todo-committed'],
    }],
    acceptanceCriteria: [{
      criterionId: 'todo-committed',
      description: 'The added todo is committed to the durable ledger, not merely rendered.',
      required: true,
      businessOracleId: 'todo-ledger-record',
    }],
    assumptions: [], risks: [], preconditions: [],
    requiredPlatforms: ['android'],
    checkpoint: { route: '//native', window: 'window-0' },
    sideEffectPolicy: 'app-state-resettable',
    reset: { required: true, strategy: 'app-action-reset' },
    independentBusinessOracles: [{
      oracleId: 'todo-ledger-record',
      description: 'Read the app-private todo ledger over adb and confirm it holds the record the flow added.',
      required: true, independent: true,
      evidenceKind: 'android-app-storage',
      reference: 'files/todo-ledger.jsonl',
      expect: { contains: [`"title":"${TITLE}"`], absent: ['{"event":"todo-removed"'] },
    }],
  };

  const flow = {
    schema: 2,
    name: 'devflow-smoke-drifted-trailing-action',
    app: target0.appName ?? 'MauiTodo',
    platform: 'android',
    preconditions: 'The app is reset to its seeded state by the in-app reset action.',
    steps: [
      { seq: 1, action: 'fill', label: 'Enter the todo text', intent: `Type "${TITLE}"`,
        args: { selector: { automationId: 'NewTodoEntry' }, text: TITLE } },
      { seq: 2, action: 'tap', label: 'Commit the todo', intent: 'Add the todo and verify the count',
        args: { selector: { automationId: 'AddButton' } },
        acceptanceCriterionIds: ['todo-committed'],
        asserts: [{ kind: 'propEquals', selector: { automationId: 'CountLabel' }, name: 'Text',
                    expected: '4 items, 0 completed', verify: true }] },
      { seq: 3, action: 'tap', label: 'Open the modal', intent: 'Trailing action whose selector drifted',
        args: { selector: { automationId: DRIFTED } } },
    ],
  };

  const begun = await call('maui_test_author', {
    operation: 'begin',
    request: { envelope: envelope('smoke-begin', { target }), plan, flow },
  });
  if (!step('an authoring session begins', begun?.ok === true, begun?.error?.message)) {
    throw new Error('author begin failed');
  }

  const snap = begun.data?.snapshot;
  let read = begun.data?.readCapabilityId ?? snap?.readCapabilityId;
  const correlation = {
    authoringSessionId: snap?.sessionId,
    planId: snap?.plan?.planId, planRevision: snap?.plan?.revision,
    flowId: snap?.plan?.flow?.flowId, flowRevision: snap?.plan?.flow?.revision,
    flowDigest: snap?.plan?.flow?.digest, planDigest: snap?.planDigest ?? begun.data?.planDigest,
  };
  const base = extra => envelope(extra.intent, { target, correlation, readCapabilityId: read, ...extra });

  const live = await call('maui_test_validate', {
    request: { envelope: base({ intent: 'smoke-validate' }), mode: 'live' },
  });
  const findings = live?.data?.findings ?? [];
  const byStatus = Object.fromEntries(findings.map(f => [f.selector, f.status]));
  step('live validation resolves the real selectors',
    byStatus['automationId:NewTodoEntry'] === 'unique' && byStatus['automationId:AddButton'] === 'unique',
    JSON.stringify(byStatus));
  step('live validation reports the drifted selector as unresolved',
    byStatus[`automationId:${DRIFTED}`] === 'unresolved');

  // The reset contract digests owner, strategy, app, device, and build, so it is the broker's to
  // state. Copying the offer verbatim is exactly what an author is told to do.
  const offer = live?.data?.resetOffer;
  if (!step('the broker offers a reset contract to declare', offer?.ownerAvailable === true, offer?.note)) {
    throw new Error('no reset owner; the app must register the devflow-reset action');
  }

  const approve = async (kind, intent, actions) => {
    const ask = await call('maui_test_author', {
      operation: 'approval-request',
      request: {
        envelope: base({ intent }),
        approvalKind: kind,
        approvalScope: {
          allowedActions: actions,
          allowedRoutes: ['//native'],
          allowedSideEffectClasses: ['app-state'],
          maxActionCount: 1,
        },
        approvalExpiresAt: new Date(Date.now() + 4 * 60000).toISOString(),
      },
    });
    const id = ask?.data?.approval?.approvalRequestId;
    if (!id) return { error: ask?.error?.message ?? 'no approvalRequestId' };
    const waited = await call('maui_test_author', {
      operation: 'await-approval',
      request: { envelope: base({ intent }), approvalRequestId: id, waitTimeoutSeconds: 90 },
    });
    return { id, grant: waited?.data?.approval?.grantId, state: waited?.data?.approval?.state };
  };

  // Re-declare the reset contract exactly as offered. This starts a fresh authoring session, so
  // every correlated value has to be rebound from it; keeping the probe session's ids would commit
  // the plan that did not carry the seed.
  plan.reset.resetIdentity = offer.resetIdentity;
  plan.reset.seedFingerprint = offer.seedFingerprint;
  const rebegun = await call('maui_test_author', {
    operation: 'begin',
    request: { envelope: envelope('smoke-rebegin', { target }), plan, flow },
  });
  if (!step('the reset contract from the offer is accepted', rebegun?.ok === true, rebegun?.error?.message))
    throw new Error('re-begin with the offered reset contract failed');

  const rsnap = rebegun.data?.snapshot;
  read = rebegun.data?.readCapabilityId ?? rsnap?.readCapabilityId;
  correlation.authoringSessionId = rsnap?.sessionId;
  correlation.planId = rsnap?.plan?.planId;
  correlation.planRevision = rsnap?.plan?.revision;
  correlation.flowId = rsnap?.plan?.flow?.flowId;
  correlation.flowRevision = rsnap?.plan?.flow?.revision;
  correlation.flowDigest = rsnap?.plan?.flow?.digest;
  correlation.planDigest = rsnap?.planDigest ?? rebegun.data?.planDigest;

  const commitApproval = await approve('commit', 'smoke-commit', ['author-commit']);
  if (!step('a human commit approval issues a grant', Boolean(commitApproval.grant),
    commitApproval.state ?? commitApproval.error)) {
    throw new Error('commit approval did not issue a grant; is an approval host running?');
  }

  const committed = await call('maui_test_author', {
    operation: 'commit',
    request: {
      envelope: { ...base({ intent: 'smoke-commit' }), approvalGrantId: commitApproval.grant },
      approvalRequestId: commitApproval.id,
    },
  });
  if (!step('the draft commits', committed?.ok === true, committed?.error?.message))
    throw new Error('commit failed');

  // Commit advances revisions, so everything after it must correlate against the committed snapshot.
  const csnap = committed.data?.snapshot;
  correlation.planRevision = csnap?.plan?.revision ?? correlation.planRevision;
  correlation.flowRevision = csnap?.plan?.flow?.revision ?? correlation.flowRevision;
  correlation.flowDigest = csnap?.plan?.flow?.digest ?? correlation.flowDigest;
  correlation.planDigest = csnap?.planDigest ?? correlation.planDigest;

  const runApproval = await approve('run', 'smoke-run', ['run']);
  if (!step('a human run approval issues a grant', Boolean(runApproval.grant),
    runApproval.state ?? runApproval.error)) {
    throw new Error('run approval did not issue a grant');
  }

  const started = await call('maui_test_run', {
    request: {
      envelope: { ...base({ intent: 'smoke-run' }), approvalGrantId: runApproval.grant },
      operation: 'start', timeoutMs: 120000,
    },
  });
  const runId = started?.data?.runId;
  // Admission runs the declared reset first; a denial here mints no runId at all.
  if (!step('replay admission accepts the run', Boolean(runId), started?.error?.message))
    throw new Error('the run was denied at admission');
  correlation.runId = runId;

  let state;
  for (let i = 0; i < 60; i++) {
    await sleep(3000);
    const status = await call('maui_test_run', {
      request: { envelope: { ...base({ intent: 'smoke-status' }), approvalGrantId: runApproval.grant }, operation: 'status' },
    });
    state = status?.data?.run?.state ?? status?.data?.state;
    if (state && !['running', 'pending', 'queued', 'starting'].includes(String(state))) break;
  }
  step('the run reaches a terminal state', state === 'failed' || state === 'passed', String(state));

  const failure = await call('maui_test_failure', {
    request: { envelope: { ...base({ intent: 'smoke-failure' }), approvalGrantId: runApproval.grant }, runId },
  });
  const classification = failure?.data?.classification;
  step('the failure is classified as a missing selector',
    classification?.class === 'locator-not-found', classification?.class);
  // Eligibility needs the business outcome to have been independently verified, so this also
  // proves the ledger oracle ran and passed.
  step('the run is a sound basis for repair', classification?.repairEligible === true,
    failure?.data?.selectorRepair?.reason);

  const previewed = await call('maui_test_patch', {
    request: {
      envelope: { ...base({ intent: 'smoke-preview' }), approvalGrantId: runApproval.grant },
      operation: 'preview',
      proposal: { sourceStepId: '3', proposedSelector: { automationId: REPAIRED } },
    },
  });
  const canonical = previewed?.data?.record?.proposal ?? previewed?.data?.proposal;
  if (!step('the broker states the canonical patch digest', Boolean(canonical?.patchDigest),
    previewed?.error?.message)) {
    throw new Error('patch preview did not return a digest');
  }

  const proposed = await call('maui_test_patch', {
    request: {
      envelope: { ...base({ intent: 'smoke-proposal' }), approvalGrantId: runApproval.grant },
      operation: 'proposal',
      reason: 'A trailing action selector drifted after the todo was committed.',
      proposal: {
        schema: 1,
        sourceRunId: runId,
        sourceStepId: '3',
        sourceFailureCode: 'locator-not-found',
        preDispatch: true,
        baseFlow: { path: FLOW_PATH, digest: correlation.flowDigest, revision: correlation.flowRevision },
        oldSelector: { automationId: DRIFTED },
        proposedSelector: { automationId: REPAIRED },
        patch: canonical.patch,
        patchDigest: canonical.patchDigest,
        unchangedAssertionsProof: {
          unchanged: true, actionsUnchanged: true, valuesUnchanged: true, orderUnchanged: true,
        },
        provenance: {
          actorKind: 'agent', actorId: 'devflow-smoke', channel: 'mcp',
          provider: 'devflow-smoke', intent: 'smoke-proposal',
        },
      },
    },
  });
  const record = proposed?.data?.record?.proposal ?? proposed?.data?.proposal;
  step('a reviewable selector repair proposal is accepted',
    proposed?.ok === true && Boolean(record?.proposalId), proposed?.error?.message ?? record?.proposalId);
  step('the proposal repairs the drifted selector only',
    record?.oldSelector?.automationId === DRIFTED &&
    record?.proposedSelector?.automationId === REPAIRED &&
    record?.unchangedAssertionsProof?.unchanged === true);
} catch (error) {
  failed = true;
  console.log(`\nstopped: ${error.message}`);
} finally {
  child.stdin.end();
  child.kill();
}

const passed = steps.filter(s => s.ok).length;
console.log(`\n${passed}/${steps.length} checks passed`);
process.exit(failed ? 1 : 0);
